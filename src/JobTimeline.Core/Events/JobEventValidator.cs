namespace JobTimeline.Core.Events;

public static class JobEventValidator
{
    public static void Validate(JobEvent jobEvent)
    {
        ArgumentNullException.ThrowIfNull(jobEvent);

        if (jobEvent.EventId == Guid.Empty)
        {
            throw new ArgumentException("An event identifier is required.", nameof(jobEvent));
        }

        if (jobEvent.ExecutionId == Guid.Empty)
        {
            throw new ArgumentException("An execution identifier is required.", nameof(jobEvent));
        }

        if (string.IsNullOrWhiteSpace(jobEvent.Source))
        {
            throw new ArgumentException("An event source is required.", nameof(jobEvent));
        }

        if (!Enum.IsDefined(jobEvent.Type))
        {
            throw new ArgumentException("The event type is not supported.", nameof(jobEvent));
        }

        if (jobEvent.AttemptNumber < 1)
        {
            throw new ArgumentException("An attempt number must be at least one.", nameof(jobEvent));
        }

        if (jobEvent.SourceSequence is not null && string.IsNullOrWhiteSpace(jobEvent.SourceStream))
        {
            throw new ArgumentException("A source stream is required when a source sequence is supplied.", nameof(jobEvent));
        }

        if (jobEvent.SourceSequence < 0)
        {
            throw new ArgumentException("A source sequence cannot be negative.", nameof(jobEvent));
        }
    }
}
