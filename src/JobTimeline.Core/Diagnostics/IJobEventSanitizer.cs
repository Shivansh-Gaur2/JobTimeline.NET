using JobTimeline.Core.Events;

namespace JobTimeline.Core.Diagnostics;

public interface IJobEventSanitizer
{
    JobEvent Sanitize(JobEvent jobEvent);
}
