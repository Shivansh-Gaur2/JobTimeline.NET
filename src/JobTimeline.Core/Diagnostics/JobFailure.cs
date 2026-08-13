namespace JobTimeline.Core.Diagnostics;

public sealed record JobFailure(
    JobFailureClassification Classification,
    string? SafeReason = null);
