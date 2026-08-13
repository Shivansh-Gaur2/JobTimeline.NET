namespace JobTimeline.Core.Events;

public sealed record AcceptedJobEvent(
    JobEvent Event,
    long ExecutionSequence,
    DateTimeOffset RecordedAtUtc);
