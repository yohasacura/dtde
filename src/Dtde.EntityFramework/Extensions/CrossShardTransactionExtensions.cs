using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// Extension methods for working with cross-shard transactions.
/// </summary>
public static class CrossShardTransactionExtensions
{
    /// <summary>
    /// Executes an action within a cross-shard transaction scope.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="shardIds">The shard IDs to enlist in the transaction.</param>
    /// <param name="action">The action to execute within the transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the transaction.</returns>
    /// <example>
    /// <code>
    /// await context.ExecuteInCrossShardTransactionAsync(
    ///     ["shard-2024", "shard-2025"],
    ///     async (transaction, ctx) =>
    ///     {
    ///         // Perform operations that span both shards
    ///         await ctx.SaveChangesAsync();
    ///     });
    /// </code>
    /// </example>
    public static async Task ExecuteInCrossShardTransactionAsync<TContext>(
        this TContext context,
        IEnumerable<string> shardIds,
        Func<ICrossShardTransaction, TContext, Task> action,
        CancellationToken cancellationToken = default) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shardIds);
        ArgumentNullException.ThrowIfNull(action);

        var coordinator = GetTransactionCoordinator(context);

        await coordinator.ExecuteInTransactionAsync(
            async transaction =>
            {
                // Enlist all specified shards
                foreach (var shardId in shardIds)
                {
                    await transaction.EnlistAsync(shardId, cancellationToken).ConfigureAwait(false);
                }

                await action(transaction, context).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes an action within a cross-shard transaction scope with custom options.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="shardIds">The shard IDs to enlist in the transaction.</param>
    /// <param name="action">The action to execute within the transaction.</param>
    /// <param name="options">The transaction options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the transaction.</returns>
    public static async Task ExecuteInCrossShardTransactionAsync<TContext>(
        this TContext context,
        IEnumerable<string> shardIds,
        Func<ICrossShardTransaction, TContext, Task> action,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shardIds);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);

        var coordinator = GetTransactionCoordinator(context);

        await coordinator.ExecuteInTransactionAsync(
            async transaction =>
            {
                foreach (var shardId in shardIds)
                {
                    await transaction.EnlistAsync(shardId, cancellationToken).ConfigureAwait(false);
                }

                await action(transaction, context).ConfigureAwait(false);
            },
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes an action within a cross-shard transaction scope and returns a result.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="shardIds">The shard IDs to enlist in the transaction.</param>
    /// <param name="action">The action to execute within the transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the action.</returns>
    public static async Task<TResult> ExecuteInCrossShardTransactionAsync<TContext, TResult>(
        this TContext context,
        IEnumerable<string> shardIds,
        Func<ICrossShardTransaction, TContext, Task<TResult>> action,
        CancellationToken cancellationToken = default) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shardIds);
        ArgumentNullException.ThrowIfNull(action);

        var coordinator = GetTransactionCoordinator(context);

        return await coordinator.ExecuteInTransactionAsync(
            async transaction =>
            {
                foreach (var shardId in shardIds)
                {
                    await transaction.EnlistAsync(shardId, cancellationToken).ConfigureAwait(false);
                }

                return await action(transaction, context).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a new cross-shard transaction.
    /// </summary>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new transaction.</returns>
    /// <example>
    /// <code>
    /// await using var transaction = await context.BeginCrossShardTransactionAsync();
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
    public static Task<ICrossShardTransaction> BeginCrossShardTransactionAsync(
        this DbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var coordinator = GetTransactionCoordinator(context);
        return coordinator.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Begins a new cross-shard transaction with the specified options.
    /// </summary>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="options">The transaction options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new transaction.</returns>
    public static Task<ICrossShardTransaction> BeginCrossShardTransactionAsync(
        this DbContext context,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var coordinator = GetTransactionCoordinator(context);
        return coordinator.BeginTransactionAsync(options, cancellationToken);
    }

    /// <summary>
    /// Gets the current ambient cross-shard transaction, if any.
    /// </summary>
    /// <param name="context">The DbContext instance.</param>
    /// <returns>The current transaction, or null if none is active.</returns>
    public static ICrossShardTransaction? GetCurrentCrossShardTransaction(this DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var coordinator = GetTransactionCoordinator(context);
            return coordinator.CurrentTransaction;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets whether there is an active ambient cross-shard transaction.
    /// </summary>
    /// <param name="context">The DbContext instance.</param>
    /// <returns>True if there is an active transaction.</returns>
    public static bool HasActiveCrossShardTransaction(this DbContext context)
    {
        return GetCurrentCrossShardTransaction(context) is not null;
    }

    private static ICrossShardTransactionCoordinator GetTransactionCoordinator(DbContext context)
    {
        var serviceProvider = ((IInfrastructure<IServiceProvider>)context).Instance;
        var coordinator = serviceProvider.GetService<ICrossShardTransactionCoordinator>();

        return coordinator
            ?? throw new InvalidOperationException(
                "Cross-shard transaction coordinator is not registered. " +
                "Call AddCrossShardTransactionSupport() in your service configuration.");
    }
}
