using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// Extension methods for DbContextOptionsBuilder to configure DTDE.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures the context to use DTDE with the specified options (generic version).
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="configureOptions">The action to configure DTDE options.</param>
    /// <returns>The typed options builder for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseDtde<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        Action<DtdeOptionsBuilder> configureOptions)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UseDtde(configureOptions);
        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to use DTDE with default options (generic version).
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <returns>The typed options builder for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseDtde<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        return optionsBuilder.UseDtde(_ => { });
    }

    /// <summary>
    /// Configures the context to use DTDE with the specified options.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="configureOptions">The action to configure DTDE options.</param>
    /// <returns>The options builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;(options =>
    ///     options.UseSqlServer(connectionString)
    ///            .UseDtde(dtde => dtde
    ///                .AddShard("Shard2024", s => s
    ///                    .WithTier(ShardTier.Hot)
    ///                    .WithConnectionString(hotConnection))
    ///                .SetDefaultTemporalContext(DateTime.UtcNow)));
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseDtde(
        this DbContextOptionsBuilder optionsBuilder,
        Action<DtdeOptionsBuilder> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var builder = new DtdeOptionsBuilder();
        configureOptions(builder);

        var extension = optionsBuilder.Options.FindExtension<DtdeOptionsExtension>()
            ?? new DtdeOptionsExtension();

        extension = extension.WithOptions(builder.Build());

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to use DTDE with default options.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <returns>The options builder for chaining.</returns>
    public static DbContextOptionsBuilder UseDtde(this DbContextOptionsBuilder optionsBuilder)
    {
        return optionsBuilder.UseDtde(_ => { });
    }

    /// <summary>
    /// Configures the context to use DTDE with pre-built options.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="options">The pre-built DTDE options.</param>
    /// <returns>The options builder for chaining.</returns>
    public static DbContextOptionsBuilder UseDtde(
        this DbContextOptionsBuilder optionsBuilder,
        DtdeOptions options)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(options);

        var extension = optionsBuilder.Options.FindExtension<DtdeOptionsExtension>()
            ?? new DtdeOptionsExtension();

        extension = extension.WithOptions(options);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return optionsBuilder;
    }
}
