using Dtde.EntityFramework.Update;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.Samples.BulkOperations;

/// <summary>
/// Demo bulk-insert provider that wraps the standard
/// <c>AddRangeAsync</c> + <c>SaveChangesAsync</c> path with logging. In a
/// real application this is where you'd call <c>SqlBulkCopy</c>,
/// PostgreSQL <c>COPY FROM STDIN</c>, Oracle direct-path, or another
/// provider-specific bulk loader.
/// </summary>
public sealed class LoggingBulkInsertProvider : IBulkInsertProvider
{
    private readonly ILogger<LoggingBulkInsertProvider> _logger;

    public LoggingBulkInsertProvider(ILogger<LoggingBulkInsertProvider> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(DbContext context) => true;

    public async Task<int> BulkInsertAsync<TEntity>(
        DbContext context,
        IReadOnlyCollection<TEntity> entities,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        _logger.LogInformation(
            "Bulk-inserting {Count} {Entity} into provider '{Provider}' (database '{Database}')",
            entities.Count,
            typeof(TEntity).Name,
            context.Database.ProviderName,
            context.Database.GetDbConnection().Database);

        await context.Set<TEntity>().AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entities.Count;
    }
}
