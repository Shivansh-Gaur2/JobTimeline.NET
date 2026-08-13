using JobTimeline.Core.Diagnostics;
using JobTimeline.Core.Events;

namespace JobTimeline.Core.Tests;

public sealed class DefaultJobEventSanitizerTests
{
    [Fact]
    public void Sanitize_RedactsCommonSecretsFromFailureReason()
    {
        var sanitizer = new DefaultJobEventSanitizer();
        var jobEvent = new JobEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobEventType.Failed,
            DateTimeOffset.UtcNow,
            "worker",
            Failure: new JobFailure(JobFailureClassification.Permanent, "password=hunter2 Bearer abc.def.ghi"));

        var sanitized = sanitizer.Sanitize(jobEvent);

        Assert.Equal("password=[REDACTED] Bearer [REDACTED]", sanitized.Failure!.SafeReason);
    }

    [Fact]
    public void Sanitize_NormalizesCorrelationAndRedactsSecretsFromLogReference()
    {
        var sanitizer = new DefaultJobEventSanitizer();
        var jobEvent = new JobEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobEventType.Failed,
            DateTimeOffset.UtcNow,
            "worker",
            Correlation: new JobCorrelation(" trace-123 ", " span-456 ", "https://logs.example/jobs?token=secret-value"));

        var sanitized = sanitizer.Sanitize(jobEvent);

        Assert.Equal("trace-123", sanitized.Correlation!.TraceId);
        Assert.Equal("span-456", sanitized.Correlation.SpanId);
        Assert.Equal("https://logs.example/jobs?token=[REDACTED]", sanitized.Correlation.LogReference);
    }
}
