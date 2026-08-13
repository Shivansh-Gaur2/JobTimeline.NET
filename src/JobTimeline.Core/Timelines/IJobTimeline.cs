using JobTimeline.Core.Events;
using JobTimeline.Core.Projections;

namespace JobTimeline.Core.Timelines;

public interface IJobTimeline
{
    AppendJobEventResult Append(JobEvent jobEvent);

    IReadOnlyList<AcceptedJobEvent> GetEvents(Guid executionId);

    JobExecutionState? GetState(Guid executionId);
}
