using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// The live assistant: builds the proxy request, posts it through an <see cref="IAssistantTransport"/>
    /// and parses the answer strictly. Throws on transport errors or an unreadable answer; the caller
    /// adds the timeout and the offline fallback. Nothing returned here is applied unchecked.
    /// </summary>
    public sealed class ProxyDesignAssistant : IDesignAssistant
    {
        readonly IAssistantTransport _transport;
        readonly IReadOnlyCollection<string> _finishIds;
        readonly IReadOnlyCollection<string> _moodIds;

        public ProxyDesignAssistant(IAssistantTransport transport, IReadOnlyCollection<string> finishIds, IReadOnlyCollection<string> moodIds)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _finishIds = finishIds ?? Array.Empty<string>();
            _moodIds = moodIds ?? Array.Empty<string>();
        }

        /// <summary>
        /// Entries the parser dropped from the last answer, for a local log. Written on whichever thread
        /// finishes the call; read it after awaiting the request (the await orders the write before the read).
        /// </summary>
        public IReadOnlyList<string> LastParseErrors { get; private set; } = Array.Empty<string>();

        public async Task<AssistantPlan> RequestAsync(AssistantRequest request, CancellationToken cancellationToken)
        {
            string body = AssistantRequestJson.Build(request, _finishIds, _moodIds);
            string response = await _transport.PostJsonAsync(body, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            string planText;
            try
            {
                var token = JObject.Parse(response ?? "")["plan"];
                planText = token != null && token.Type == JTokenType.String ? (string)token : null;
            }
            catch (JsonException)
            {
                planText = null;
            }
            if (planText == null) throw new FormatException("assistant: the proxy answer has no plan");

            var errors = new List<string>();
            if (!PlanParser.TryParse(planText, out var plan, errors)) throw new FormatException("assistant: " + string.Join("; ", errors));
            LastParseErrors = errors;
            return plan;
        }
    }
}
