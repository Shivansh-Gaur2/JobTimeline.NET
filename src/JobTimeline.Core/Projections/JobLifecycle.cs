using JobTimeline.Core.Events;

namespace JobTimeline.Core.Projections;

internal static class JobLifecycle
{
    public static bool IsTransitionAllowed(JobEvent previous, JobEvent next)
    {
        if (next.AttemptNumber < previous.AttemptNumber)
        {
            return false;
        }

        if (previous.Type is JobEventType.Succeeded or JobEventType.Cancelled)
        {
            return false;
        }

        if (previous.Type is JobEventType.Failed or JobEventType.TimedOut)
        {
            return next.Type == JobEventType.Retrying || next.AttemptNumber > previous.AttemptNumber;
        }

        if (next.AttemptNumber > previous.AttemptNumber)
        {
            return true;
        }

        return previous.Type switch
        {
            JobEventType.Submitted => next.Type is JobEventType.Enqueued or JobEventType.Received or JobEventType.Started or JobEventType.Handoff or JobEventType.Failed or JobEventType.Cancelled or JobEventType.TimedOut,
            JobEventType.Enqueued => next.Type is JobEventType.Received or JobEventType.Started or JobEventType.Handoff or JobEventType.Failed or JobEventType.Cancelled or JobEventType.TimedOut,
            JobEventType.Received => next.Type is JobEventType.Started or JobEventType.Handoff or JobEventType.Failed or JobEventType.Cancelled or JobEventType.TimedOut,
            JobEventType.Started => next.Type is JobEventType.Progressed or JobEventType.Heartbeat or JobEventType.Handoff or JobEventType.Succeeded or JobEventType.Failed or JobEventType.Cancelled or JobEventType.TimedOut,
            JobEventType.Progressed or JobEventType.Heartbeat => next.Type is JobEventType.Progressed or JobEventType.Heartbeat or JobEventType.Handoff or JobEventType.Succeeded or JobEventType.Failed or JobEventType.Cancelled or JobEventType.TimedOut,
            JobEventType.Handoff => next.Type is JobEventType.Enqueued or JobEventType.Received or JobEventType.Started or JobEventType.Failed or JobEventType.Cancelled or JobEventType.TimedOut,
            JobEventType.Retrying => next.Type is JobEventType.Enqueued or JobEventType.Received or JobEventType.Started or JobEventType.Failed or JobEventType.Cancelled or JobEventType.TimedOut,
            _ => false
        };
    }
}
