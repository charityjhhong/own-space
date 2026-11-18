using System;
using System.Threading;
using System.Threading.Tasks;

namespace OwnSpace.Contracts
{
    /// <summary>
    /// Posts one JSON body to the assistant proxy and returns the response body. The xr lane
    /// implements it with UnityWebRequest: the Worker URL comes from a config asset (no secret),
    /// and an <c>x-device-id</c> header carries a random install id (never a user or device name).
    /// Throws <see cref="AssistantTransportException"/> for non-2xx answers and honors cancellation.
    /// </summary>
    public interface IAssistantTransport
    {
        Task<string> PostJsonAsync(string json, CancellationToken cancellationToken);
    }

    public sealed class AssistantTransportException : Exception
    {
        public AssistantTransportException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>HTTP status; 0 when the request never got an answer (offline, DNS, TLS).</summary>
        public int StatusCode { get; }
    }
}
