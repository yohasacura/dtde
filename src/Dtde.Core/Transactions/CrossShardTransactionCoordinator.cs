using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.Core.Transactions;

/// <summary>
/// Coordinates cross-shard transactions using the two-phase commit (2PC) protocol.
/// </summary>
/// <remarks>
/// <para>
/// The coordinator manages the lifecycle of cross-shard transactions, including:
/// </para>
/// <list type="bullet">
/// <item>Transaction creation and configuration</item>
/// <item>Shard enlistment and participant management</item>
/// <item>Two-phase commit protocol execution</item>
/// <item>Transaction recovery for in-doubt transactions</item>
/// </list>
/// <para>
/// For best performance, prefer using the ExecuteInTransactionAsync methods
/// which provide automatic transaction management with proper error handling.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Using automatic transaction management (recommended)
/// await coordinator.ExecuteInTransactionAsync(async transaction =>
/// {
///     var participant1 = await transaction.GetOrCreateParticipantAsync("shard-2024");
///     participant1.Context.Update(entity1);
///
///     var participant2 = await transaction.GetOrCreateParticipantAsync("shard-2025");
///     participant2.Context.Add(entity2);
/// });
///
/// // Manual transaction control
/// await using var transaction = await coordinator.BeginTransactionAsync();
/// try
/// {
///     await transaction.EnlistAsync("shard-2024");
///     // ... perform operations ...
///     await transaction.CommitAsync();
/// }
/// catch
/// {
///     await transaction.RollbackAsync();
///     throw;
/// }
/// </code>
/// </example>
public sealed class CrossShardTransactionCoordinator : ICrossShardTransactionCoordinator
{
    private readonly IShardRegistry _shardRegistry;
    private readonly Func<string, CancellationToken, Task<DbContext>> _contextFactory;
    private readonly ILogger<CrossShardTransactionCoordinator> _logger;
    private readonly ILogger<CrossShardTransaction> _transactionLogger;
    private readonly AsyncLocal<ICrossShardTransaction?> _currentTransaction = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossShardTransactionCoordinator"/> class.
    /// </summary>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="contextFactory">Factory for creating DbContext instances for shards.</param>
    /// <param name="logger">The logger for the coordinator.</param>
    /// <param name="transactionLogger">The logger for transactions.</param>
    public CrossShardTransactionCoordinator(
        IShardRegistry shardRegistry,
        Func<string, CancellationToken, Task<DbContext>> contextFactory,
        ILogger<CrossShardTransactionCoordinator> logger,
        ILogger<CrossShardTransaction> transactionLogger)
    {
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _transactionLogger = transactionLogger ?? throw new ArgumentNullException(nameof(transactionLogger));
    }

    /// <inheritdoc />
    public ICrossShardTransaction? CurrentTransaction => _currentTransaction.Value;

    /// <inheritdoc />
    public Task<ICrossShardTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        return BeginTransactionAsync(CrossShardTransactionOptions.Default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ICrossShardTransaction> BeginTransactionAsync(
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_currentTransaction.Value is not null)
        {
            throw new InvalidOperationException(
                "A cross-shard transaction is already active in the current context. " +
                "Nested transactions are not supported.");
        }

        var transactionId = GenerateTransactionId(options.TransactionName);

        TransactionLogMessages.BeginningTransactionWithOptions(_logger, transactionId, options.IsolationLevel.ToString());

        var transaction = new CrossShardTransaction(
            transactionId,
            options,
            _shardRegistry,
            _contextFactory,
            _transactionLogger);

        _currentTransaction.Value = transaction;

        return Task.FromResult<ICrossShardTransaction>(transaction);
    }

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(
        Func<ICrossShardTransaction, Task> action,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInTransactionAsync(action, CrossShardTransactionOptions.Default, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ExecuteInTransactionAsync(
        Func<ICrossShardTransaction, Task> action,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);

        var transaction = await BeginTransactionAsync(options, cancellationToken)
            .ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await ExecuteWithRetryAsync(
                    async () =>
                    {
                        await action(transaction).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    },
                    options,
                    cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Must catch all exceptions to ensure proper rollback
            catch (Exception ex)
#pragma warning restore CA1031
            {
                TransactionLogMessages.TransactionFailed(_logger, ex, transaction.TransactionId);

                try
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Must catch all exceptions during rollback to prevent masking original exception
                catch (Exception rollbackEx)
#pragma warning restore CA1031
                {
                    TransactionLogMessages.TransactionRollbackError(_logger, rollbackEx, transaction.TransactionId);
                }

                throw;
            }
            finally
            {
                _currentTransaction.Value = null;
            }
        }
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<ICrossShardTransaction, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInTransactionAsync(action, CrossShardTransactionOptions.Default, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<ICrossShardTransaction, Task<TResult>> action,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);

        var transaction = await BeginTransactionAsync(options, cancellationToken)
            .ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                var result = default(TResult);

                await ExecuteWithRetryAsync(
                    async () =>
                    {
                        result = await action(transaction).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    },
                    options,
                    cancellationToken).ConfigureAwait(false);

                return result!;
            }
#pragma warning disable CA1031 // Must catch all exceptions to ensure proper rollback
            catch (Exception ex)
#pragma warning restore CA1031
            {
                TransactionLogMessages.TransactionFailed(_logger, ex, transaction.TransactionId);

                try
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Must catch all exceptions during rollback to prevent masking original exception
                catch (Exception rollbackEx)
#pragma warning restore CA1031
                {
                    TransactionLogMessages.TransactionRollbackError(_logger, rollbackEx, transaction.TransactionId);
                }

                throw;
            }
            finally
            {
                _currentTransaction.Value = null;
            }
        }
    }

    /// <inheritdoc />
    public Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        // Transaction recovery requires persistent transaction logs
        // This is a placeholder for future implementation
        TransactionLogMessages.NoInDoubtTransactions(_logger);
        return Task.FromResult(0);
    }

    private async Task ExecuteWithRetryAsync(
        Func<Task> action,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.EnableRetry)
        {
            await action().ConfigureAwait(false);
            return;
        }

        var attempts = 0;
        var delay = options.RetryDelay;

        while (true)
        {
            attempts++;

            try
            {
                await action().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsTransientError(ex) && attempts < options.MaxRetryAttempts)
            {
                TransactionLogMessages.RetryingTransaction(_logger, ex, attempts, options.MaxRetryAttempts, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                if (options.UseExponentialBackoff)
                {
                    delay = TimeSpan.FromMilliseconds(Math.Min(
                        delay.TotalMilliseconds * 2,
                        options.MaxRetryDelay.TotalMilliseconds));
                }
            }
        }
    }

    private static bool IsTransientError(Exception ex)
    {
        // Check for transient database errors that can be retried
        return ex is TimeoutException
            || ex is OperationCanceledException
            || (ex.InnerException is not null && IsTransientError(ex.InnerException))
            || ex.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateTransactionId(string? transactionName)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var uniqueId = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..8];

        return string.IsNullOrEmpty(transactionName)
            ? $"XS-{timestamp}-{uniqueId}"
            : $"XS-{transactionName}-{timestamp}-{uniqueId}";
    }
}
