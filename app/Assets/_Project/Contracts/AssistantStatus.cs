using System;

namespace OwnSpace.Contracts
{
    public enum AssistantState
    {
        /// <summary>Nothing running; <see cref="IAssistantStatus.Message"/> holds the last answer's rationale, if any.</summary>
        Idle = 0,
        Asking = 1,
        /// <summary>A plan was applied; the message says how much.</summary>
        Applied = 2,
        /// <summary>The live assistant failed or timed out, so a preset is shown; the message says so.</summary>
        Fallback = 3,
        Failed = 4,
    }

    /// <summary>
    /// What the AI panel shows. <see cref="Changed"/> is raised on the thread that started the request
    /// (Unity's main thread in the app), so UI can bind to it directly. Messages are short (≤ 60 chars
    /// for core's own texts; a model rationale can be longer).
    /// </summary>
    public interface IAssistantStatus
    {
        AssistantState State { get; }
        string Message { get; }
        event Action Changed;
    }
}
