using JobTimeline.Core.Diagnostics;
using JobTimeline.Core.Events;

namespace JobTimeline.Core.Projections;

public sealed record JobExecutionState(
    Guid ExecutionId,
    JobEventType? CurrentEventType,
    DateTimeOffset LastUpdatedAtUtc,
    long ExecutionSequence,
    JobExecutionStateStatus Status,
    int AttemptNumber,
    DateTimeOffset? LastProgressAtUtc,
    JobFailure? Failure,
    JobCorrelation? Correlation);
