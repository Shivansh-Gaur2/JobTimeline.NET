using System.Text.RegularExpressions;
using JobTimeline.Core.Events;

namespace JobTimeline.Core.Diagnostics;

public sealed class DefaultJobEventSanitizer : IJobEventSanitizer
{
    private const int MaximumReasonLength = 1_024;
    private const int MaximumLogReferenceLength = 512;
    private static readonly Regex SensitiveAssignment = new(
        @"(?i)\b(password|pwd|secret|token|api[_-]?key|connection\s*string)\s*[:=]\s*[^\s;,]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BearerToken = new(
        @"(?i)\bbearer\s+[^\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public JobEvent Sanitize(JobEvent jobEvent)
    {
        ArgumentNullException.ThrowIfNull(jobEvent);

        var sanitizedFailure = jobEvent.Failure is null
            ? null
            : jobEvent.Failure with { SafeReason = SanitizeReason(jobEvent.Failure.SafeReason) };
        var sanitizedCorrelation = jobEvent.Correlation is null
            ? null
            : jobEvent.Correlation with
            {
                TraceId = NormalizeIdentifier(jobEvent.Correlation.TraceId, 128),
                SpanId = NormalizeIdentifier(jobEvent.Correlation.SpanId, 128),
                LogReference = SanitizeLogReference(jobEvent.Correlation.LogReference)
            };

        return jobEvent with
        {
            Source = jobEvent.Source.Trim(),
            SourceStream = string.IsNullOrWhiteSpace(jobEvent.SourceStream) ? null : jobEvent.SourceStream.Trim(),
            Failure = sanitizedFailure,
            Correlation = sanitizedCorrelation
        };
    }

    private static string? SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var redacted = SensitiveAssignment.Replace(reason, "$1=[REDACTED]");
        redacted = BearerToken.Replace(redacted, "Bearer [REDACTED]");

        return redacted.Length <= MaximumReasonLength
            ? redacted
            : redacted[..MaximumReasonLength];
    }

    private static string? SanitizeLogReference(string? logReference)
    {
        if (string.IsNullOrWhiteSpace(logReference))
        {
            return null;
        }

        var redacted = SensitiveAssignment.Replace(logReference.Trim(), "$1=[REDACTED]");
        redacted = BearerToken.Replace(redacted, "Bearer [REDACTED]");
        return redacted.Length <= MaximumLogReferenceLength
            ? redacted
            : redacted[..MaximumLogReferenceLength];
    }

    private static string? NormalizeIdentifier(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}
