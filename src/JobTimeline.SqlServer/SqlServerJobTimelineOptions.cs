namespace JobTimeline.SqlServer;

public sealed class SqlServerJobTimelineOptions
{
    public required string ConnectionString { get; init; }

    public bool AutoCreateSchema { get; init; } = true;
}
