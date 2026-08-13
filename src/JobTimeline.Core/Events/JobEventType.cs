namespace JobTimeline.Core.Events;

public enum JobEventType
{
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
    TimedOut,
    Submitted
}
