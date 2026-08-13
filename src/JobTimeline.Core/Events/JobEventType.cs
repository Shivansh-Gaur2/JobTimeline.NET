namespace JobTimeline.Core.Events;

public enum JobEventType
{
    Submitted,
    Enqueued,
    Received,
    Started,
    Progressed,
    Heartbeat,
    Retrying,
    Handoff,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut
}
