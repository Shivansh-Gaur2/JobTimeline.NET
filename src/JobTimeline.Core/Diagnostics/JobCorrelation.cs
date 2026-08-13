namespace JobTimeline.Core.Diagnostics;

public sealed record JobCorrelation(
    string? TraceId = null,
    string? SpanId = null,
    string? LogReference = null);
