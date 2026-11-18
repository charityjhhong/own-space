using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// <see cref="IRoomInfo"/> read from MRUK's current room. Rebuilt only when MRUK loads, creates,
    /// updates or removes a room, never per frame. With no room loaded every list is empty and
    /// <see cref="HasScan"/> is false, so callers take their no-scan path.
    /// A room counts only when the user's head is inside it (device day #9: in an unscanned room MRUK loaded the
    /// saved scan of another room and drew it misaligned around the user). Otherwise <see cref="InOtherRoom"/> is
    /// true, the no-scan path runs, and the head is re-checked once a second (the pose may not be tracked yet).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class MrukRoomInfo : MonoBehaviour, IRoomInfo
    {
        const string LogTag = "[OwnSpace.MR]";

        readonly List<Vec2> _outline = new List<Vec2>(), _realOutline = new List<Vec2>();
        readonly List<RoomElement> _walls = new List<RoomElement>();
        readonly List<RoomElement> _windows = new List<RoomElement>();
        readonly List<RoomElement> _doors = new List<RoomElement>();
        readonly List<FurnitureAnchor> _furniture = new List<FurnitureAnchor>();
        MRUK _mruk;
        MRUKRoom _rejected;  // a loaded room the head is not in
        MRUKRoom _last;      // the room read last: kept on a reload while the head is still in it
        Transform _head;
        float _nextCheck;
        const float RecheckSeconds = 1f;

        const float SettleSeconds = 3f;

        /// <summary>The used room's anchor UUID ("N" format), or <see cref="RoomSaves.NoScanKey"/> without a usable scan.</summary>
        public string RoomKey { get; private set; } = RoomSaves.NoScanKey;

        /// <summary>
        /// True once the room is known: MRUK has loaded (or failed to), there is no MRUK, or it has not answered within
        /// a few seconds. Changed is raised when it turns true. Saves are keyed to the room only after this.
        /// </summary>
        public bool Settled { get; private set; }

        /// <summary>True when MRUK has a room but the user is not inside it: treated as no scan.</summary>
        public bool InOtherRoom => _rejected != null;

        public bool HasScan { get; private set; }
        public IReadOnlyList<Vec2> FloorOutline => _outline;

        /// <summary>The scanned outline, also while the virtual room is extended past it (<see cref="Extended"/>).</summary>
        public IReadOnlyList<Vec2> RealOutline => Visiting != null ? _outline : _realOutline;

        /// <summary>
        /// The virtual room reaches past the real walls (a small room: <see cref="VirtualExtent"/>). Everything reading
        /// <see cref="FloorOutline"/> (shell, placement, the free-floor check, AI plans) uses the extended outline; the real
        /// walls, windows and doors are not drawn, as in a visit.
        /// </summary>
        public bool Extended { get; private set; }

        /// <summary>Extends the floor outline to <paramref name="outline"/> (null = back to the scan); raises Changed when it changed.</summary>
        public void Extend(IReadOnlyList<Vec2> outline)
        {
            if (Visiting != null || !HasScan) return;
            IReadOnlyList<Vec2> target = outline != null && outline.Count >= 3 ? outline : _realOutline;
            if (Same(target, _outline)) return;
            _outline.Clear();
            for (int i = 0; i < target.Count; i++) _outline.Add(target[i]);
            Extended = !ReferenceEquals(target, _realOutline);
            Debug.Log($"{LogTag} Virtual room {(Extended ? "extended past the real walls" : "back to the real walls")}: {_outline.Count} corners.");
            Changed?.Invoke();
        }

        /// <summary>Tests only: a scanned room with this outline and nothing else.</summary>
        internal void UseForTests(IReadOnlyList<Vec2> outline)
        {
            Clear();
            HasScan = true;
            for (int i = 0; i < outline.Count; i++) { _outline.Add(outline[i]); _realOutline.Add(outline[i]); }
        }

        static bool Same(IReadOnlyList<Vec2> a, IReadOnlyList<Vec2> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (Mathf.Abs(a[i].x - b[i].x) > 1e-4f || Mathf.Abs(a[i].y - b[i].y) > 1e-4f) return false;
            return true;
        }
        public float FloorY { get; private set; }
        public float CeilingY { get; private set; }
        public IReadOnlyList<RoomElement> Walls => _walls;
        public IReadOnlyList<RoomElement> Windows => _windows;
        public IReadOnlyList<RoomElement> Doors => _doors;
        public IReadOnlyList<FurnitureAnchor> Furniture => _furniture;
        public event Action Changed;

        /// <summary>
        /// Where MRUK loads the room from. On a headset only the device: a Meta sample room must never stand in for the
        /// user's room (Main.unity is set to Device by <c>MrukDataSourceSetup</c>). In the Editor the sample rooms stay as
        /// the fallback, so Play mode and the XR Simulator have a room.
        /// </summary>
        public static MRUK.SceneDataSource DataSourceFor(bool inEditor, MRUK.SceneDataSource configured) =>
            inEditor ? (configured == MRUK.SceneDataSource.Device ? MRUK.SceneDataSource.DeviceWithPrefabFallback : configured)
                     : MRUK.SceneDataSource.Device;

        /// <summary>A room counts on a headset only when it came from the device (it has an anchor handle).</summary>
        public static bool Usable(bool inEditor, bool fromDevice) => inEditor || fromDevice;

        /// <summary>
        /// Whether a reload keeps the room read last (pure, for tests). Two scans can overlap and MRUK's "current room"
        /// can flip between them (device: back from a saved space the app read the other scan and showed its empty
        /// design). Keep the last room while it still exists, was not removed, and the head is not known to be outside.
        /// </summary>
        public static bool KeepLast(bool lastAlive, bool lastRemoved, bool? headInsideLast) => lastAlive && !lastRemoved && headInsideLast != false;

        public const int MaxLoadRetries = 5;
        public const float LoadRetrySeconds = 3f;

        /// <summary>
        /// Whether to ask the device for the room again: on a headset, once settled with no room at all (the startup load
        /// failed or came back empty), a few times, <see cref="LoadRetrySeconds"/> apart. Never during a visit, a rescan
        /// or another load.
        /// </summary>
        public static bool ShouldRetryLoad(bool inEditor, bool settled, bool hasScan, bool inOtherRoom, bool visiting,
            bool busy, int retries, float now, float retryAt) =>
            !inEditor && settled && !hasScan && !inOtherRoom && !visiting && !busy && retries < MaxLoadRetries && now >= retryAt;

        /// <summary>The user is there to be asked about: the app is in focus and not paused, and the headset is worn.</summary>
        public static bool Present(bool focused, bool paused, bool worn) => focused && !paused && worn;

        /// <summary>
        /// The tries used so far. They start again when the user comes back (the headset is put on, the app regains
        /// focus): device run of build 0dad52e spent all five while the headset was asleep and then stayed on no-scan.
        /// </summary>
        public static int RetriesOnPresence(bool present, bool wasPresent, int retries) => present && !wasPresent ? 0 : retries;

        /// <summary>A try counts only if the user was there when its answer came.</summary>
        public static int RetriesAfterAnswer(bool present, int retries) => present ? retries : Mathf.Max(0, retries - 1);

        // ---- the first answer: no "no scan" before the device has said so (device, builds X and Y of PR #275: the app
        // settled on no-scan at the first frame, wrote a stray no-scan design, and the room arrived a moment later) ----

        public enum FirstLoad { Wait, Ask, GiveUp }

        /// <summary>The first ask comes this long after start, then once per <see cref="AskEverySeconds"/>; after
        /// <see cref="MaxWaitSeconds"/> with the user there and no answer, the app goes on with no room.</summary>
        public const float FirstAskSeconds = 1.5f, AskEverySeconds = 1f, MaxWaitSeconds = 20f;

        /// <summary>
        /// What to do while the room is not known yet. On a headset: ask the device (the answer is "still loading", a
        /// room, or no room), and give up only after <see cref="MaxWaitSeconds"/>. In the Editor: a few seconds, as before.
        /// <paramref name="waited"/> and <paramref name="sinceAsk"/> count only while the user is there.
        /// </summary>
        public static FirstLoad WhileUnsettled(bool inEditor, float waited, bool asking, float sinceAsk)
        {
            if (inEditor) return waited >= SettleSeconds ? FirstLoad.GiveUp : FirstLoad.Wait;
            if (waited >= MaxWaitSeconds) return FirstLoad.GiveUp;
            return !asking && waited >= FirstAskSeconds && sinceAsk >= AskEverySeconds ? FirstLoad.Ask : FirstLoad.Wait;
        }

        /// <summary>"Still loading" is not an answer; everything else is (a room, no room, no permission, a failure).</summary>
        public static bool IsAnswer(MRUK.LoadDeviceResult result) => result != MRUK.LoadDeviceResult.DiscoveryOngoing;

        float _waited, _sinceAsk = AskEverySeconds;
        bool _asking;

        async void AskFirst()
        {
            _asking = true;
            try
            {
                MRUK.LoadDeviceResult result = await _mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
                if (this == null || Settled) return; // MRUK's own load answered meanwhile
                if (!IsAnswer(result)) return;        // MRUK's startup load is still running: keep waiting
                Debug.Log($"{LogTag} Room first load: {result}, {(_mruk.Rooms != null ? _mruk.Rooms.Count : 0)} room(s) after {_waited:F1} s.");
                if (result == MRUK.LoadDeviceResult.Success) Rebuild(); else Settle();
            }
            catch (Exception e) { Debug.LogWarning($"{LogTag} Room first load threw: {e.Message}"); }
            finally { _asking = false; _sinceAsk = 0f; }
        }

        int _loadRetries;
        float _loadRetryAt;
        bool _loading, _focused = true, _paused, _wasPresent;

        bool IsPresent => Present(_focused, _paused, OVRManager.instance == null || OVRManager.instance.isUserPresent);

        // Runs before MRUK's Awake (execution order -50), which starts the startup load with the data source it finds.
        void Awake()
        {
            MRUK mruk = FindAnyObjectByType<MRUK>();
            if (mruk == null || mruk.SceneSettings == null) return;
            MRUK.SceneDataSource source = DataSourceFor(Application.isEditor, mruk.SceneSettings.DataSource);
            if (source == mruk.SceneSettings.DataSource) return;
            Debug.Log($"{LogTag} MRUK data source {mruk.SceneSettings.DataSource} -> {source}.");
            mruk.SceneSettings.DataSource = source;
        }

        async void RetryLoad()
        {
            _loading = true;
            _loadRetries++;
            try
            {
                MRUK.LoadDeviceResult result = await _mruk.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
                if (this == null) return;
                bool present = IsPresent;
                Debug.Log($"{LogTag} Room load retry {_loadRetries}/{MaxLoadRetries}: {result}, {(_mruk != null && _mruk.Rooms != null ? _mruk.Rooms.Count : 0)} room(s)" +
                          (present ? "." : "; not counted (the headset is not worn or the app is not in focus)."));
                _loadRetries = RetriesAfterAnswer(present && IsAnswer(result), _loadRetries); // "still loading" is not a try
                if (result == MRUK.LoadDeviceResult.Success) Rebuild();
            }
            catch (Exception e) { Debug.LogWarning($"{LogTag} Room load retry {_loadRetries}/{MaxLoadRetries} threw: {e.Message}"); }
            finally { _loading = false; _loadRetryAt = Time.unscaledTime + LoadRetrySeconds; }
        }

        void Start()
        {
            _mruk = MRUK.Instance;
            if (_mruk == null)
            {
                Debug.LogWarning($"{LogTag} No MRUK in the scene; running with no room.");
                Settle();
                return;
            }
            _mruk.RoomCreatedEvent.AddListener(OnRoomEvent);
            _mruk.RoomUpdatedEvent.AddListener(OnRoomEvent);
            _mruk.RoomRemovedEvent.AddListener(OnRoomRemoved);
            _mruk.RegisterSceneLoadedCallback(Rebuild);
        }

        void OnDestroy()
        {
            if (_mruk == null) return;
            _mruk.SceneLoadedEvent.RemoveListener(Rebuild);
            _mruk.RoomCreatedEvent.RemoveListener(OnRoomEvent);
            _mruk.RoomUpdatedEvent.RemoveListener(OnRoomEvent);
            _mruk.RoomRemovedEvent.RemoveListener(OnRoomRemoved);
        }

        // Only while a loaded room was rejected: once a second, is the head inside it now?
        void Settle()
        {
            if (Settled) return;
            Settled = true;
            Changed?.Invoke();
        }

        void Update()
        {
            if (!Settled && _mruk != null)
            {
                if (Application.isEditor || IsPresent) { _waited += Time.unscaledDeltaTime; _sinceAsk += Time.unscaledDeltaTime; }
                FirstLoad next = WhileUnsettled(Application.isEditor, _waited, _asking, _sinceAsk);
                if (next == FirstLoad.Ask) AskFirst();
                else if (next == FirstLoad.GiveUp)
                {
                    Debug.LogWarning($"{LogTag} MRUK has not loaded a scene after {_waited:F0} s; running with no room for now.");
                    Settle();
                }
            }
            // Only in the room-less state on a headset: is the user there, and is it time to ask the device again?
            if (_mruk != null && !Application.isEditor && Settled && !HasScan && _rejected == null && Visiting == null)
            {
                bool present = IsPresent;
                if (present && !_wasPresent && _loadRetries > 0) Debug.Log($"{LogTag} Room load: the user is back with no room; trying again.");
                _loadRetries = RetriesOnPresence(present, _wasPresent, _loadRetries);
                _wasPresent = present;
                if (ShouldRetryLoad(false, true, false, false, false, _loading || _rescanPending || !present, _loadRetries,
                        Time.unscaledTime, _loadRetryAt)) RetryLoad();
            }
            if (_rejected == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + RecheckSeconds;
            if (HeadInside(_rejected) != false) Rebuild();
        }

        bool _rescanPending;

        /// <summary>
        /// Rescan room: opens Meta's Space Setup. When the app is back in focus, MRUK reloads the scene and the room is
        /// rebuilt (a new room UUID gets its own save; a cancelled setup reloads the same room, which changes nothing).
        /// False where Space Setup cannot run (the Editor, no MRUK).
        /// </summary>
        public bool RequestRescan()
        {
            if (_mruk == null || Application.isEditor) { Debug.Log($"{LogTag} Rescan room: Space Setup runs on the headset."); return false; }
            _rescanPending = true;
            OVRScene.RequestSpaceSetup();
            Debug.Log($"{LogTag} Rescan room: Space Setup requested.");
            return true;
        }

        void OnApplicationFocus(bool focus) { _focused = focus; if (focus) ReloadAfterRescan(); }
        void OnApplicationPause(bool paused) { _paused = paused; if (!paused) ReloadAfterRescan(); }

        void ReloadAfterRescan()
        {
            if (!_rescanPending || _mruk == null) return;
            _rescanPending = false;
            Debug.Log($"{LogTag} Rescan room: back from Space Setup, reloading the scene.");
            _mruk.LoadSceneFromDevice(); // its room events rebuild this (Rebuild) and key the save to the room
        }

        void OnRoomEvent(MRUKRoom room) => Rebuild();

        // MRUK raises this before the room leaves its Rooms list, so skip it explicitly.
        void OnRoomRemoved(MRUKRoom room) => Rebuild(room);

        /// <summary>Re-reads the current room from MRUK and raises <see cref="Changed"/>.</summary>
        public void Rebuild() => Rebuild(null);

        /// <summary>
        /// Re-reads the room, ignoring <paramref name="removed"/> and destroyed rooms. With no room
        /// left everything goes empty and HasScan false; Changed is raised either way.
        /// </summary>
        public void Rebuild(MRUKRoom removed)
        {
            if (Visiting != null) { Debug.Log($"{LogTag} Room reload ignored during a visit to '{Visiting.Snapshot.name}'."); return; }
            Clear();
            MRUKRoom room = PickRoom(removed);
            if (room != null && !Usable(Application.isEditor, room.IsLocal))
            {
                Debug.LogWarning($"{LogTag} Room {room.Anchor.Uuid:N} did not come from the device; it is not used.");
                room = null;
            }
            _rejected = room != null && HeadInside(room) == false ? room : null;
            if (room != null && _rejected == null && FirstOf(room.FloorAnchors) != null)
            {
                Read(room);
                RoomKey = room.Anchor.Uuid.ToString("N");
                _last = room;
            }
            Settled = true;
            if (Debug.isDebugBuild) LogRooms(room);
            Debug.Log($"{LogTag} Room read: scan={HasScan}, walls={_walls.Count}, windows={_windows.Count}, " +
                      $"doors={_doors.Count}, furniture={_furniture.Count}" +
                      (_rejected != null ? "; the user is not inside the loaded room, so no scan is used" : ""));
            Changed?.Invoke();
        }

        /// <summary>
        /// Development builds: one line per room MRUK holds (device check of PR #275: with more than one scanned room on
        /// the headset the app read a different room). Runs on a room event only, never per frame.
        /// </summary>
        void LogRooms(MRUKRoom picked)
        {
            List<MRUKRoom> rooms = _mruk != null ? _mruk.Rooms : null;
            MRUKRoom current = _mruk != null ? _mruk.GetCurrentRoom() : null;
            Vector3 head = _head != null ? _head.position : Vector3.zero;
            Debug.Log($"{LogTag} Rooms from MRUK: {(rooms != null ? rooms.Count : 0)}, head {(_head != null ? head.ToString("F2") : "unknown")}.");
            for (int i = 0; rooms != null && i < rooms.Count; i++)
            {
                MRUKRoom room = rooms[i];
                if (room == null) continue;
                int walls = 0, windows = 0, doors = 0, volumes = 0, unlabelled = 0;
                List<MRUKAnchor> anchors = room.Anchors;
                for (int k = 0; k < anchors.Count; k++)
                {
                    MRUKAnchor anchor = anchors[k];
                    if (anchor == null) continue;
                    if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE)) walls++;
                    else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME)) windows++;
                    else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME)) doors++;
                    else if (anchor.VolumeBounds.HasValue) volumes++;
                    if (anchor.Label == 0) unlabelled++;
                }
                MRUKAnchor floor = FirstOf(room.FloorAnchors);
                Debug.Log($"{LogTag} Room {i} {room.Anchor.Uuid:N} ({(room.IsLocal ? "from the device" : "NOT from the device")}): anchors {anchors.Count} (walls {walls}, windows {windows}, doors {doors}, " +
                          $"volumes {volumes}, no label {unlabelled}), floor at {(floor != null ? floor.transform.position.ToString("F2") : "none")}, " +
                          $"head inside {HeadInside(room)}{(room == current ? ", MRUK's current room" : "")}{(room == picked ? ", PICKED" : "")}.");
            }
        }

        /// <summary>Whether the head is inside <paramref name="room"/>'s floor outline; null when there is no head to test.</summary>
        bool? HeadInside(MRUKRoom room)
        {
            if (_head == null && Camera.main != null) _head = Camera.main.transform; // the rig's centre eye
            if (_head == null) return null;
            return room.IsPositionInRoom(_head.position, false);
        }

        // The room the head is in (current first), else the current or first room (which Rebuild then rejects).
        MRUKRoom PickRoom(MRUKRoom removed)
        {
            if (_mruk == null) return null;
            // A destroyed room compares equal to null in Unity.
            if (KeepLast(_last != null, _last == removed, _last != null ? HeadInside(_last) : null)) return _last;
            MRUKRoom current = _mruk.GetCurrentRoom();
            List<MRUKRoom> rooms = _mruk.Rooms;
            if (current != null && current != removed && HeadInside(current) != false) return current;
            for (int i = 0; rooms != null && i < rooms.Count; i++)
            {
                if (rooms[i] != null && rooms[i] != removed && HeadInside(rooms[i]) == true) return rooms[i];
            }
            if (current != null && current != removed) return current;
            for (int i = 0; rooms != null && i < rooms.Count; i++)
            {
                // A destroyed room compares equal to null in Unity.
                if (rooms[i] != null && rooms[i] != removed) return rooms[i];
            }
            return null;
        }

        /// <summary>The saved space being visited (Saved spaces), or null in the real room.</summary>
        public SnapshotRoomInfo Visiting { get; private set; }

        /// <summary>
        /// Plays a saved space back as this room (posed around the user's real desk or bed): every consumer (shell, zone,
        /// design store, edit mode) sees it through the same IRoomInfo, under its own save key. Raises Changed.
        /// </summary>
        public void Visit(SnapshotRoomInfo room, string key)
        {
            Clear();
            Visiting = room;
            HasScan = true;
            RoomKey = key;
            FloorY = room.FloorY;
            CeilingY = room.CeilingY;
            for (int i = 0; i < room.FloorOutline.Count; i++) _outline.Add(room.FloorOutline[i]);
            for (int i = 0; i < room.Walls.Count; i++) _walls.Add(room.Walls[i]);
            for (int i = 0; i < room.Windows.Count; i++) _windows.Add(room.Windows[i]);
            for (int i = 0; i < room.Doors.Count; i++) _doors.Add(room.Doors[i]);
            for (int i = 0; i < room.Furniture.Count; i++) _furniture.Add(room.Furniture[i]);
            Settled = true;
            Debug.Log($"{LogTag} Visiting '{room.Snapshot.name}' ({key}): floor y {FloorY:F2}, furniture {_furniture.Count}.");
            Changed?.Invoke();
        }

        /// <summary>Back to the real room (re-read from the scan, or no scan).</summary>
        public void Leave()
        {
            if (Visiting == null) return;
            Debug.Log($"{LogTag} Leaving '{Visiting.Snapshot.name}'.");
            Visiting = null;
            Rebuild();
        }

        void Clear()
        {
            HasScan = false;
            RoomKey = RoomSaves.NoScanKey;
            FloorY = 0f;
            CeilingY = 0f;
            _outline.Clear();
            _realOutline.Clear();
            Extended = false;
            _walls.Clear();
            _windows.Clear();
            _doors.Clear();
            _furniture.Clear();
        }

        void Read(MRUKRoom room)
        {
            HasScan = true;
            FloorY = FirstOf(room.FloorAnchors).transform.position.y;
            MRUKAnchor ceiling = FirstOf(room.CeilingAnchors);
            CeilingY = ceiling != null ? ceiling.transform.position.y : FloorY;
            RoomGeometry.FloorOutlineCcw(room.GetRoomOutline(), _outline);
            _realOutline.AddRange(_outline);
            Vector2 center = RoomGeometry.Centroid(_outline);

            List<MRUKAnchor> anchors = room.Anchors;
            for (int i = 0; i < anchors.Count; i++)
            {
                MRUKAnchor anchor = anchors[i];
                if (anchor == null) continue;
                string id = IdOf(anchor, i);

                if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE)) AddPlane(_walls, anchor, id);
                else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME)) AddPlane(_windows, anchor, id);
                else if (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME)) AddPlane(_doors, anchor, id);
                else if (anchor.VolumeBounds.HasValue && TryRole(anchor.Label, out FurnitureRole role))
                {
                    _furniture.Add(ToFurniture(anchor, id, role, center));
                }
            }
        }

        static MRUKAnchor FirstOf(List<MRUKAnchor> anchors) =>
            anchors != null && anchors.Count > 0 ? anchors[0] : null;

        static void AddPlane(List<RoomElement> list, MRUKAnchor anchor, string id)
        {
            if (!anchor.PlaneRect.HasValue) return;
            Rect rect = anchor.PlaneRect.Value;
            Transform t = anchor.transform;
            Vector3 center = t.TransformPoint(rect.center.x, rect.center.y, 0f);
            list.Add(new RoomElement(id, RoomGeometry.BoxFromPlane(center, rect.width, rect.height, t.forward)));
        }

        FurnitureAnchor ToFurniture(MRUKAnchor anchor, string id, FurnitureRole role, Vector2 roomCenter)
        {
            Bounds bounds = anchor.VolumeBounds.Value;
            Transform t = anchor.transform;
            Vector3 size = bounds.size;
            OrientedBox box = RoomGeometry.BoxFromEdges(
                t.TransformPoint(bounds.center),
                t.TransformVector(size.x, 0f, 0f),
                t.TransformVector(0f, size.y, 0f),
                t.TransformVector(0f, 0f, size.z),
                roomCenter);

            // A table or bed scanned with a top plane reports that plane; the plane's normal is up.
            float? topPlaneY = null;
            if (anchor.PlaneRect.HasValue && t.forward.y > 0.9f) topPlaneY = t.position.y;
            return new FurnitureAnchor(id, role, box, RoomGeometry.UsableHeight(role, box, FloorY, topPlaneY));
        }

        static bool TryRole(MRUKAnchor.SceneLabels label, out FurnitureRole role)
        {
            role = FurnitureRole.Other;
            if ((label & MRUKAnchor.SceneLabels.TABLE) != 0) role = FurnitureRole.Table;
            else if ((label & MRUKAnchor.SceneLabels.BED) != 0) role = FurnitureRole.Bed;
            else if ((label & MRUKAnchor.SceneLabels.STORAGE) != 0) role = FurnitureRole.Storage;
            else if ((label & MRUKAnchor.SceneLabels.COUCH) != 0) role = FurnitureRole.Couch;
            else if ((label & (MRUKAnchor.SceneLabels.OTHER | MRUKAnchor.SceneLabels.SCREEN |
                               MRUKAnchor.SceneLabels.LAMP | MRUKAnchor.SceneLabels.PLANT)) == 0) return false;
            return true;
        }

        // The MRUK anchor UUID is stable across launches. Rooms loaded from a prefab or JSON may
        // have no UUID; then the label and the anchor's order in the room stand in for it.
        static string IdOf(MRUKAnchor anchor, int index)
        {
            Guid uuid = anchor.Anchor.Uuid;
            return uuid != Guid.Empty ? uuid.ToString("N") : $"{anchor.Label}-{index}";
        }
    }
}
