using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// EF Core <see cref="DbContextOptionsBuilder"/> extension for wiring DTDE
/// directly into an <c>AddDbContext</c> call.
/// </summary>
/// <remarks>
/// Most applications should use <c>AddDtdeDbContext&lt;TContext&gt;</c> from
/// <see cref="ServiceCollectionExtensions"/> — the one-call canonical entry
/// that wires the per-shard context factory and transparent cross-shard
/// transactions automatically.
/// <see cref="UseDtde{TContext}(DbContextOptionsBuilder{TContext}, System.Action{DtdeOptionsBuilder})"/>
/// is provided for setups that compose <c>AddDbContext</c> manually; note
/// that this path does NOT install the per-shard context factory, so reads
/// and writes will all go through the parent context's connection — useful
/// for non-sharded queries against a DTDE-aware DbContext or for testing.
/// </remarks>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures the typed <see cref="DbContextOptionsBuilder{TContext}"/> to use DTDE.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="configureOptions">DTDE configuration callback.</param>
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
    /// Configures the <see cref="DbContextOptionsBuilder"/> to use DTDE.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="configureOptions">DTDE configuration callback.</param>
    /// <returns>The options builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;(options =>
    ///     options.UseSqlServer(connectionString)
    ///            .UseDtde(dtde => dtde
    ///                .AddShard("EU", euConnectionString)
    ///                .AddShard("US", usConnectionString)));
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

        return optionsBuilder.UseDtdeOptions(builder.Build());
    }

    /// <summary>
    /// Internal hook used by <c>AddDtdeDbContext</c> to wire pre-built
    /// <see cref="DtdeOptions"/> into the EF Core options pipeline.
    /// </summary>
    internal static DbContextOptionsBuilder UseDtdeOptions(
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
