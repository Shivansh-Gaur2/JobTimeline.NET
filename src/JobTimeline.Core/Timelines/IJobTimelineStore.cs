using JobTimeline.Core.Events;
using JobTimeline.Core.Projections;

namespace JobTimeline.Core.Timelines;

public interface IJobTimelineStore
{
    ValueTask<AppendJobEventResult> AppendAsync(JobEvent jobEvent, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AcceptedJobEvent>> GetEventsAsync(Guid executionId, CancellationToken cancellationToken = default);

    ValueTask<JobExecutionState?> GetStateAsync(Guid executionId, CancellationToken cancellationToken = default);
}
