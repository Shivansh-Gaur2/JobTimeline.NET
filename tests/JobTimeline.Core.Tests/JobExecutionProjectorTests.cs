using JobTimeline.Core.Diagnostics;
using JobTimeline.Core.Events;
using JobTimeline.Core.Projections;

namespace JobTimeline.Core.Tests;

public sealed class JobExecutionProjectorTests
{
    [Fact]
    public void Project_RetainsLatestAttemptFailureCorrelationAndHeartbeat()
    {
        var executionId = Guid.NewGuid();
        var heartbeatAt = DateTimeOffset.Parse("2026-08-13T10:15:00Z");
        var failure = new JobFailure(JobFailureClassification.Timeout, "The operation exceeded its deadline.");
        var correlation = new JobCorrelation("trace-123", "span-456", "logs://job-42");
        var events = new[]
        {
            Accepted(executionId, JobEventType.Started, 1, 1),
            Accepted(executionId, JobEventType.Heartbeat, 2, 1, heartbeatAt),
            Accepted(executionId, JobEventType.TimedOut, 3, 2, failure: failure, correlation: correlation)
        };

        var state = JobExecutionProjector.Project(events);

        Assert.Equal(JobEventType.TimedOut, state.CurrentEventType);
        Assert.Equal(2, state.AttemptNumber);
        Assert.Equal(heartbeatAt, state.LastProgressAtUtc);
        Assert.Equal(failure, state.Failure);
        Assert.Equal(correlation, state.Correlation);
    }

    [Fact]
    public void Project_AllowsRetryAfterFailureAndKeepsTheRetryAttemptKnown()
    {
        var executionId = Guid.NewGuid();
        var events = new[]
        {
            Accepted(executionId, JobEventType.Started, 1, 1),
            Accepted(executionId, JobEventType.Failed, 2, 1),
            Accepted(executionId, JobEventType.Retrying, 3, 1),
            Accepted(executionId, JobEventType.Started, 4, 2),
            Accepted(executionId, JobEventType.Succeeded, 5, 2)
        };

        var state = JobExecutionProjector.Project(events);

        Assert.Equal(JobExecutionStateStatus.Known, state.Status);
        Assert.Equal(JobEventType.Succeeded, state.CurrentEventType);
        Assert.Equal(2, state.AttemptNumber);
    }

    [Fact]
    public void Project_RecordsAReceivedLifecycleRegressionAsAnOrderingConflict()
    {
        var executionId = Guid.NewGuid();
        var events = new[]
        {
            Accepted(executionId, JobEventType.Started, 1, 1),
            Accepted(executionId, JobEventType.Enqueued, 2, 1)
        };

        var state = JobExecutionProjector.Project(events);

        Assert.Equal(JobExecutionStateStatus.OrderingConflict, state.Status);
        Assert.Null(state.CurrentEventType);
    }

    [Fact]
    public void Project_UsesReceiptTimeForLastUpdatedTime()
    {
        var executionId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.Parse("2026-08-13T10:15:00Z");
        var recordedAt = DateTimeOffset.Parse("2026-08-13T10:20:00Z");
        var events = new[]
        {
            Accepted(executionId, JobEventType.Started, 1, 1, occurredAt, recordedAtUtc: recordedAt)
        };

        var state = JobExecutionProjector.Project(events);

        Assert.Equal(recordedAt, state.LastUpdatedAtUtc);
    }

    private static AcceptedJobEvent Accepted(
        Guid executionId,
        JobEventType type,
        long sequence,
        int attempt,
        DateTimeOffset? occurredAtUtc = null,
        JobFailure? failure = null,
        JobCorrelation? correlation = null,
        DateTimeOffset? recordedAtUtc = null)
    {
        var occurredAt = occurredAtUtc ?? DateTimeOffset.UtcNow;
        var jobEvent = new JobEvent(
            Guid.NewGuid(),
            executionId,
            type,
            occurredAt,
            "tests",
            AttemptNumber: attempt,
            Failure: failure,
            Correlation: correlation);

        return new AcceptedJobEvent(jobEvent, sequence, recordedAtUtc ?? occurredAt);
    }
}
