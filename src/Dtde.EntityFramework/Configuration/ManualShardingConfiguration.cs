using System.Collections.Generic;
using System.Linq.Expressions;

namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Configuration for manual table-based sharding, where the underlying tables are
/// pre-created and managed outside of EF Core (for example, by a SQL project).
/// </summary>
/// <typeparam name="TEntity">The entity type being configured.</typeparam>
public sealed class ManualShardingConfiguration<TEntity>
    where TEntity : class
{
    private readonly List<ManualTableMapping<TEntity>> _tables = [];

    /// <summary>
    /// Gets or sets a value indicating whether EF Core migrations are enabled for this entity.
    /// Defaults to <see langword="false"/> because manual shards are typically owned externally.
    /// </summary>
    public bool MigrationsEnabled { get; set; }

    /// <summary>
    /// Gets the configured table mappings.
    /// </summary>
    public IReadOnlyList<ManualTableMapping<TEntity>> Tables => _tables;

    /// <summary>
    /// Adds a table mapping with a routing predicate.
    /// </summary>
    /// <param name="tableName">The fully qualified table name (for example, <c>dbo.Orders_2024</c>).</param>
    /// <param name="predicate">Predicate that decides whether an entity belongs to this table.</param>
    public void AddTable(string tableName, Expression<Func<TEntity, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(predicate);

        _tables.Add(new ManualTableMapping<TEntity>(tableName, predicate));
    }
}

/// <summary>
/// Represents a single manual table mapping: a target table name and the predicate that
/// determines which entities are routed there.
/// </summary>
/// <typeparam name="TEntity">The entity type being configured.</typeparam>
/// <param name="TableName">The fully qualified table name.</param>
/// <param name="Predicate">The routing predicate.</param>
public sealed record ManualTableMapping<TEntity>(string TableName, Expression<Func<TEntity, bool>> Predicate)
    where TEntity : class;
