namespace JobTimeline.SqlServer.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JOBTIMELINE_SQLSERVER_CONNECTION_STRING")))
        {
            Skip = "Set JOBTIMELINE_SQLSERVER_CONNECTION_STRING to run SQL Server integration tests.";
        }
    }
}
