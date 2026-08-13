using JobTimeline.Core.Events;

namespace JobTimeline.Core.Tests;

public sealed class JobEventValidatorTests
{
    [Fact]
    public void Validate_RejectsOccurrenceTimeThatIsNotUtc()
    {
        var jobEvent = new JobEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobEventType.Enqueued,
            new DateTimeOffset(2026, 8, 13, 10, 15, 0, TimeSpan.FromHours(5.5)),
            "tests");

        Assert.Throws<ArgumentException>(() => JobEventValidator.Validate(jobEvent));
    }
}
