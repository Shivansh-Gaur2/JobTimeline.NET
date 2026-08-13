using JobTimeline.Core.Events;
using JobTimeline.Core.Projections;

namespace JobTimeline.Core.Timelines;

public sealed record AppendJobEventResult(
    AcceptedJobEvent Event,
    JobExecutionState State,
    bool WasDuplicate);
