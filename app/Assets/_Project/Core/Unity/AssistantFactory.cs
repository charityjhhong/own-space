using System.Collections.Generic;
using System.IO;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.Core.Unity
{
    /// <summary>
    /// Builds the app's design assistant from <c>Resources/OwnSpaceAssistant.json</c> (the proxy URL,
    /// no secret). With no usable URL it returns <see cref="OfflineDesignAssistant"/>, so the runner
    /// uses the offline presets and tells the user. Call on the main thread, e.g. at startup.
    /// </summary>
    public static class AssistantFactory
    {
        public const string ConfigResource = "OwnSpaceAssistant";
        public const string InstallIdFile = "install-id.txt";

        public static IDesignAssistant Create(IReadOnlyCollection<string> finishIds, IReadOnlyCollection<string> moodIds)
        {
            var text = Resources.Load<TextAsset>(ConfigResource);
            return Create(AssistantConfig.Parse(text != null ? text.text : null),
                Path.Combine(Application.persistentDataPath, InstallIdFile), finishIds, moodIds);
        }

        public static IDesignAssistant Create(AssistantConfig config, string installIdPath,
            IReadOnlyCollection<string> finishIds, IReadOnlyCollection<string> moodIds)
        {
            if (config == null || !config.IsLive) return new OfflineDesignAssistant();
            var transport = new UnityWebRequestTransport(config.Endpoint, InstallId.LoadOrCreate(installIdPath));
            return new ProxyDesignAssistant(transport, finishIds, moodIds);
        }
    }
}
