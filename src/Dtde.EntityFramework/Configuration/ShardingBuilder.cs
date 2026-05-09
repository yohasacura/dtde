using System.Collections.Generic;

using Dtde.Abstractions.Metadata;

using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Fluent builder for chaining additional sharding options after a primary
/// <c>ShardBy*</c> call on an <see cref="EntityTypeBuilder{TEntity}"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type being configured.</typeparam>
public sealed class ShardingBuilder<TEntity>
    where TEntity : class
{
    private readonly EntityTypeBuilder<TEntity> _builder;

    internal ShardingBuilder(EntityTypeBuilder<TEntity> builder)
    {
        _builder = builder;
    }

    /// <summary>
    /// Returns the underlying <see cref="EntityTypeBuilder{TEntity}"/> for further EF Core configuration.
    /// </summary>
    public EntityTypeBuilder<TEntity> Builder => _builder;

    /// <summary>
    /// Sets the storage mode for shards (per-table partitioning, per-database sharding, or manual).
    /// </summary>
    /// <param name="mode">The storage mode.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardingBuilder<TEntity> WithStorageMode(ShardStorageMode mode)
    {
        _builder.Metadata.SetAnnotation(DtdeAnnotationNames.StorageMode, mode);
        return this;
    }

    /// <summary>
    /// Sets the table name pattern for table-based sharding.
    /// Tokens such as <c>{TableName}</c> and <c>{ShardKey}</c> are replaced at runtime.
    /// </summary>
    /// <param name="pattern">The table name pattern.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardingBuilder<TEntity> WithTablePattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        _builder.Metadata.SetAnnotation(DtdeAnnotationNames.TableNamePattern, pattern);
        return this;
    }

    /// <summary>
    /// Disables EF Core migrations for this sharded entity. Use this when shard tables are
    /// managed externally (for example, by a SQL project or a DBA-owned process).
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public ShardingBuilder<TEntity> WithoutMigrations()
    {
        _builder.Metadata.SetAnnotation(DtdeAnnotationNames.MigrationsEnabled, false);
        return this;
    }

    /// <summary>
    /// Returns the underlying <see cref="EntityTypeBuilder{TEntity}"/>.
    /// </summary>
    public EntityTypeBuilder<TEntity> ToEntityTypeBuilder() => _builder;

    /// <summary>
    /// Implicit conversion to <see cref="EntityTypeBuilder{TEntity}"/> so a <c>ShardBy*</c> call
    /// can be the final fluent step in <c>OnModelCreating</c>.
    /// </summary>
    /// <param name="shardingBuilder">The sharding builder to convert.</param>
    public static implicit operator EntityTypeBuilder<TEntity>(ShardingBuilder<TEntity> shardingBuilder)
    {
        ArgumentNullException.ThrowIfNull(shardingBuilder);
        return shardingBuilder._builder;
    }
}
