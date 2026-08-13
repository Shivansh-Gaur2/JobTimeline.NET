using JobTimeline.Core.Events;

namespace JobTimeline.Core.Projections;

public static class JobExecutionProjector
{
    public static JobExecutionState Project(IReadOnlyList<AcceptedJobEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            throw new ArgumentException("An execution must contain at least one accepted event.", nameof(events));
        }

        var firstEvent = events[0];
        var executionId = firstEvent.Event.ExecutionId;
        JobEventType? currentEventType = firstEvent.Event.Type;
        var status = JobExecutionStateStatus.Known;
        var lastProgressAtUtc = GetProgressTime(firstEvent.Event);
        var failure = firstEvent.Event.Failure;
        var correlation = firstEvent.Event.Correlation;

        ValidateSequence(firstEvent, 1, executionId);

        for (var index = 1; index < events.Count; index++)
        {
            var nextEvent = events[index];
            ValidateSequence(nextEvent, index + 1L, executionId);

            if (status == JobExecutionStateStatus.Known &&
                currentEventType is { } knownEventType &&
                !JobLifecycle.IsTransitionAllowed(events[index - 1].Event, nextEvent.Event))
            {
                status = JobExecutionStateStatus.OrderingConflict;
                currentEventType = null;
            }
            else if (status == JobExecutionStateStatus.Known)
            {
                currentEventType = nextEvent.Event.Type;
            }

            if (GetProgressTime(nextEvent.Event) is { } progressTime &&
                (lastProgressAtUtc is null || progressTime > lastProgressAtUtc))
            {
                lastProgressAtUtc = progressTime;
            }

            if (nextEvent.Event.Failure is not null)
            {
                failure = nextEvent.Event.Failure;
            }

            if (nextEvent.Event.Correlation is not null)
            {
                correlation = nextEvent.Event.Correlation;
            }
        }

        var latestEvent = events[^1];

        return new JobExecutionState(
            executionId,
            currentEventType,
            latestEvent.RecordedAtUtc,
            latestEvent.ExecutionSequence,
            status,
            latestEvent.Event.AttemptNumber,
            lastProgressAtUtc,
            failure,
            correlation);
    }

    private static void ValidateSequence(AcceptedJobEvent acceptedEvent, long expectedSequence, Guid expectedExecutionId)
    {
        if (acceptedEvent.Event.ExecutionId != expectedExecutionId)
        {
            throw new ArgumentException("All events in one projection must belong to the same execution.", nameof(acceptedEvent));
        }

        if (acceptedEvent.ExecutionSequence != expectedSequence)
        {
            throw new ArgumentException("Accepted events must have a contiguous execution sequence.", nameof(acceptedEvent));
        }
    }

    private static DateTimeOffset? GetProgressTime(JobEvent jobEvent)
    {
        return jobEvent.Type is JobEventType.Progressed or JobEventType.Heartbeat
            ? jobEvent.OccurredAtUtc
            : null;
    }
}
