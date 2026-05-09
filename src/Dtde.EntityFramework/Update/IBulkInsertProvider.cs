using Microsoft.EntityFrameworkCore;

namespace Dtde.EntityFramework.Update;

/// <summary>
/// Pluggable per-provider bulk-insert path. The default DTDE shipping path
/// uses change-tracker-driven <see cref="DbContext.AddRange(object[])"/> +
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> — correct for
/// every provider, slow for very large batches. Plug in a
/// provider-specific implementation (SQL Server <c>SqlBulkCopy</c>,
/// PostgreSQL <c>COPY</c>, Oracle direct-path, etc.) to skip the
/// change-tracker overhead.
/// </summary>
/// <remarks>
/// <para>
/// Multiple providers can be registered at the same time; DTDE picks the
/// first one whose <see cref="CanHandle"/> returns <c>true</c> for the
/// per-shard <see cref="DbContext"/>. The built-in
/// <c>DefaultBulkInsertProvider</c> is registered last with a
/// <c>CanHandle</c> that always returns <c>true</c> so there's always a
/// fallback.
/// </para>
/// <para>
/// Implementers should respect the cancellation token, transparently
/// participate in any ambient transaction on the supplied context (the
/// context's open <c>IDbContextTransaction</c>, if any, is the bulk path's
/// transaction), and return the count of inserted entities.
/// </para>
/// </remarks>
public interface IBulkInsertProvider
{
    /// <summary>
    /// Whether this provider can drive a bulk insert against the given
    /// per-shard <see cref="DbContext"/>. Typically inspects
    /// <c>context.Database.ProviderName</c> (e.g. matches
    /// <c>"Microsoft.EntityFrameworkCore.SqlServer"</c>).
    /// </summary>
    /// <param name="context">The per-shard DbContext that the bulk insert
    /// will run against.</param>
    /// <returns>True if this provider should handle the call.</returns>
    public bool CanHandle(DbContext context);

    /// <summary>
    /// Inserts <paramref name="entities"/> into the supplied context's
    /// <see cref="DbSet{TEntity}"/>. The context's database connection and
    /// any open transaction are used unchanged.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="context">The per-shard DbContext.</param>
    /// <param name="entities">The entities to insert.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of entities inserted.</returns>
    public Task<int> BulkInsertAsync<TEntity>(
        DbContext context,
        IReadOnlyCollection<TEntity> entities,
        CancellationToken cancellationToken = default) where TEntity : class;
}
