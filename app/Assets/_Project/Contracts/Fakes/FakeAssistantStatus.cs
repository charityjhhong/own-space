using System;

namespace OwnSpace.Contracts.Fakes
{
    /// <summary>A status the UI can be built against: call <see cref="Set"/> to drive it.</summary>
    public sealed class FakeAssistantStatus : IAssistantStatus
    {
        public AssistantState State { get; private set; }
        public string Message { get; private set; } = "";
        public event Action Changed;

        public void Set(AssistantState state, string message = "")
        {
            State = state;
            Message = message ?? "";
            Changed?.Invoke();
        }
    }
}
