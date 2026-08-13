using JobTimeline.Core.Diagnostics;

namespace JobTimeline.Core.Events;

public sealed record JobEvent(
    Guid EventId,
    Guid ExecutionId,
    JobEventType Type,
    DateTimeOffset OccurredAtUtc,
    string Source,
    string? SourceStream = null,
    long? SourceSequence = null,
    int AttemptNumber = 1,
    JobFailure? Failure = null,
    JobCorrelation? Correlation = null,
    Guid? ParentExecutionId = null);
