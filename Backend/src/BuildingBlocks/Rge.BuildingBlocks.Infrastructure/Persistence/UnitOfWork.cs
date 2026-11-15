using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Infrastructure.Observability;

namespace Rge.BuildingBlocks.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork, IAsyncDisposable
{
    private readonly ConnectionScope _connectionScope;
    private readonly ILogger<UnitOfWork> _logger;
    private DbTransaction? _transaction;
    private Activity? _transactionActivity;
    private Stopwatch? _transactionStopwatch;
    private string _transactionId = string.Empty;
    private IsolationLevel _isolationLevel = IsolationLevel.ReadCommitted;

    public UnitOfWork(ConnectionScope connectionScope, ILogger<UnitOfWork> logger)
    {
        _connectionScope = connectionScope ?? throw new ArgumentNullException(nameof(connectionScope));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool HasActiveTransaction => _transaction is not null || _connectionScope.CurrentTransaction is not null;

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

    public async Task BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            return;
        }

        _isolationLevel = isolationLevel;
        _transactionId = Guid.NewGuid().ToString("N")[..12];
        _transactionStopwatch = Stopwatch.StartNew();
        _transactionActivity = ActivitySources.Database.StartActivity("db.transaction", ActivityKind.Client);
        _transactionActivity?.SetTag("db.system", "postgresql");
        _transactionActivity?.SetTag("db.transaction.id", _transactionId);
        _transactionActivity?.SetTag("db.transaction.isolation_level", isolationLevel.ToString());

        var connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        _transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        _connectionScope.SetActiveTransaction(_transaction);

        var ctx = ExecutionContextAccessor.Instance.Current;

        RuntimeExecutionStore.RecordDbTransaction(new DbTransactionTraceNode(
            TransactionId: _transactionId,
            EventType: "TransactionStarted",
            IsolationLevel: isolationLevel.ToString(),
            Outcome: "ACTIVE",
            SqlState: null,
            DurationMs: 0,
            OccurredAtUtc: DateTimeOffset.UtcNow));

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "[RUNTIME-DB-TX] Event=TransactionStarted CorrelationId={CorrelationId} TraceId={TraceId} " +
                "TransactionId={TransactionId} IsolationLevel={IsolationLevel} Module={Module} Handler={Handler}",
                ctx.CorrelationId,
                ctx.TraceId,
                _transactionId,
                isolationLevel,
                ctx.ModuleName ?? "Unknown",
                ctx.HandlerName ?? "Unknown");
        }
    }

    public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return 0;
        }

        var ctx = ExecutionContextAccessor.Instance.Current;

        try
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _transactionStopwatch?.Stop();

            _transactionActivity?.SetTag("db.transaction.outcome", "COMMITTED");
            _transactionActivity?.SetStatus(ActivityStatusCode.Ok);

            var durationMs = _transactionStopwatch?.Elapsed.TotalMilliseconds ?? 0;

            RuntimeExecutionStore.RecordDbTransaction(new DbTransactionTraceNode(
                TransactionId: _transactionId,
                EventType: "TransactionCommitted",
                IsolationLevel: _isolationLevel.ToString(),
                Outcome: "COMMITTED",
                SqlState: null,
                DurationMs: Math.Round(durationMs, 2),
                OccurredAtUtc: DateTimeOffset.UtcNow));

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "[RUNTIME-DB-TX] Event=TransactionCommitted CorrelationId={CorrelationId} TraceId={TraceId} " +
                    "TransactionId={TransactionId} DurationMs={DurationMs:0.00} Module={Module} Handler={Handler}",
                    ctx.CorrelationId,
                    ctx.TraceId,
                    _transactionId,
                    durationMs,
                    ctx.ModuleName ?? "Unknown",
                    ctx.HandlerName ?? "Unknown");
            }

            return 1;
        }
        catch (Exception ex)
        {
            _transactionStopwatch?.Stop();
            var sqlState = (ex as PostgresException)?.SqlState ?? "NONE";

            _transactionActivity?.SetTag("db.transaction.outcome", "COMMIT_FAILED");
            _transactionActivity?.SetTag("db.sql_state", sqlState);
            _transactionActivity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            var durationMs = _transactionStopwatch?.Elapsed.TotalMilliseconds ?? 0;

            RuntimeExecutionStore.RecordDbTransaction(new DbTransactionTraceNode(
                TransactionId: _transactionId,
                EventType: "TransactionCommitFailed",
                IsolationLevel: _isolationLevel.ToString(),
                Outcome: "COMMIT_FAILED",
                SqlState: sqlState,
                DurationMs: Math.Round(durationMs, 2),
                OccurredAtUtc: DateTimeOffset.UtcNow));

            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    ex,
                    "[RUNTIME-DB-TX] Event=TransactionCommitFailed CorrelationId={CorrelationId} TraceId={TraceId} " +
                    "TransactionId={TransactionId} SqlState={SqlState} DurationMs={DurationMs:0.00}",
                    ctx.CorrelationId,
                    ctx.TraceId,
                    _transactionId,
                    sqlState,
                    durationMs);
            }

            throw;
        }
        finally
        {
            await CleanupTransactionAsync().ConfigureAwait(false);
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        var ctx = ExecutionContextAccessor.Instance.Current;

        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _transactionStopwatch?.Stop();

            _transactionActivity?.SetTag("db.transaction.outcome", "ROLLED_BACK");
            _transactionActivity?.SetStatus(ActivityStatusCode.Error, "RolledBack");

            var durationMs = _transactionStopwatch?.Elapsed.TotalMilliseconds ?? 0;

            RuntimeExecutionStore.RecordDbTransaction(new DbTransactionTraceNode(
                TransactionId: _transactionId,
                EventType: "TransactionRolledBack",
                IsolationLevel: _isolationLevel.ToString(),
                Outcome: "ROLLED_BACK",
                SqlState: null,
                DurationMs: Math.Round(durationMs, 2),
                OccurredAtUtc: DateTimeOffset.UtcNow));

            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "[RUNTIME-DB-TX] Event=TransactionRolledBack CorrelationId={CorrelationId} TraceId={TraceId} " +
                    "TransactionId={TransactionId} DurationMs={DurationMs:0.00} Module={Module} Handler={Handler}",
                    ctx.CorrelationId,
                    ctx.TraceId,
                    _transactionId,
                    durationMs,
                    ctx.ModuleName ?? "Unknown",
                    ctx.HandlerName ?? "Unknown");
            }
        }
        finally
        {
            await CleanupTransactionAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask CleanupTransactionAsync()
    {
        _connectionScope.SetActiveTransaction(null);

        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }

        _transactionActivity?.Dispose();
        _transactionActivity = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await RollbackAsync().ConfigureAwait(false);
        }
    }
}
