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
/// Extension methods for IServiceCollection to register DTDE services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds DTDE services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The action to configure DTDE options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDtde(
        this IServiceCollection services,
        Action<DtdeOptionsBuilder> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var builder = new DtdeOptionsBuilder();
        configureOptions(builder);
        var options = builder.Build();

        return services.AddDtde(options);
    }

    /// <summary>
    /// Adds DTDE services to the service collection with pre-built options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The DTDE options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDtde(
        this IServiceCollection services,
        DtdeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Register options
        services.AddSingleton(options);

        // Register core services
        services.AddSingleton<IMetadataRegistry>(options.MetadataRegistry);
        services.AddSingleton<IShardRegistry>(options.ShardRegistry);
        services.AddSingleton<ITemporalContext>(options.TemporalContext);

        // Register query services
        services.AddScoped<IExpressionRewriter, DtdeExpressionRewriter>();
        services.AddScoped<IShardedQueryExecutor, ShardedQueryExecutor>();

        // Register update services
        services.AddScoped<IDtdeUpdateProcessor, DtdeUpdateProcessor>();
        services.AddScoped<VersionManager>();
        services.AddScoped<ShardWriteRouter>();

        return services;
    }

    /// <summary>
    /// Adds DTDE services with a typed shard context factory.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The action to configure DTDE options.</param>
    /// <param name="contextFactory">The factory function to create context instances.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDtde<TContext>(
        this IServiceCollection services,
        Action<DtdeOptionsBuilder> configureOptions,
        Func<DbContextOptions<TContext>, TContext> contextFactory) where TContext : DbContext
    {
        services.AddDtde(configureOptions);
        services.AddScoped<IShardContextFactory>(sp =>
            new ShardContextFactory<TContext>(
                sp.GetRequiredService<IShardRegistry>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ShardContextFactory<TContext>>>(),
                contextFactory));

        return services;
    }

    /// <summary>
    /// Adds DTDE DbContext to the service collection with transparent sharding enabled by default.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDbContext">The action to configure the DbContext.</param>
    /// <param name="configureDtde">The action to configure DTDE.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method enables fully transparent sharding. Use regular EF Core patterns
    /// and sharding is handled automatically:
    /// </para>
    /// <code>
    /// // All these patterns work transparently with sharding:
    ///
    /// // Simple SaveChanges
    /// context.Add(entity1);
    /// context.Add(entity2);
    /// await context.SaveChangesAsync(); // Auto cross-shard if needed
    ///
    /// // Explicit transactions
    /// using var transaction = await context.Database.BeginTransactionAsync();
    /// context.Add(entity1);
    /// await context.SaveChangesAsync();
    /// context.Update(entity2);
    /// await context.SaveChangesAsync();
    /// await transaction.CommitAsync(); // 2PC across all touched shards
    /// </code>
    /// </remarks>
    public static IServiceCollection AddDtdeDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext,
        Action<DtdeOptionsBuilder> configureDtde) where TContext : DtdeDbContext
    {
        return services.AddDtdeDbContext<TContext>(
            configureDbContext,
            configureDtde,
            enableTransparentSharding: true);
    }

    /// <summary>
    /// Adds DTDE DbContext to the service collection with configurable transparent sharding.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDbContext">The action to configure the DbContext.</param>
    /// <param name="configureDtde">The action to configure DTDE.</param>
    /// <param name="enableTransparentSharding">
    /// When true (default), sharding is completely transparent. Standard EF Core patterns
    /// work automatically with cross-shard transaction support.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDtdeDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext,
        Action<DtdeOptionsBuilder> configureDtde,
        bool enableTransparentSharding) where TContext : DtdeDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDbContext);
        ArgumentNullException.ThrowIfNull(configureDtde);

        // Add DTDE services
        services.AddDtde(configureDtde);

        // Add shard context factory for cross-shard operations
        services.AddScoped<IShardContextFactory, NullShardContextFactory<TContext>>();

        // Add cross-shard transaction support
        if (enableTransparentSharding)
        {
            services.AddTransparentShardingSupport();
        }

        // Add DbContext
        services.AddDbContext<TContext>((sp, options) =>
        {
            configureDbContext(options);

            var dtdeOptions = sp.GetRequiredService<DtdeOptions>();
            options.UseDtde(dtdeOptions);

            // Add the transparent sharding interceptor
            if (enableTransparentSharding)
            {
                options.UseTransparentSharding(sp);
            }
        });

        return services;
    }

    /// <summary>
    /// Adds transparent sharding support to the service collection.
    /// This enables automatic cross-shard transaction coordination.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTransparentShardingSupport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the cross-shard coordinator
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

        // Register the transparent sharding interceptor
        services.TryAddSingleton<TransparentShardingInterceptor>();

        return services;
    }

    /// <summary>
    /// Adds transparent sharding to DbContext options.
    /// </summary>
    /// <param name="optionsBuilder">The DbContext options builder.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>The options builder for chaining.</returns>
    public static DbContextOptionsBuilder UseTransparentSharding(
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

    #region Legacy/Deprecated Methods

    /// <summary>
    /// Adds cross-shard transaction support to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method is provided for backward compatibility. For new code,
    /// use <see cref="AddTransparentShardingSupport"/> instead.
    /// </remarks>
    [Obsolete("Use AddTransparentShardingSupport() instead. This method is provided for backward compatibility.")]
    public static IServiceCollection AddCrossShardTransactionSupport(this IServiceCollection services)
    {
        return services.AddTransparentShardingSupport();
    }

    /// <summary>
    /// Adds cross-shard transaction support with custom options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDefaultOptions">Action to configure default transaction options.</param>
    /// <returns>The service collection for chaining.</returns>
    [Obsolete("Use AddTransparentShardingSupport() instead. This method is provided for backward compatibility.")]
    public static IServiceCollection AddCrossShardTransactionSupport(
        this IServiceCollection services,
        Action<CrossShardTransactionOptions> configureDefaultOptions)
    {
        ArgumentNullException.ThrowIfNull(configureDefaultOptions);

        var options = new CrossShardTransactionOptions();
        configureDefaultOptions(options);

        CrossShardTransactionOptions.DefaultTimeout = options.Timeout;
        CrossShardTransactionOptions.DefaultIsolationLevel = options.IsolationLevel;

        return services.AddTransparentShardingSupport();
    }

    /// <summary>
    /// Adds automatic cross-shard transaction handling for SaveChanges operations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    [Obsolete("Use AddTransparentShardingSupport() instead. This method is provided for backward compatibility.")]
    public static IServiceCollection AddAutoCrossShardSaveChanges(this IServiceCollection services)
    {
        services.TryAddSingleton<ShardAwareSaveChangesInterceptor>();
        return services;
    }

    /// <summary>
    /// Adds automatic cross-shard transaction handling to DbContext options.
    /// </summary>
    /// <param name="optionsBuilder">The DbContext options builder.</param>
    /// <param name="serviceProvider">The service provider to resolve the interceptor from.</param>
    /// <returns>The options builder for chaining.</returns>
    [Obsolete("Use UseTransparentSharding() instead. This method is provided for backward compatibility.")]
    public static DbContextOptionsBuilder UseAutoCrossShardSaveChanges(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider)
    {
        return optionsBuilder.UseTransparentSharding(serviceProvider);
    }

    #endregion
}
