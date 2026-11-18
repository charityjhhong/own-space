using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using OwnSpace.Core.Unity;
using UnityEngine.TestTools;

namespace OwnSpace.Tests
{
    public class AssistantClientTests
    {
        static readonly string[] Finishes = { "floor-oak-planks", "wall-cream" };
        static readonly string[] Moods = { "morning", "afternoon", "evening", "night" };

        static AssistantRequest Request() => new AssistantRequest(AssistantTask.FillOutside, "cozy",
            FakeRoomInfo.DormRoom(), new DesignState { zoneOutline = FakeRoomInfo.HalfZone("a") }, FakeCatalog.Dorm().Items);

        static string TempPath() => Path.Combine(Path.GetTempPath(), "ownspace-" + Guid.NewGuid().ToString("N") + ".txt");

        [Test]
        public void ProxyAssistant_PostsTheRequestAndParsesThePlan()
        {
            var transport = new FakeAssistantTransport();
            var plan = new ProxyDesignAssistant(transport, Finishes, Moods).RequestAsync(Request(), CancellationToken.None).Result;
            Assert.AreEqual("night", plan.moodPresetId);
            Assert.AreEqual("Dim and calm.", plan.rationale);
            Assert.AreEqual("FillOutside", (string)JObject.Parse(transport.LastBody)["task"]);
        }

        [TestCase("not json")]
        [TestCase("{\"error\":\"rate_limited\"}")]
        [TestCase("{\"plan\":\"Sorry, I cannot help.\"}")]
        public void ProxyAssistant_UnreadableAnswer_Throws(string answer)
        {
            var assistant = new ProxyDesignAssistant(new FakeAssistantTransport { Answer = answer }, Finishes, Moods);
            var e = Assert.Throws<AggregateException>(() => assistant.RequestAsync(Request(), CancellationToken.None).Wait());
            Assert.IsInstanceOf<FormatException>(e.InnerException);
        }

        [Test]
        public void ProxyAssistant_TransportErrors_Propagate()
        {
            var transport = new FakeAssistantTransport { Throw = new AssistantTransportException(429, "rate_limited") };
            var e = Assert.Throws<AssistantTransportException>(() =>
                new ProxyDesignAssistant(transport, Finishes, Moods).RequestAsync(Request(), CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(429, e.StatusCode);
        }

        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("{\"workerUrl\":\"\"}", null)]
        [TestCase("{\"workerUrl\":\"http://insecure.example\"}", null)]
        [TestCase("{\"workerUrl\":\"https://x.workers.dev/?key=abc\"}", null)]
        [TestCase("{\"workerUrl\":42}", null)]
        [TestCase("{\"workerUrl\":\"https://user:pass@x.workers.dev\"}", null)]
        [TestCase("{\"workerUrl\":\"https://x.workers.dev/#frag\"}", null)]
        [TestCase("{\"workerUrl\":\" https://ownspace.x.workers.dev/ \"}", "https://ownspace.x.workers.dev/v1/assist")]
        public void Config_OnlyAcceptsAnHttpsUrl(string json, string endpoint)
        {
            var config = AssistantConfig.Parse(json);
            Assert.AreEqual(endpoint, config.Endpoint);
            Assert.AreEqual(endpoint != null, config.IsLive);
        }

        [Test]
        public void ShippedConfig_PointsAtTheWorker_AndHasNoSecret()
        {
            string json = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "_Project/Core/Unity/Resources/OwnSpaceAssistant.json"));
            Assert.AreEqual("https://ownspace-assistant.charityjhhong.workers.dev/v1/assist", AssistantConfig.Parse(json).Endpoint);
            StringAssert.DoesNotContain("AIza", json, "a Google API key must never be in the app");
        }

        [Test]
        public void InstallId_IsRandomStableAndRepaired()
        {
            string path = TempPath();
            try
            {
                string id = InstallId.LoadOrCreate(path);
                StringAssert.IsMatch("^[a-f0-9]{32}$", id);
                Assert.AreEqual(id, InstallId.LoadOrCreate(path));
                File.WriteAllText(path, "not an id");
                Assert.AreNotEqual(id, InstallId.LoadOrCreate(path));
                Assert.AreNotEqual(InstallId.LoadOrCreate(TempPath()), id, "another install gets another id");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void NoUrl_MeansOfflinePresets_WithAnHonestNotice()
        {
            var assistant = AssistantFactory.Create(AssistantConfig.Parse(""), TempPath(), Finishes, Moods);
            Assert.IsInstanceOf<OfflineDesignAssistant>(assistant);
            var session = new DesignSession(FakeRoomInfo.DormRoom(), CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog);
            session.SetZone(FakeRoomInfo.HalfZone("a"));
            var outcome = new AssistantRunner(assistant, session, session.Catalog, Finishes, Moods)
                .AskAsync(AssistantTask.FillOutside, "cozy", null, CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsTrue(outcome.Plan.fromFallback);
            StringAssert.Contains("offline", outcome.Plan.notice);
        }

        [Test]
        public void Url_MeansTheLiveProxyClient() =>
            Assert.IsInstanceOf<ProxyDesignAssistant>(AssistantFactory.Create(
                AssistantConfig.Parse("{\"workerUrl\":\"https://ownspace.x.workers.dev\"}"), TempPath(), Finishes, Moods));

        [Test]
        public void Transport_OffTheMainThread_IsRefused()
        {
            var transport = new UnityWebRequestTransport("https://127.0.0.1:9/v1/assist", "test-device-0000000001");
            var e = Assert.Throws<AggregateException>(() => Task.Run(() => transport.PostJsonAsync("{}", CancellationToken.None)).Wait());
            Assert.IsInstanceOf<InvalidOperationException>(e.InnerException);
        }

        // Real UnityWebRequest against a closed local port: fails as a transport error (status 0), never hangs.
        // Opens a socket, so it is explicit (not in the default run) and tagged Network.
        [UnityTest, Explicit, Category("Network")]
        public IEnumerator Transport_UnreachableProxy_FailsWithStatusZero()
        {
            var task = new UnityWebRequestTransport("https://127.0.0.1:9/v1/assist", "test-device-0000000001", 5)
                .PostJsonAsync("{}", CancellationToken.None);
            for (int frames = 0; !task.IsCompleted && frames < 2000; frames++) yield return null;
            Assert.IsTrue(task.IsFaulted, task.Status.ToString());
            Assert.IsInstanceOf<AssistantTransportException>(task.Exception.InnerException);
            Assert.AreEqual(0, ((AssistantTransportException)task.Exception.InnerException).StatusCode);
        }

        // Starts a real request (a socket to a closed local port) but needs no answer from it.
        [UnityTest, Category("Network")]
        public IEnumerator Transport_CancelFromAnotherThread_CompletesAtOnce()
        {
            var cts = new CancellationTokenSource();
            var task = new UnityWebRequestTransport("https://127.0.0.1:9/v1/assist", "test-device-0000000001", 30)
                .PostJsonAsync("{}", cts.Token);
            Task.Run(() => cts.Cancel()).Wait();
            Assert.IsTrue(task.IsCanceled);
            yield return null; // let the posted Abort run on the main thread
            cts.Dispose();
        }
    }
}
