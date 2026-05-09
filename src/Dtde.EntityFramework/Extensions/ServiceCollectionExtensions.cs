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
/// Single canonical DI entry point for DTDE:
/// <see cref="AddDtdeDbContext{TContext}(IServiceCollection, Action{DbContextOptionsBuilder}, Action{DtdeOptionsBuilder})"/>.
/// </summary>
/// <remarks>
/// Application code should use that one-call helper for nearly all setups. The lower-level helpers (<c>AddDtde</c>,
/// <c>UseTransparentSharding</c>, etc.) are internal implementation details.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a DTDE-aware <see cref="DbContext"/> with the DI container.
    /// Cross-shard transparency (automatic 2PC across shards in
    /// <c>SaveChangesAsync</c>) is enabled by default.
    /// </summary>
    /// <typeparam name="TContext">The application's <see cref="DtdeDbContext"/> subclass.</typeparam>
    /// <param name="services">The DI container.</param>
    /// <param name="configureDbContext">Configures the underlying EF Core options (e.g. <c>UseSqlite</c>, <c>UseSqlServer</c>).</param>
    /// <param name="configureDtde">Configures DTDE: shards, defaults, diagnostics.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddDtdeDbContext&lt;AppDbContext&gt;(
    ///     db => db.UseSqlite("Data Source=app.db"),
    ///     dtde => dtde.AddShards("EU", "US", "APAC"));
    /// </code>
    /// </example>
    public static IServiceCollection AddDtdeDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext,
        Action<DtdeOptionsBuilder> configureDtde)
        where TContext : DtdeDbContext
        => services.AddDtdeDbContext<TContext>(
            configureDbContext,
            configureDtde,
            enableTransparentSharding: true);

    /// <summary>
    /// Registers a DTDE-aware <see cref="DbContext"/> with the option to
    /// disable transparent cross-shard transaction handling. Use the simpler
    /// overload unless you specifically need to opt out of 2PC interception.
    /// </summary>
    /// <typeparam name="TContext">The application's <see cref="DtdeDbContext"/> subclass.</typeparam>
    /// <param name="services">The DI container.</param>
    /// <param name="configureDbContext">Configures the underlying EF Core options.</param>
    /// <param name="configureDtde">Configures DTDE.</param>
    /// <param name="enableTransparentSharding">
    /// When <see langword="true"/> (recommended), <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
    /// transparently fans out and coordinates writes that span multiple shards.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddDtdeDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext,
        Action<DtdeOptionsBuilder> configureDtde,
        bool enableTransparentSharding)
        where TContext : DtdeDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDbContext);
        ArgumentNullException.ThrowIfNull(configureDtde);

        services.AddDtdeCore(configureDtde);
        services.AddScoped<IShardContextFactory, NullShardContextFactory<TContext>>();

        if (enableTransparentSharding)
        {
            services.AddTransparentShardingSupport();
        }

        services.AddDbContext<TContext>((sp, options) =>
        {
            configureDbContext(options);

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
    //  Internal infrastructure (composable via UseDtde extension)
    // ------------------------------------------------------------------

    /// <summary>
    /// Registers DTDE's core services (registries, query/update services). Public so the
    /// <c>UseDtde(Action&lt;DtdeOptionsBuilder&gt;)</c> extension can call into it; not
    /// intended for application code.
    /// </summary>
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
