using Microsoft.EntityFrameworkCore;

namespace Dtde.EntityFramework.Update;

/// <summary>
/// Default <see cref="IBulkInsertProvider"/>: change-tracker-driven
/// <c>AddRangeAsync</c> + <c>SaveChangesAsync</c>. Always handles, so it
/// acts as the fallback when no provider-specific bulk path is registered
/// (or none of them claim the context).
/// </summary>
public sealed class DefaultBulkInsertProvider : IBulkInsertProvider
{
    /// <inheritdoc />
    public bool CanHandle(DbContext context) => true;

    /// <inheritdoc />
    public async Task<int> BulkInsertAsync<TEntity>(
        DbContext context,
        IReadOnlyCollection<TEntity> entities,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        if (entities.Count == 0)
        {
            return 0;
        }

        var set = context.Set<TEntity>();
        await set.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entities.Count;
    }
}
