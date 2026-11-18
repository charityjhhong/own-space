using System;
using System.Threading;
using System.Threading.Tasks;

namespace OwnSpace.Contracts.Fakes
{
    /// <summary>
    /// Answers <see cref="IAssistantTransport"/> calls without a network: a fixed proxy answer, or a
    /// thrown error (e.g. <see cref="AssistantTransportException"/> with 429). Records the last body.
    /// </summary>
    public sealed class FakeAssistantTransport : IAssistantTransport
    {
        /// <summary>The proxy's answer body. Default: a valid plan with a mood and a rationale.</summary>
        public string Answer = "{\"plan\":\"{\\\"moodPresetId\\\":\\\"night\\\",\\\"rationale\\\":\\\"Dim and calm.\\\"}\"}";

        /// <summary>When set, every call fails with it.</summary>
        public Exception Throw;

        public string LastBody { get; private set; }
        public int Calls { get; private set; }

        public Task<string> PostJsonAsync(string json, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = json;
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<string>(cancellationToken);
            return Throw != null ? Task.FromException<string>(Throw) : Task.FromResult(Answer);
        }
    }
}
