using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Temporal;
using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Query;
using Dtde.EntityFramework.Update;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    /// Adds DTDE DbContext to the service collection.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureDbContext">The action to configure the DbContext.</param>
    /// <param name="configureDtde">The action to configure DTDE.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDtdeDbContext<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDbContext,
        Action<DtdeOptionsBuilder> configureDtde) where TContext : DtdeDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDbContext);
        ArgumentNullException.ThrowIfNull(configureDtde);

        // Add DTDE services
        services.AddDtde(configureDtde);

        // Add null shard context factory for single-database scenarios
        services.AddScoped<IShardContextFactory, NullShardContextFactory<TContext>>();

        // Add DbContext
        services.AddDbContext<TContext>((sp, options) =>
        {
            configureDbContext(options);

            var dtdeOptions = sp.GetRequiredService<DtdeOptions>();
            options.UseDtde(dtdeOptions);
        });

        return services;
    }
}
