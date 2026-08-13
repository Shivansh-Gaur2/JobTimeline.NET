namespace JobTimeline.Core.Events;

public static class JobEventTypeExtensions
{
    public static bool IsTerminal(this JobEventType eventType)
    {
        return eventType is JobEventType.Succeeded or JobEventType.Failed or JobEventType.Cancelled or JobEventType.TimedOut;
    }
}
