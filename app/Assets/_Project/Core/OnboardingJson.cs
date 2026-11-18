using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>Save/restore for <see cref="OnboardingState"/>. A missing, broken or newer file gives a fresh first run.</summary>
    public static class OnboardingJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Converters = { new StringEnumConverter { AllowIntegerValues = false } },
        };

        public static string ToJson(OnboardingState state) => JsonConvert.SerializeObject(state, Settings);

        public static bool TryFromJson(string json, out OnboardingState state)
        {
            state = new OnboardingState();
            if (string.IsNullOrWhiteSpace(json)) return false;
            OnboardingState read;
            try
            {
                read = JsonConvert.DeserializeObject<OnboardingState>(json, Settings);
            }
            catch (JsonException)
            {
                return false;
            }
            if (read == null || read.version > OnboardingState.CurrentVersion) return false;
            if (!System.Enum.IsDefined(typeof(OnboardingStep), read.step)) read.step = OnboardingStep.Scan;
            if (!System.Enum.IsDefined(typeof(OnboardingBranch), read.branch)) read.branch = OnboardingBranch.None;
            if (read.version < 2) Upgrade(read);
            if (read.skipped) read.step = OnboardingStep.Done;
            if (float.IsNaN(read.activeSeconds) || read.activeSeconds < 0f) read.activeSeconds = 0f;
            state = read;
            return true;
        }

        // Version 1 ran zone → redesign → fill outside → mood. Its steps map onto the guided flow; Done stays Done.
        static void Upgrade(OnboardingState s)
        {
            switch (s.step)
            {
                case OnboardingStep.SetZone: s.step = OnboardingStep.Scan; break; // passes at once with a room
                case OnboardingStep.FillOutside:
                case OnboardingStep.PickMood: s.step = OnboardingStep.ChooseBranch; break; // RedesignOne stays
            }
            s.branch = OnboardingBranch.None;
            s.version = OnboardingState.CurrentVersion;
        }

        public static void SaveToFile(string path, OnboardingState state) => AtomicFile.WriteAllText(path, ToJson(state));

        public static bool TryLoadFromFile(string path, out OnboardingState state)
        {
            string text = AtomicFile.TryReadAllText(path);
            if (TryFromJson(text, out state)) return true;
            if (text != null) AtomicFile.KeepBrokenCopy(path);
            return false;
        }
    }
}
