namespace JobTimeline.Core.Diagnostics;

public enum JobFailureClassification
{
    Unknown,
    Transient,
    Permanent,
    Cancellation,
    Timeout
}
