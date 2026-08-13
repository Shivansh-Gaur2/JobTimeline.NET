using JobTimeline.Core.Events;
using JobTimeline.Core.Projections;
using JobTimeline.Core.Timelines;

namespace JobTimeline.Core.Tests;

public sealed class InMemoryJobTimelineTests
{
    [Fact]
    public void Append_AssignsIncreasingSequenceAndProjectsLatestKnownState()
    {
        var timeline = new InMemoryJobTimeline();
        var executionId = Guid.NewGuid();

        var enqueued = timeline.Append(CreateEvent(executionId, JobEventType.Enqueued));
        var started = timeline.Append(CreateEvent(executionId, JobEventType.Started));
        var succeeded = timeline.Append(CreateEvent(executionId, JobEventType.Succeeded));

        Assert.Equal(1, enqueued.Event.ExecutionSequence);
        Assert.Equal(2, started.Event.ExecutionSequence);
        Assert.Equal(3, succeeded.Event.ExecutionSequence);
        Assert.Equal(JobEventType.Succeeded, succeeded.State.CurrentEventType);
        Assert.Equal(JobExecutionStateStatus.Known, succeeded.State.Status);
    }

    [Fact]
    public void Append_IdenticalEventTwice_ReturnsExistingEventWithoutAppendingAgain()
    {
        var timeline = new InMemoryJobTimeline();
        var jobEvent = CreateEvent(Guid.NewGuid(), JobEventType.Enqueued);

        var firstResult = timeline.Append(jobEvent);
        var retryResult = timeline.Append(jobEvent);

        Assert.False(firstResult.WasDuplicate);
        Assert.True(retryResult.WasDuplicate);
        Assert.Equal(firstResult.Event, retryResult.Event);
        Assert.Single(timeline.GetEvents(jobEvent.ExecutionId));
    }

    [Fact]
    public void Append_ConflictingEventAfterTerminalEvent_ProducesOrderingConflict()
    {
        var timeline = new InMemoryJobTimeline();
        var executionId = Guid.NewGuid();

        timeline.Append(CreateEvent(executionId, JobEventType.Started));
        timeline.Append(CreateEvent(executionId, JobEventType.Succeeded));
        var result = timeline.Append(CreateEvent(executionId, JobEventType.Failed));

        Assert.Equal(JobExecutionStateStatus.OrderingConflict, result.State.Status);
        Assert.Null(result.State.CurrentEventType);
        Assert.Equal(3, timeline.GetEvents(executionId).Count);
    }

    [Fact]
    public void Append_ReusedSourcePositionForDifferentEvent_IsRejected()
    {
        var timeline = new InMemoryJobTimeline();
        var executionId = Guid.NewGuid();

        timeline.Append(CreateEvent(executionId, JobEventType.Enqueued, sourceSequence: 10));

        Assert.Throws<InvalidOperationException>(() =>
            timeline.Append(CreateEvent(executionId, JobEventType.Started, sourceSequence: 10)));
    }

    private static JobEvent CreateEvent(Guid executionId, JobEventType type, long? sourceSequence = null)
    {
        return new JobEvent(
            Guid.NewGuid(),
            executionId,
            type,
            DateTimeOffset.UtcNow,
            "tests",
            sourceSequence is null ? null : "test-stream",
            sourceSequence);
    }
}
