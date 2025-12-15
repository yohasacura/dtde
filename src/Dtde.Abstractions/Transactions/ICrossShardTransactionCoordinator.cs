namespace Dtde.Abstractions.Transactions;

/// <summary>
/// Coordinates cross-shard transactions using the two-phase commit protocol.
/// </summary>
/// <remarks>
/// The coordinator is responsible for:
/// <list type="bullet">
/// <item>Creating and managing transaction instances</item>
/// <item>Coordinating the prepare phase across all participants</item>
/// <item>Making the global commit/abort decision</item>
/// <item>Coordinating the commit/abort phase across all participants</item>
/// <item>Managing transaction recovery for in-doubt transactions</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Simple transaction with automatic shard detection
/// await coordinator.ExecuteInTransactionAsync(
///     async (transaction) =>
///     {
///         // Operations are automatically enlisted
///         context.Update(entity1); // Goes to shard-A
///         context.Add(entity2);    // Goes to shard-B
///         await context.SaveChangesAsync();
///     });
///
/// // Manual transaction control
/// await using var transaction = await coordinator.BeginTransactionAsync(new TransactionOptions
/// {
///     IsolationLevel = CrossShardIsolationLevel.Snapshot,
///     Timeout = TimeSpan.FromSeconds(30)
/// });
///
/// try
/// {
///     await transaction.EnlistAsync("shard-2024");
///     await transaction.EnlistAsync("shard-2025");
///
///     // Perform operations...
///
///     await transaction.CommitAsync();
/// }
/// catch
/// {
///     await transaction.RollbackAsync();
///     throw;
/// }
/// </code>
/// </example>
public interface ICrossShardTransactionCoordinator
{
    /// <summary>
    /// Gets the current ambient transaction, if any.
    /// </summary>
    ICrossShardTransaction? CurrentTransaction { get; }

    /// <summary>
    /// Gets whether there is an active ambient transaction.
    /// </summary>
    bool HasActiveTransaction => CurrentTransaction is not null;

    /// <summary>
    /// Begins a new cross-shard transaction with default options.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new transaction.</returns>
    Task<ICrossShardTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a new cross-shard transaction with the specified options.
    /// </summary>
    /// <param name="options">The transaction options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new transaction.</returns>
    Task<ICrossShardTransaction> BeginTransactionAsync(
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an action within a cross-shard transaction scope.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the transaction.</returns>
    /// <remarks>
    /// The transaction is automatically committed if the action completes successfully,
    /// or rolled back if an exception is thrown.
    /// </remarks>
    Task ExecuteInTransactionAsync(
        Func<ICrossShardTransaction, Task> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an action within a cross-shard transaction scope with options.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="options">The transaction options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the transaction.</returns>
    Task ExecuteInTransactionAsync(
        Func<ICrossShardTransaction, Task> action,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an action within a cross-shard transaction scope and returns a result.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the action.</returns>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<ICrossShardTransaction, Task<TResult>> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an action within a cross-shard transaction scope with options and returns a result.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <param name="options">The transaction options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the action.</returns>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<ICrossShardTransaction, Task<TResult>> action,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers any in-doubt transactions that may exist from previous failures.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of transactions recovered.</returns>
    Task<int> RecoverAsync(CancellationToken cancellationToken = default);
}
