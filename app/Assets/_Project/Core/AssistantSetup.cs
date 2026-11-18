using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// The app's assistant settings: only the proxy's base URL, never a key. Missing, empty or
    /// non-https URLs, and URLs with user info, a query or a fragment (places a secret could hide),
    /// mean "no live assistant", and the app uses its offline presets.
    /// </summary>
    public sealed class AssistantConfig
    {
        AssistantConfig(string endpoint) { Endpoint = endpoint; }

        /// <summary>The full request URL (base + /v1/assist), or null when there is no live assistant.</summary>
        public string Endpoint { get; }
        public bool IsLive => Endpoint != null;

        /// <summary>Reads <c>{ "workerUrl": "https://..." }</c>. Anything unusable gives an offline config.</summary>
        public static AssistantConfig Parse(string json)
        {
            string url = null;
            try
            {
                var token = JObject.Parse(json ?? "")["workerUrl"];
                url = token != null && token.Type == JTokenType.String ? ((string)token).Trim() : null;
            }
            catch (JsonException) { }
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
                return new AssistantConfig(null);
            return new AssistantConfig(url.TrimEnd('/') + "/v1/assist");
        }
    }

    /// <summary>
    /// A random id for this install, sent as <c>x-device-id</c> so the proxy can rate-limit per device.
    /// It is not derived from the device, the user or the account, and it stays in its own file.
    /// </summary>
    public static class InstallId
    {
        static readonly Regex Valid = new Regex("^[a-f0-9]{32}$");

        public static string LoadOrCreate(string path)
        {
            string id = AtomicFile.TryReadAllText(path)?.Trim();
            if (id != null && Valid.IsMatch(id)) return id;
            id = Guid.NewGuid().ToString("N");
            try { AtomicFile.WriteAllText(path, id); }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException) { } // still usable this session
            return id;
        }
    }

    /// <summary>Used when no proxy URL is configured: always fails, so the runner uses the offline preset and says so.</summary>
    public sealed class OfflineDesignAssistant : IDesignAssistant
    {
        public Task<AssistantPlan> RequestAsync(AssistantRequest request, CancellationToken cancellationToken) =>
            Task.FromException<AssistantPlan>(new InvalidOperationException("assistant: no proxy URL configured"));
    }
}
