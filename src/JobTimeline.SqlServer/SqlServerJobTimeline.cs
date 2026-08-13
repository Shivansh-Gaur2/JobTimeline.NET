using System.Data;
using JobTimeline.Core.Diagnostics;
using JobTimeline.Core.Events;
using JobTimeline.Core.Projections;
using JobTimeline.Core.Timelines;
using Microsoft.Data.SqlClient;

namespace JobTimeline.SqlServer;

public sealed class SqlServerJobTimeline : IJobTimelineStore, IAsyncDisposable
{
    private const int MaximumDeadlockRetries = 3;
    private readonly SqlServerJobTimelineOptions _options;
    private readonly IJobEventSanitizer _sanitizer;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public SqlServerJobTimeline(
        SqlServerJobTimelineOptions options,
        TimeProvider? timeProvider = null,
        IJobEventSanitizer? sanitizer = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new ArgumentException("A SQL Server connection string is required.", nameof(options));
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
        _sanitizer = sanitizer ?? new DefaultJobEventSanitizer();
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || !_options.AutoCreateSchema)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = new SqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(SqlServerJobTimelineSchema.Create, connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async ValueTask<AppendJobEventResult> AppendAsync(JobEvent jobEvent, CancellationToken cancellationToken = default)
    {
        jobEvent = _sanitizer.Sanitize(jobEvent);
        JobEventValidator.Validate(jobEvent);

        for (var retryAttempt = 0; ; retryAttempt++)
        {
            try
            {
                return await AppendOnceAsync(jobEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (SqlException exception) when (exception.Number == 1205 && retryAttempt < MaximumDeadlockRetries)
            {
                var delay = TimeSpan.FromMilliseconds(25 * (1 << retryAttempt));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<AppendJobEventResult> AppendOnceAsync(JobEvent jobEvent, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await AcquireExecutionLockAsync(connection, transaction, jobEvent.ExecutionId, cancellationToken).ConfigureAwait(false);

        var existingEvent = await GetEventByIdentityAsync(connection, transaction, jobEvent, cancellationToken).ConfigureAwait(false);
        if (existingEvent is not null)
        {
            if (existingEvent.Event != jobEvent)
            {
                throw new InvalidOperationException("An existing event identity cannot be reused with different event data.");
            }

            var duplicateState = await GetStateAsync(connection, transaction, jobEvent.ExecutionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("A stored event is missing its execution-state projection.");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new AppendJobEventResult(existingEvent, duplicateState, true);
        }

        await EnsureSourcePositionIsUniqueAsync(connection, transaction, jobEvent, cancellationToken).ConfigureAwait(false);
        var sequence = await GetNextSequenceAsync(connection, transaction, jobEvent.ExecutionId, cancellationToken).ConfigureAwait(false);
        var acceptedEvent = new AcceptedJobEvent(jobEvent, sequence, _timeProvider.GetUtcNow());
        await InsertEventAsync(connection, transaction, acceptedEvent, cancellationToken).ConfigureAwait(false);

        var events = await GetEventsAsync(connection, transaction, jobEvent.ExecutionId, cancellationToken).ConfigureAwait(false);
        var state = JobExecutionProjector.Project(events);
        await UpsertStateAsync(connection, transaction, state, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new AppendJobEventResult(acceptedEvent, state, false);
    }

    public async ValueTask<IReadOnlyList<AcceptedJobEvent>> GetEventsAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetEventsAsync(connection, null, executionId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<JobExecutionState?> GetStateAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetStateAsync(connection, null, executionId, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _initializationLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<AcceptedJobEvent?> GetEventByIdentityAsync(SqlConnection connection, SqlTransaction transaction, JobEvent jobEvent, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) * FROM JobTimeline.Events WITH (UPDLOCK, HOLDLOCK)
            WHERE ExecutionId = @executionId AND Source = @source AND EventId = @eventId;
            """;
        await using var command = CreateCommand(sql, connection, transaction);
        Add(command, "@executionId", SqlDbType.UniqueIdentifier, jobEvent.ExecutionId);
        Add(command, "@source", SqlDbType.NVarChar, jobEvent.Source, 128);
        Add(command, "@eventId", SqlDbType.UniqueIdentifier, jobEvent.EventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEvent(reader) : null;
    }

    private static async Task AcquireExecutionLockAsync(SqlConnection connection, SqlTransaction transaction, Guid executionId, CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @lockResult int;
            EXEC @lockResult = sp_getapplock
                @Resource = @resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 30000;
            SELECT @lockResult;
            """;

        await using var command = CreateCommand(sql, connection, transaction);
        Add(command, "@resource", SqlDbType.NVarChar, $"JobTimeline.Execution.{executionId:N}", 128);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (result < 0)
        {
            throw new InvalidOperationException("Could not acquire the execution append lock.");
        }
    }

    private static async Task EnsureSourcePositionIsUniqueAsync(SqlConnection connection, SqlTransaction transaction, JobEvent jobEvent, CancellationToken cancellationToken)
    {
        if (jobEvent.SourceStream is null || jobEvent.SourceSequence is null)
        {
            return;
        }

        const string sql = """
            SELECT COUNT_BIG(1) FROM JobTimeline.Events WITH (UPDLOCK, HOLDLOCK)
            WHERE ExecutionId = @executionId AND Source = @source
              AND SourceStream = @sourceStream AND SourceSequence = @sourceSequence;
            """;
        await using var command = CreateCommand(sql, connection, transaction);
        Add(command, "@executionId", SqlDbType.UniqueIdentifier, jobEvent.ExecutionId);
        Add(command, "@source", SqlDbType.NVarChar, jobEvent.Source, 128);
        Add(command, "@sourceStream", SqlDbType.NVarChar, jobEvent.SourceStream, 256);
        Add(command, "@sourceSequence", SqlDbType.BigInt, jobEvent.SourceSequence);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
        {
            throw new InvalidOperationException("A source sequence cannot identify two different events in the same source stream.");
        }
    }

    private static async Task<long> GetNextSequenceAsync(SqlConnection connection, SqlTransaction transaction, Guid executionId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT ISNULL(MAX(ExecutionSequence), 0) + 1 FROM JobTimeline.Events WITH (UPDLOCK, HOLDLOCK) WHERE ExecutionId = @executionId;";
        await using var command = CreateCommand(sql, connection, transaction);
        Add(command, "@executionId", SqlDbType.UniqueIdentifier, executionId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task InsertEventAsync(SqlConnection connection, SqlTransaction transaction, AcceptedJobEvent acceptedEvent, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO JobTimeline.Events
            (ExecutionId, ExecutionSequence, EventId, EventType, OccurredAtUtc, RecordedAtUtc, Source, SourceStream, SourceSequence, AttemptNumber, FailureClassification, FailureReason, TraceId, SpanId, LogReference, ParentExecutionId)
            VALUES
            (@executionId, @executionSequence, @eventId, @eventType, @occurredAtUtc, @recordedAtUtc, @source, @sourceStream, @sourceSequence, @attemptNumber, @failureClassification, @failureReason, @traceId, @spanId, @logReference, @parentExecutionId);
            """;
        await using var command = CreateCommand(sql, connection, transaction);
        AddEventParameters(command, acceptedEvent);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<AcceptedJobEvent>> GetEventsAsync(SqlConnection connection, SqlTransaction? transaction, Guid executionId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM JobTimeline.Events WHERE ExecutionId = @executionId ORDER BY ExecutionSequence;";
        await using var command = CreateCommand(sql, connection, transaction);
        Add(command, "@executionId", SqlDbType.UniqueIdentifier, executionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var events = new List<AcceptedJobEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Add(ReadEvent(reader));
        }

        return events;
    }

    private static async Task<JobExecutionState?> GetStateAsync(SqlConnection connection, SqlTransaction? transaction, Guid executionId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT TOP (1) * FROM JobTimeline.ExecutionStates WHERE ExecutionId = @executionId;";
        await using var command = CreateCommand(sql, connection, transaction);
        Add(command, "@executionId", SqlDbType.UniqueIdentifier, executionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadState(reader) : null;
    }

    private static async Task UpsertStateAsync(SqlConnection connection, SqlTransaction transaction, JobExecutionState state, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE JobTimeline.ExecutionStates
            SET CurrentEventType = @currentEventType, LastUpdatedAtUtc = @lastUpdatedAtUtc,
                ExecutionSequence = @executionSequence, Status = @status, AttemptNumber = @attemptNumber,
                LastProgressAtUtc = @lastProgressAtUtc, FailureClassification = @failureClassification,
                FailureReason = @failureReason, TraceId = @traceId, SpanId = @spanId, LogReference = @logReference
            WHERE ExecutionId = @executionId;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO JobTimeline.ExecutionStates
                (ExecutionId, CurrentEventType, LastUpdatedAtUtc, ExecutionSequence, Status, AttemptNumber, LastProgressAtUtc, FailureClassification, FailureReason, TraceId, SpanId, LogReference)
                VALUES
                (@executionId, @currentEventType, @lastUpdatedAtUtc, @executionSequence, @status, @attemptNumber, @lastProgressAtUtc, @failureClassification, @failureReason, @traceId, @spanId, @logReference);
            END;
            """;
        await using var command = CreateCommand(sql, connection, transaction);
        AddStateParameters(command, state);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AcceptedJobEvent ReadEvent(SqlDataReader reader)
    {
        var failure = reader.IsDBNull(reader.GetOrdinal("FailureClassification"))
            ? null
            : new JobFailure((JobFailureClassification)reader.GetInt32(reader.GetOrdinal("FailureClassification")), ReadNullableString(reader, "FailureReason"));
        var correlation = ReadCorrelation(reader);
        var jobEvent = new JobEvent(
            reader.GetGuid(reader.GetOrdinal("EventId")),
            reader.GetGuid(reader.GetOrdinal("ExecutionId")),
            (JobEventType)reader.GetInt32(reader.GetOrdinal("EventType")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("OccurredAtUtc")),
            reader.GetString(reader.GetOrdinal("Source")),
            ReadNullableString(reader, "SourceStream"),
            ReadNullableInt64(reader, "SourceSequence"),
            reader.GetInt32(reader.GetOrdinal("AttemptNumber")),
            failure,
            correlation,
            ReadNullableGuid(reader, "ParentExecutionId"));

        return new AcceptedJobEvent(
            jobEvent,
            reader.GetInt64(reader.GetOrdinal("ExecutionSequence")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("RecordedAtUtc")));
    }

    private static JobExecutionState ReadState(SqlDataReader reader)
    {
        var failure = reader.IsDBNull(reader.GetOrdinal("FailureClassification"))
            ? null
            : new JobFailure((JobFailureClassification)reader.GetInt32(reader.GetOrdinal("FailureClassification")), ReadNullableString(reader, "FailureReason"));

        return new JobExecutionState(
            reader.GetGuid(reader.GetOrdinal("ExecutionId")),
            ReadNullableEventType(reader, "CurrentEventType"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("LastUpdatedAtUtc")),
            reader.GetInt64(reader.GetOrdinal("ExecutionSequence")),
            (JobExecutionStateStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            reader.GetInt32(reader.GetOrdinal("AttemptNumber")),
            ReadNullableDateTimeOffset(reader, "LastProgressAtUtc"),
            failure,
            ReadCorrelation(reader));
    }

    private static JobCorrelation? ReadCorrelation(SqlDataReader reader)
    {
        var traceId = ReadNullableString(reader, "TraceId");
        var spanId = ReadNullableString(reader, "SpanId");
        var logReference = ReadNullableString(reader, "LogReference");
        return traceId is null && spanId is null && logReference is null ? null : new JobCorrelation(traceId, spanId, logReference);
    }

    private static SqlCommand CreateCommand(string sql, SqlConnection connection, SqlTransaction? transaction)
    {
        return new SqlCommand(sql, connection, transaction);
    }

    private static void AddEventParameters(SqlCommand command, AcceptedJobEvent acceptedEvent)
    {
        var jobEvent = acceptedEvent.Event;
        Add(command, "@executionId", SqlDbType.UniqueIdentifier, jobEvent.ExecutionId);
        Add(command, "@executionSequence", SqlDbType.BigInt, acceptedEvent.ExecutionSequence);
        Add(command, "@eventId", SqlDbType.UniqueIdentifier, jobEvent.EventId);
        Add(command, "@eventType", SqlDbType.Int, (int)jobEvent.Type);
        Add(command, "@occurredAtUtc", SqlDbType.DateTimeOffset, jobEvent.OccurredAtUtc);
        Add(command, "@recordedAtUtc", SqlDbType.DateTimeOffset, acceptedEvent.RecordedAtUtc);
        Add(command, "@source", SqlDbType.NVarChar, jobEvent.Source, 128);
        Add(command, "@sourceStream", SqlDbType.NVarChar, jobEvent.SourceStream, 256);
        Add(command, "@sourceSequence", SqlDbType.BigInt, jobEvent.SourceSequence);
        Add(command, "@attemptNumber", SqlDbType.Int, jobEvent.AttemptNumber);
        Add(command, "@failureClassification", SqlDbType.Int, jobEvent.Failure is null ? null : (int)jobEvent.Failure.Classification);
        Add(command, "@failureReason", SqlDbType.NVarChar, jobEvent.Failure?.SafeReason, 1024);
        Add(command, "@traceId", SqlDbType.NVarChar, jobEvent.Correlation?.TraceId, 128);
        Add(command, "@spanId", SqlDbType.NVarChar, jobEvent.Correlation?.SpanId, 128);
        Add(command, "@logReference", SqlDbType.NVarChar, jobEvent.Correlation?.LogReference, 512);
        Add(command, "@parentExecutionId", SqlDbType.UniqueIdentifier, jobEvent.ParentExecutionId);
    }

    private static void AddStateParameters(SqlCommand command, JobExecutionState state)
    {
        Add(command, "@executionId", SqlDbType.UniqueIdentifier, state.ExecutionId);
        Add(command, "@currentEventType", SqlDbType.Int, state.CurrentEventType is null ? null : (int)state.CurrentEventType.Value);
        Add(command, "@lastUpdatedAtUtc", SqlDbType.DateTimeOffset, state.LastUpdatedAtUtc);
        Add(command, "@executionSequence", SqlDbType.BigInt, state.ExecutionSequence);
        Add(command, "@status", SqlDbType.Int, (int)state.Status);
        Add(command, "@attemptNumber", SqlDbType.Int, state.AttemptNumber);
        Add(command, "@lastProgressAtUtc", SqlDbType.DateTimeOffset, state.LastProgressAtUtc);
        Add(command, "@failureClassification", SqlDbType.Int, state.Failure is null ? null : (int)state.Failure.Classification);
        Add(command, "@failureReason", SqlDbType.NVarChar, state.Failure?.SafeReason, 1024);
        Add(command, "@traceId", SqlDbType.NVarChar, state.Correlation?.TraceId, 128);
        Add(command, "@spanId", SqlDbType.NVarChar, state.Correlation?.SpanId, 128);
        Add(command, "@logReference", SqlDbType.NVarChar, state.Correlation?.LogReference, 512);
    }

    private static void Add(SqlCommand command, string name, SqlDbType type, object? value, int size = 0)
    {
        var parameter = size == 0
            ? command.Parameters.Add(name, type)
            : command.Parameters.Add(name, type, size);
        parameter.Value = value ?? DBNull.Value;
    }

    private static string? ReadNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static long? ReadNullableInt64(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static Guid? ReadNullableGuid(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static JobEventType? ReadNullableEventType(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : (JobEventType)reader.GetInt32(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
