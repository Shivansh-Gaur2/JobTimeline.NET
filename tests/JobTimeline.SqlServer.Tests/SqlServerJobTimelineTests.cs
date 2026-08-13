using JobTimeline.Core.Events;

namespace JobTimeline.SqlServer.Tests;

public sealed class SqlServerJobTimelineTests
{
    [SqlServerFact]
    public async Task AppendAsync_PersistsEventsStateAndDuplicateIdentity()
    {
        await using var timeline = CreateTimeline();
        var executionId = Guid.NewGuid();
        var started = CreateEvent(executionId, JobEventType.Started);

        var first = await timeline.AppendAsync(started);
        var duplicate = await timeline.AppendAsync(started);
        var succeeded = await timeline.AppendAsync(CreateEvent(executionId, JobEventType.Succeeded));

        var events = await timeline.GetEventsAsync(executionId);
        var state = await timeline.GetStateAsync(executionId);

        Assert.False(first.WasDuplicate);
        Assert.True(duplicate.WasDuplicate);
        Assert.Equal(1, first.Event.ExecutionSequence);
        Assert.Equal(2, succeeded.Event.ExecutionSequence);
        Assert.Equal(2, events.Count);
        Assert.Equal(JobEventType.Succeeded, state!.CurrentEventType);
    }

    [SqlServerFact]
    public async Task AppendAsync_ConcurrentEventsReceiveContiguousSequences()
    {
        await using var timeline = CreateTimeline();
        var executionId = Guid.NewGuid();
        await timeline.AppendAsync(CreateEvent(executionId, JobEventType.Started));

        var appends = Enumerable.Range(0, 24)
            .Select(_ => timeline.AppendAsync(CreateEvent(executionId, JobEventType.Heartbeat)).AsTask());
        await Task.WhenAll(appends);

        var events = await timeline.GetEventsAsync(executionId);
        var state = await timeline.GetStateAsync(executionId);

        Assert.Equal(25, events.Count);
        Assert.Equal(Enumerable.Range(1, 25).Select(sequence => (long)sequence), events.Select(item => item.ExecutionSequence));
        Assert.Equal(JobEventType.Heartbeat, state!.CurrentEventType);
    }

    private static SqlServerJobTimeline CreateTimeline()
    {
        return new SqlServerJobTimeline(new SqlServerJobTimelineOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("JOBTIMELINE_SQLSERVER_CONNECTION_STRING")!
        });
    }

    private static JobEvent CreateEvent(Guid executionId, JobEventType type)
    {
        return new JobEvent(
            Guid.NewGuid(),
            executionId,
            type,
            DateTimeOffset.UtcNow,
            "sql-server-tests");
    }
}
