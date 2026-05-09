using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Temporal;
using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;
using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Infrastructure;
using Dtde.EntityFramework.Query;
using Dtde.EntityFramework.Update;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// DI entry point for DTDE: <see cref="AddDtdeDbContext{TContext}(IServiceCollection, Action{DbContextOptionsBuilder, string}, Action{DtdeOptionsBuilder})"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a DTDE-aware <see cref="DbContext"/> with the DI container,
    /// wires up cross-shard transparency (automatic 2PC across shards in
    /// <c>SaveChangesAsync</c>), and installs a per-shard context factory
    /// that creates real per-shard <see cref="DbContext"/> instances on demand
    /// — with shard-specific table names (table-mode) or shard-specific
    /// connection strings (database-mode), depending on how each shard was
    /// declared.
    /// </summary>
    /// <typeparam name="TContext">The application's <see cref="DtdeDbContext"/> subclass. Must declare a public constructor taking a single <see cref="DbContextOptions{TContext}"/>.</typeparam>
    /// <param name="services">The DI container.</param>
    /// <param name="configureProvider">
    /// Configures the underlying EF Core provider for both the parent context
    /// and each per-shard context. The framework invokes this with each
    /// shard's connection string (or <see langword="null"/> for the parent
    /// context and for table-mode shards that inherit the default connection).
    /// Typical body: <c>(db, conn) =&gt; db.UseSqlite(conn ?? "Data Source=app.db")</c>.
    /// </param>
    /// <param name="configureDtde">Configures DTDE: shards, defaults, diagnostics.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// // Table-mode (single SQLite file, per-shard tables Customers_EU, Customers_US, ...):
    /// builder.Services.AddDtdeDbContext&lt;AppDbContext&gt;(
    ///     (db, conn) =&gt; db.UseSqlite(conn ?? "Data Source=app.db"),
    ///     dtde =&gt; dtde.AddShards("EU", "US", "APAC"));
    ///
    /// // Database-mode (one DB per shard):
    /// builder.Services.AddDtdeDbContext&lt;AppDbContext&gt;(
    ///     (db, conn) =&gt; db.UseSqlite(conn ?? "Data Source=base.db"),
    ///     dtde =&gt; dtde
    ///         .AddShard("EU", "Data Source=eu.db")
    ///         .AddShard("US", "Data Source=us.db"));
    /// </code>
    /// </example>
    public static IServiceCollection AddDtdeDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder, string?> configureProvider,
        Action<DtdeOptionsBuilder> configureDtde)
        where TContext : DtdeDbContext
        => services.AddDtdeDbContext<TContext>(configureProvider, configureDtde, enableTransparentSharding: true);

    /// <summary>
    /// As <see cref="AddDtdeDbContext{TContext}(IServiceCollection, Action{DbContextOptionsBuilder, string}, Action{DtdeOptionsBuilder})"/>
    /// but lets you opt out of automatic cross-shard transaction interception.
    /// </summary>
    public static IServiceCollection AddDtdeDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder, string?> configureProvider,
        Action<DtdeOptionsBuilder> configureDtde,
        bool enableTransparentSharding)
        where TContext : DtdeDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);
        ArgumentNullException.ThrowIfNull(configureDtde);

        services.AddDtdeCore(configureDtde);

        // Per-shard factory replaces the no-op default. It calls back into the
        // user's configureProvider with the shard's connection string, so each
        // per-shard DbContext has the right provider and the right connection.
        services.AddScoped<IShardContextFactory>(sp =>
        {
            var shardRegistry = sp.GetRequiredService<IShardRegistry>();
            var dtdeOptions = sp.GetRequiredService<DtdeOptions>();
            var logger = sp.GetRequiredService<ILogger<PerShardContextFactory<TContext>>>();
            return new PerShardContextFactory<TContext>(
                shardRegistry,
                configureProvider,
                dtdeOptions,
                logger);
        });

        if (enableTransparentSharding)
        {
            services.AddTransparentShardingSupport();
        }

        services.AddDbContext<TContext>((sp, options) =>
        {
            // Parent context: framework calls configureProvider with null
            // (no specific shard connection); user typically falls back to a
            // configured default ("Data Source=app.db" or similar).
            configureProvider(options, null);

            var dtdeOptions = sp.GetRequiredService<DtdeOptions>();
            options.UseDtdeOptions(dtdeOptions);

            if (enableTransparentSharding)
            {
                options.UseTransparentSharding(sp);
            }
        });

        return services;
    }

    // ------------------------------------------------------------------
    //  Internal infrastructure
    // ------------------------------------------------------------------

    internal static IServiceCollection AddDtdeCore(
        this IServiceCollection services,
        Action<DtdeOptionsBuilder> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var builder = new DtdeOptionsBuilder();
        configureOptions(builder);
        var options = builder.Build();

        services.AddSingleton(options);
        services.AddSingleton<IMetadataRegistry>(options.MetadataRegistry);
        services.AddSingleton<IShardRegistry>(options.ShardRegistry);
        services.AddSingleton<IShardGroupRegistry>(options.ShardGroupRegistry);
        services.AddSingleton<ITemporalContext>(options.TemporalContext);

        services.AddScoped<IExpressionRewriter, DtdeExpressionRewriter>();
        services.AddScoped<IShardedQueryExecutor, ShardedQueryExecutor>();

        services.AddScoped<IDtdeUpdateProcessor, DtdeUpdateProcessor>();
        services.AddScoped<VersionManager>();
        services.AddScoped<ShardWriteRouter>();

        return services;
    }

    internal static IServiceCollection AddTransparentShardingSupport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICrossShardTransactionCoordinator>(sp =>
        {
            var shardRegistry = sp.GetRequiredService<IShardRegistry>();
            var contextFactory = sp.GetRequiredService<IShardContextFactory>();
            var logger = sp.GetRequiredService<ILogger<CrossShardTransactionCoordinator>>();
            var transactionLogger = sp.GetRequiredService<ILogger<CrossShardTransaction>>();

            return new CrossShardTransactionCoordinator(
                shardRegistry,
                async (shardId, ct) => await contextFactory.CreateContextAsync(shardId, ct).ConfigureAwait(false),
                logger,
                transactionLogger);
        });

        services.TryAddSingleton<TransparentShardingInterceptor>();

        return services;
    }

    internal static DbContextOptionsBuilder UseTransparentSharding(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var interceptor = serviceProvider.GetService<TransparentShardingInterceptor>();
        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        return optionsBuilder;
    }
}
