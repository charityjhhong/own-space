using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OwnSpace.Contracts;
using UnityEngine.Networking;

namespace OwnSpace.Core.Unity
{
    /// <summary>
    /// Posts the assistant request to the proxy with UnityWebRequest. Call
    /// <see cref="PostJsonAsync"/> on Unity's main thread (the request is created and sent there).
    /// Cancellation completes the task at once and aborts the request back on the main thread, so a
    /// cancel from a worker thread (the runner's timeout) never touches UnityWebRequest off-thread.
    /// </summary>
    public sealed class UnityWebRequestTransport : IAssistantTransport
    {
        readonly string _endpoint;
        readonly string _deviceId;
        readonly int _timeoutSeconds;

        public UnityWebRequestTransport(string endpoint, string deviceId, int timeoutSeconds = 10)
        {
            _endpoint = endpoint;
            _deviceId = deviceId;
            _timeoutSeconds = timeoutSeconds;
        }

        /// <exception cref="InvalidOperationException">Called without Unity's main-thread context: a cancel
        /// could not abort the request safely, so this is refused instead of half-working.</exception>
        public Task<string> PostJsonAsync(string json, CancellationToken cancellationToken)
        {
            var main = SynchronizationContext.Current
                ?? throw new InvalidOperationException("UnityWebRequestTransport: call PostJsonAsync on Unity's main thread");
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new UnityWebRequest(_endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds,
            };
            request.SetRequestHeader("content-type", "application/json");
            request.SetRequestHeader("x-device-id", _deviceId);
            bool done = false; // read and written on the main thread only
            CancellationTokenRegistration registration = default;
            request.SendWebRequest().completed += _ =>
            {
                done = true;
                registration.Dispose();
                if (request.result == UnityWebRequest.Result.Success) result.TrySetResult(request.downloadHandler.text);
                else result.TrySetException(new AssistantTransportException((int)request.responseCode, request.error ?? "request failed"));
                request.Dispose();
            };
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    result.TrySetCanceled(cancellationToken);
                    main.Post(_ => { if (!done) request.Abort(); }, null);
                });
            }
            return result.Task;
        }
    }
}
