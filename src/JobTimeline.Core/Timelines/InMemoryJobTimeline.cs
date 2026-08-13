using JobTimeline.Core.Diagnostics;
using JobTimeline.Core.Events;
using JobTimeline.Core.Projections;

namespace JobTimeline.Core.Timelines;

public sealed class InMemoryJobTimeline : IJobTimeline, IJobTimelineStore
{
    private readonly Dictionary<Guid, ExecutionTimeline> _timelines = [];
    private readonly object _syncRoot = new();
    private readonly IJobEventSanitizer _sanitizer;
    private readonly TimeProvider _timeProvider;

    public InMemoryJobTimeline(TimeProvider? timeProvider = null, IJobEventSanitizer? sanitizer = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sanitizer = sanitizer ?? new DefaultJobEventSanitizer();
    }

    public AppendJobEventResult Append(JobEvent jobEvent)
    {
        jobEvent = _sanitizer.Sanitize(jobEvent);
        JobEventValidator.Validate(jobEvent);

        lock (_syncRoot)
        {
            var timeline = GetOrCreateTimeline(jobEvent.ExecutionId);
            var existingEvent = timeline.Events.SingleOrDefault(acceptedEvent =>
                acceptedEvent.Event.EventId == jobEvent.EventId &&
                string.Equals(acceptedEvent.Event.Source, jobEvent.Source, StringComparison.Ordinal));

            if (existingEvent is not null)
            {
                if (existingEvent.Event != jobEvent)
                {
                    throw new InvalidOperationException("An existing event identity cannot be reused with different event data.");
                }

                return new AppendJobEventResult(existingEvent, timeline.State, true);
            }

            EnsureSourceSequenceIsUnique(timeline.Events, jobEvent);

            var acceptedEvent = new AcceptedJobEvent(
                jobEvent,
                timeline.Events.Count + 1L,
                _timeProvider.GetUtcNow());

            timeline.Events.Add(acceptedEvent);
            timeline.State = JobExecutionProjector.Project(timeline.Events);

            return new AppendJobEventResult(acceptedEvent, timeline.State, false);
        }
    }

    public IReadOnlyList<AcceptedJobEvent> GetEvents(Guid executionId)
    {
        lock (_syncRoot)
        {
            return _timelines.TryGetValue(executionId, out var timeline)
                ? timeline.Events.ToArray()
                : [];
        }
    }

    public JobExecutionState? GetState(Guid executionId)
    {
        lock (_syncRoot)
        {
            return _timelines.TryGetValue(executionId, out var timeline)
                ? timeline.State
                : null;
        }
    }

    public ValueTask<AppendJobEventResult> AppendAsync(JobEvent jobEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Append(jobEvent));
    }

    public ValueTask<IReadOnlyList<AcceptedJobEvent>> GetEventsAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetEvents(executionId));
    }

    public ValueTask<JobExecutionState?> GetStateAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetState(executionId));
    }

    private ExecutionTimeline GetOrCreateTimeline(Guid executionId)
    {
        if (!_timelines.TryGetValue(executionId, out var timeline))
        {
            timeline = new ExecutionTimeline();
            _timelines.Add(executionId, timeline);
        }

        return timeline;
    }

    private static void EnsureSourceSequenceIsUnique(IEnumerable<AcceptedJobEvent> events, JobEvent jobEvent)
    {
        if (jobEvent.SourceStream is null || jobEvent.SourceSequence is null)
        {
            return;
        }

        var hasCollision = events.Any(acceptedEvent =>
            string.Equals(acceptedEvent.Event.Source, jobEvent.Source, StringComparison.Ordinal) &&
            string.Equals(acceptedEvent.Event.SourceStream, jobEvent.SourceStream, StringComparison.Ordinal) &&
            acceptedEvent.Event.SourceSequence == jobEvent.SourceSequence);

        if (hasCollision)
        {
            throw new InvalidOperationException("A source sequence cannot identify two different events in the same source stream.");
        }
    }

    private sealed class ExecutionTimeline
    {
        public List<AcceptedJobEvent> Events { get; } = [];

        public JobExecutionState State { get; set; } = null!;
    }
}
