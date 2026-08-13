namespace JobTimeline.SqlServer;

public static class SqlServerJobTimelineSchema
{
    public const string Create = """
        DECLARE @lockResult int;
        EXEC @lockResult = sp_getapplock
            @Resource = N'JobTimeline.Schema',
            @LockMode = N'Exclusive',
            @LockOwner = N'Session',
            @LockTimeout = 60000;

        IF @lockResult < 0
            THROW 51000, 'Could not acquire the JobTimeline schema initialization lock.', 1;

        IF SCHEMA_ID(N'JobTimeline') IS NULL EXEC(N'CREATE SCHEMA JobTimeline');

        IF OBJECT_ID(N'JobTimeline.Events', N'U') IS NULL
        BEGIN
            CREATE TABLE JobTimeline.Events
            (
                ExecutionId uniqueidentifier NOT NULL,
                ExecutionSequence bigint NOT NULL,
                EventId uniqueidentifier NOT NULL,
                EventType int NOT NULL,
                OccurredAtUtc datetimeoffset(7) NOT NULL,
                RecordedAtUtc datetimeoffset(7) NOT NULL,
                Source nvarchar(128) NOT NULL,
                SourceStream nvarchar(256) NULL,
                SourceSequence bigint NULL,
                AttemptNumber int NOT NULL,
                FailureClassification int NULL,
                FailureReason nvarchar(1024) NULL,
                TraceId nvarchar(128) NULL,
                SpanId nvarchar(128) NULL,
                LogReference nvarchar(512) NULL,
                ParentExecutionId uniqueidentifier NULL,
                CONSTRAINT PK_JobTimeline_Events PRIMARY KEY (ExecutionId, ExecutionSequence),
                CONSTRAINT UQ_JobTimeline_Events_EventIdentity UNIQUE (ExecutionId, Source, EventId),
                CONSTRAINT CK_JobTimeline_Events_AttemptNumber CHECK (AttemptNumber >= 1),
                CONSTRAINT CK_JobTimeline_Events_SourceSequence CHECK (SourceSequence IS NULL OR SourceSequence >= 0),
                CONSTRAINT CK_JobTimeline_Events_SourcePosition CHECK (SourceSequence IS NULL OR SourceStream IS NOT NULL)
            );

            CREATE UNIQUE INDEX UX_JobTimeline_Events_SourcePosition
                ON JobTimeline.Events (ExecutionId, Source, SourceStream, SourceSequence)
                WHERE SourceStream IS NOT NULL AND SourceSequence IS NOT NULL;
        END;

        IF OBJECT_ID(N'JobTimeline.ExecutionStates', N'U') IS NULL
        BEGIN
            CREATE TABLE JobTimeline.ExecutionStates
            (
                ExecutionId uniqueidentifier NOT NULL CONSTRAINT PK_JobTimeline_ExecutionStates PRIMARY KEY,
                CurrentEventType int NULL,
                LastUpdatedAtUtc datetimeoffset(7) NOT NULL,
                ExecutionSequence bigint NOT NULL,
                Status int NOT NULL,
                AttemptNumber int NOT NULL,
                LastProgressAtUtc datetimeoffset(7) NULL,
                FailureClassification int NULL,
                FailureReason nvarchar(1024) NULL,
                TraceId nvarchar(128) NULL,
                SpanId nvarchar(128) NULL,
                LogReference nvarchar(512) NULL
            );
        END;

        EXEC sp_releaseapplock @Resource = N'JobTimeline.Schema', @LockOwner = N'Session';
        """;
}
