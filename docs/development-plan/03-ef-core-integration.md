# DTDE Development Plan - EF Core Integration

[← Back to Core Domain Model](02-core-domain-model.md) | [Next: Query Engine →](04-query-engine.md)

---

## 1. Integration Overview

DTDE integrates with EF Core by replacing key services in the query and update pipelines. This allows developers to write standard LINQ queries while DTDE transparently handles temporal filtering, shard resolution, and distributed execution.

### 1.1 Integration Points

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           EF Core Pipeline                                   │
│                                                                             │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐  │
│  │   LINQ      │ -> │   Query     │ -> │    SQL      │ -> │   Query     │  │
│  │  Provider   │    │  Compiler   │    │  Generator  │    │  Executor   │  │
│  └──────┬──────┘    └──────┬──────┘    └──────┬──────┘    └──────┬──────┘  │
│         │                  │                  │                  │          │
│         ▼                  ▼                  ▼                  ▼          │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    DTDE Service Replacements                         │   │
│  │  ┌─────────────┐    ┌─────────────┐              ┌─────────────┐     │   │
│  │  │  Expression │    │   Query     │              │   Custom    │     │   │
│  │  │  Rewriter   │    │   Planner   │              │  Executor   │     │   │
│  │  └─────────────┘    └─────────────┘              └─────────────┘     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     SaveChanges Pipeline                             │   │
│  │  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐              │   │
│  │  │  Change     │ -> │ Interceptor │ -> │   Version   │              │   │
│  │  │  Tracker    │    │  (DTDE)     │    │   Manager   │              │   │
│  │  └─────────────┘    └─────────────┘    └─────────────┘              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. DbContext Options Extension

### 2.1 Extension Method

```csharp
namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// Extension methods for configuring DTDE on DbContextOptionsBuilder.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures the DbContext to use DTDE for temporal and sharded data management.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="configureOptions">Action to configure DTDE options.</param>
    /// <returns>The options builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;(options =>
    /// {
    ///     options.UseSqlServer(connectionString);
    ///     options.UseDtde(dtde =>
    ///     {
    ///         dtde.AddShardFromConfig("shards.json");
    ///         dtde.SetDefaultTemporalContext(() => DateTime.UtcNow);
    ///     });
    /// });
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseDtde(
        this DbContextOptionsBuilder optionsBuilder,
        Action<DtdeOptionsBuilder> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(configureOptions);
        
        var dtdeOptionsBuilder = new DtdeOptionsBuilder();
        configureOptions(dtdeOptionsBuilder);
        
        var extension = optionsBuilder.Options.FindExtension<DtdeOptionsExtension>()
            ?? new DtdeOptionsExtension();
        
        extension = extension.WithOptions(dtdeOptionsBuilder.Build());
        
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(extension);
        
        return optionsBuilder;
    }
}
```

### 2.2 Options Builder

```csharp
namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Builder for configuring DTDE options.
/// </summary>
public sealed class DtdeOptionsBuilder
{
    private readonly List<ShardMetadata> _shards = new();
    private Func<DateTime>? _defaultTemporalContextProvider;
    private int _maxParallelShards = 10;
    private bool _enableDiagnostics = false;
    private bool _enableTestMode = false;
    
    /// <summary>
    /// Adds shards from a JSON configuration file.
    /// </summary>
    /// <param name="configPath">Path to the shard configuration file.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder AddShardsFromConfig(string configPath)
    {
        var config = ShardConfigurationLoader.Load(configPath);
        _shards.AddRange(config.Shards);
        return this;
    }
    
    /// <summary>
    /// Adds a single shard configuration.
    /// </summary>
    /// <param name="configure">Action to configure the shard.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder AddShard(Action<ShardMetadataBuilder> configure)
    {
        var builder = new ShardMetadataBuilder();
        configure(builder);
        _shards.Add(builder.Build());
        return this;
    }
    
    /// <summary>
    /// Sets the default temporal context provider.
    /// </summary>
    /// <param name="provider">Function returning the default temporal point.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetDefaultTemporalContext(Func<DateTime> provider)
    {
        _defaultTemporalContextProvider = provider;
        return this;
    }
    
    /// <summary>
    /// Sets the maximum number of shards to query in parallel.
    /// </summary>
    /// <param name="maxParallel">Maximum parallel shard queries.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetMaxParallelShards(int maxParallel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallel, 1);
        _maxParallelShards = maxParallel;
        return this;
    }
    
    /// <summary>
    /// Enables diagnostic logging and events.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder EnableDiagnostics()
    {
        _enableDiagnostics = true;
        return this;
    }
    
    /// <summary>
    /// Enables test mode (single shard, no distribution).
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder EnableTestMode()
    {
        _enableTestMode = true;
        return this;
    }
    
    internal DtdeOptions Build()
    {
        return new DtdeOptions
        {
            Shards = _shards.ToList(),
            DefaultTemporalContextProvider = _defaultTemporalContextProvider,
            MaxParallelShards = _maxParallelShards,
            EnableDiagnostics = _enableDiagnostics,
            EnableTestMode = _enableTestMode
        };
    }
}
```

### 2.3 Options Extension

```csharp
namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// EF Core options extension for DTDE configuration.
/// </summary>
public sealed class DtdeOptionsExtension : IDbContextOptionsExtension
{
    private DtdeOptions _options = new();
    private ExtensionInfo? _info;
    
    public DtdeOptions Options => _options;
    
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);
    
    public DtdeOptionsExtension WithOptions(DtdeOptions options)
    {
        var clone = Clone();
        clone._options = options;
        return clone;
    }
    
    public void ApplyServices(IServiceCollection services)
    {
        // Register DTDE services
        services.AddSingleton(_options);
        services.AddSingleton<IMetadataRegistry, MetadataRegistry>();
        services.AddSingleton<IShardRegistry, ShardRegistry>();
        services.AddScoped<ITemporalContext, TemporalContext>();
        services.AddScoped<IDtdeQueryExecutor, DtdeQueryExecutor>();
        services.AddScoped<IDtdeUpdateProcessor, DtdeUpdateProcessor>();
        
        // Replace EF Core services
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IQueryTranslationPostprocessorFactory, DtdeQueryTranslationPostprocessorFactory>();
    }
    
    public void Validate(IDbContextOptions options)
    {
        if (_options.Shards.Count == 0 && !_options.EnableTestMode)
        {
            throw new InvalidOperationException(
                "At least one shard must be configured unless test mode is enabled.");
        }
    }
    
    private DtdeOptionsExtension Clone() => new()
    {
        _options = _options
    };
    
    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(DtdeOptionsExtension extension) : base(extension) { }
        
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "using DTDE";
        
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) 
            => other is ExtensionInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) 
            => debugInfo["DTDE:Enabled"] = "true";
    }
}
```

---

## 3. Fluent API Extensions

### 3.1 Entity Type Builder Extensions

DTDE configures entities through extension methods on `EntityTypeBuilder<TEntity>`.
The shipped surface uses three verb prefixes consistently:

| Prefix | Purpose | Methods |
|---|---|---|
| `ShardBy*` | Primary sharding configuration. Returns `ShardingBuilder<TEntity>` for chained options. | `ShardBy`, `ShardByDate`, `ShardByHash` |
| `Has*` | EF Core-style property configuration. | `HasTemporalValidity`, `HasTemporalContainment` |
| `Use*` | Opt-in to externally-managed table layouts. | `UseManualSharding` |

```csharp
namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// EF Core <see cref="EntityTypeBuilder{TEntity}"/> extensions that configure
/// DTDE's sharding and temporal behaviour declaratively from <c>OnModelCreating</c>.
/// </summary>
public static class EntityTypeBuilderExtensions
{
    // -------- Sharding --------

    /// <summary>
    /// Property-value sharding: rows are routed by a discriminator property
    /// such as a region code or tenant identifier.
    /// </summary>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Customer&gt;()
    ///     .ShardBy(c => c.Region)
    ///     .WithStorageMode(ShardStorageMode.Tables);
    /// </code>
    /// </example>
    public static ShardingBuilder<TEntity> ShardBy<TEntity, TKey>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TKey>> shardKeySelector)
        where TEntity : class;

    /// <summary>
    /// Date-range sharding: rows are routed into time-bucketed shards
    /// (year, quarter, month, or day) by a date property.
    /// </summary>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .ShardByDate(o => o.OrderDate, DateShardInterval.Year);
    /// </code>
    /// </example>
    public static ShardingBuilder<TEntity> ShardByDate<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateTime>> dateSelector,
        DateShardInterval interval = DateShardInterval.Year)
        where TEntity : class;

    /// <summary>
    /// Hash-based sharding for even distribution across a fixed shard count.
    /// </summary>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Product&gt;()
    ///     .ShardByHash(p => p.Id, shardCount: 8);
    /// </code>
    /// </example>
    public static ShardingBuilder<TEntity> ShardByHash<TEntity, TKey>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TKey>> shardKeySelector,
        int shardCount = 4)
        where TEntity : class;

    /// <summary>
    /// Manual sharding with pre-existing tables, typically managed by a SQL
    /// project or DBA-owned process. Each table is mapped via a routing predicate.
    /// </summary>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .UseManualSharding(config =>
    ///     {
    ///         config.AddTable("dbo.Orders_2023", o => o.OrderDate.Year == 2023);
    ///         config.AddTable("dbo.Orders_2024", o => o.OrderDate.Year == 2024);
    ///         config.MigrationsEnabled = false;
    ///     });
    /// </code>
    /// </example>
    public static EntityTypeBuilder<TEntity> UseManualSharding<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Action<ManualShardingConfiguration<TEntity>> configureManual)
        where TEntity : class;

    // -------- Temporal validity --------

    /// <summary>
    /// Configures bi-temporal validity tracking. Property names are user-defined,
    /// so any pair of <c>DateTime</c>/<c>DateTime?</c> properties works.
    /// </summary>
    /// <example>
    /// <code>
    /// // Standard naming
    /// modelBuilder.Entity&lt;Contract&gt;()
    ///     .HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo);
    ///
    /// // Domain-specific naming
    /// modelBuilder.Entity&lt;Policy&gt;()
    ///     .HasTemporalValidity(p => p.EffectiveDate, p => p.ExpirationDate);
    ///
    /// // Open-ended validity (no end date)
    /// modelBuilder.Entity&lt;Subscription&gt;()
    ///     .HasTemporalValidity(s => s.StartDate);
    /// </code>
    /// </example>
    public static EntityTypeBuilder<TEntity> HasTemporalValidity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime?>>? validToSelector = null)
        where TEntity : class;

    /// <summary>
    /// Overload for entities whose validity-end property is non-nullable.
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasTemporalValidity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateTime>> validFromSelector,
        Expression<Func<TEntity, DateTime>> validToSelector)
        where TEntity : class;

    /// <summary>
    /// Configures the temporal containment rule for parent-child relationships
    /// (whether child validity must fall inside the parent's validity range).
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasTemporalContainment<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        TemporalContainmentRule rule)
        where TEntity : class;
}
```

The implementations live in
[`src/Dtde.EntityFramework/Extensions/EntityTypeBuilderExtensions.cs`](https://github.com/yohasacura/dtde/blob/main/src/Dtde.EntityFramework/Extensions/EntityTypeBuilderExtensions.cs)
and write metadata via `DtdeAnnotationNames` constants on the EF model. The
chained builder type [`ShardingBuilder<TEntity>`](https://github.com/yohasacura/dtde/blob/main/src/Dtde.EntityFramework/Configuration/ShardingBuilder.cs)
exposes `WithStorageMode`, `WithTablePattern`, `WithoutMigrations`, and
`AddDatabase` for advanced storage scenarios.

### 3.2 Annotation Names

```csharp
namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Annotation names used to store DTDE configuration in EF Core model.
/// </summary>
internal static class DtdeAnnotationNames
{
    public const string Prefix = "Dtde:";
    
    public const string IsTemporal = Prefix + "IsTemporal";
    public const string ValidFromProperty = Prefix + "ValidFromProperty";
    public const string ValidToProperty = Prefix + "ValidToProperty";
    
    public const string IsSharded = Prefix + "IsSharded";
    public const string ShardKeyProperty = Prefix + "ShardKeyProperty";
    public const string ShardKeyProperties = Prefix + "ShardKeyProperties";
    public const string ShardingStrategy = Prefix + "ShardingStrategy";
}
```

---

## 4. Queryable Extensions

### 4.1 Temporal Query Extensions

```csharp
namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// LINQ extension methods for temporal queries.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Filters entities to those valid at the specified date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="date">The date to filter by.</param>
    /// <returns>A queryable filtered to valid entities.</returns>
    /// <example>
    /// <code>
    /// var activeContracts = await db.Contracts
    ///     .ValidAt(DateTime.Today)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> ValidAt<TEntity>(
        this IQueryable<TEntity> source,
        DateTime date)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        
        return source.Provider.CreateQuery<TEntity>(
            Expression.Call(
                null,
                ValidAtMethodInfo.MakeGenericMethod(typeof(TEntity)),
                source.Expression,
                Expression.Constant(date)));
    }
    
    /// <summary>
    /// Includes all historical versions of entities.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <returns>A queryable including all versions.</returns>
    /// <example>
    /// <code>
    /// var allVersions = await db.Contracts
    ///     .WithVersions()
    ///     .Where(c => c.Id == contractId)
    ///     .OrderBy(c => c.ValidFrom)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> WithVersions<TEntity>(
        this IQueryable<TEntity> source)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        
        return source.Provider.CreateQuery<TEntity>(
            Expression.Call(
                null,
                WithVersionsMethodInfo.MakeGenericMethod(typeof(TEntity)),
                source.Expression));
    }
    
    /// <summary>
    /// Filters entities to those valid within the specified date range.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="from">The range start date (inclusive).</param>
    /// <param name="to">The range end date (exclusive).</param>
    /// <returns>A queryable filtered to the date range.</returns>
    public static IQueryable<TEntity> ValidBetween<TEntity>(
        this IQueryable<TEntity> source,
        DateTime from,
        DateTime to)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        
        return source.Provider.CreateQuery<TEntity>(
            Expression.Call(
                null,
                ValidBetweenMethodInfo.MakeGenericMethod(typeof(TEntity)),
                source.Expression,
                Expression.Constant(from),
                Expression.Constant(to)));
    }
    
    /// <summary>
    /// Provides a hint for shard routing (advanced usage).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="shardIds">The shard IDs to query.</param>
    /// <returns>A queryable targeting specific shards.</returns>
    public static IQueryable<TEntity> ShardHint<TEntity>(
        this IQueryable<TEntity> source,
        params string[] shardIds)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(shardIds);
        
        return source.Provider.CreateQuery<TEntity>(
            Expression.Call(
                null,
                ShardHintMethodInfo.MakeGenericMethod(typeof(TEntity)),
                source.Expression,
                Expression.Constant(shardIds)));
    }
    
    private static readonly MethodInfo ValidAtMethodInfo = 
        typeof(QueryableExtensions).GetMethod(nameof(ValidAt))!;
    
    private static readonly MethodInfo WithVersionsMethodInfo = 
        typeof(QueryableExtensions).GetMethod(nameof(WithVersions))!;
    
    private static readonly MethodInfo ValidBetweenMethodInfo = 
        typeof(QueryableExtensions).GetMethod(nameof(ValidBetween))!;
    
    private static readonly MethodInfo ShardHintMethodInfo = 
        typeof(QueryableExtensions).GetMethod(nameof(ShardHint))!;
}
```

---

## 5. DtdeDbContext

### 5.1 Base DbContext

```csharp
namespace Dtde.EntityFramework.Context;

/// <summary>
/// Base DbContext class with DTDE temporal and sharding support.
/// Provides temporal context management and SaveChanges interception.
/// </summary>
/// <example>
/// <code>
/// public class AppDbContext : DtdeDbContext
/// {
///     public DbSet&lt;Contract&gt; Contracts => Set&lt;Contract&gt;();
///     public DbSet&lt;Policy&gt; Policies => Set&lt;Policy&gt;();
///     
///     public AppDbContext(DbContextOptions&lt;AppDbContext&gt; options) 
///         : base(options) { }
///     
///     protected override void OnModelCreating(ModelBuilder modelBuilder)
///     {
///         base.OnModelCreating(modelBuilder);
///         
///         modelBuilder.Entity&lt;Contract&gt;()
///             .HasTemporalValidity(c => c.EffectiveDate, c => c.ExpirationDate)
///             .ShardByDate(c => c.EffectiveDate, DateShardInterval.Year);
///     }
/// }
/// </code>
/// </example>
public abstract class DtdeDbContext : DbContext
{
    private readonly ITemporalContext _temporalContext;
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly IDtdeUpdateProcessor _updateProcessor;
    private readonly ILogger<DtdeDbContext> _logger;
    
    /// <summary>
    /// Gets the current temporal context for queries.
    /// </summary>
    public ITemporalContext TemporalContext => _temporalContext;
    
    /// <summary>
    /// Initializes a new instance of DtdeDbContext.
    /// </summary>
    /// <param name="options">The DbContext options.</param>
    protected DtdeDbContext(DbContextOptions options) : base(options)
    {
        var serviceProvider = options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?? throw new InvalidOperationException("Service provider not configured.");
        
        _temporalContext = serviceProvider.GetRequiredService<ITemporalContext>();
        _metadataRegistry = serviceProvider.GetRequiredService<IMetadataRegistry>();
        _updateProcessor = serviceProvider.GetRequiredService<IDtdeUpdateProcessor>();
        _logger = serviceProvider.GetRequiredService<ILogger<DtdeDbContext>>();
    }
    
    /// <summary>
    /// Sets the temporal context for all queries in this DbContext instance.
    /// </summary>
    /// <param name="date">The temporal point to filter by.</param>
    /// <example>
    /// <code>
    /// db.SetTemporalContext(DateTime.Today);
    /// var allQueries = await db.Contracts.ToListAsync(); // Auto-filtered
    /// </code>
    /// </example>
    public void SetTemporalContext(DateTime date)
    {
        ((TemporalContext)_temporalContext).SetPoint(date);
        _logger.LogDebug("Temporal context set to {Date}", date);
    }
    
    /// <summary>
    /// Clears the temporal context.
    /// </summary>
    public void ClearTemporalContext()
    {
        ((TemporalContext)_temporalContext).Clear();
        _logger.LogDebug("Temporal context cleared");
    }
    
    /// <summary>
    /// Enables access to all historical versions.
    /// </summary>
    public void EnableHistoricalAccess()
    {
        ((TemporalContext)_temporalContext).EnableAllVersions();
        _logger.LogDebug("Historical access enabled");
    }
    
    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return await SaveChangesInternalAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }
    
    /// <inheritdoc />
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        return await SaveChangesInternalAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    
    private async Task<int> SaveChangesInternalAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added 
                or EntityState.Modified 
                or EntityState.Deleted)
            .ToList();
        
        if (entries.Count == 0)
        {
            return 0;
        }
        
        // Process temporal entities through DTDE update processor
        var temporalEntries = entries
            .Where(e => _metadataRegistry.GetEntityMetadata(e.Entity.GetType())?.IsTemporal ?? false)
            .ToList();
        
        if (temporalEntries.Count > 0)
        {
            return await _updateProcessor.ProcessUpdatesAsync(
                this, 
                temporalEntries, 
                cancellationToken);
        }
        
        // Non-temporal entities use standard EF Core SaveChanges
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Apply global query filters for temporal entities if context is set
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var isTemporal = entityType.FindAnnotation(DtdeAnnotationNames.IsTemporal)?.Value as bool? ?? false;
            
            if (isTemporal)
            {
                ApplyTemporalQueryFilter(modelBuilder, entityType);
            }
        }
    }
    
    private void ApplyTemporalQueryFilter(ModelBuilder modelBuilder, IMutableEntityType entityType)
    {
        // This method generates a global query filter based on temporal context
        // The actual predicate is built dynamically using the configured property names
        var validFromProperty = entityType.FindAnnotation(DtdeAnnotationNames.ValidFromProperty)?.Value as string;
        var validToProperty = entityType.FindAnnotation(DtdeAnnotationNames.ValidToProperty)?.Value as string;
        
        if (validFromProperty is null)
        {
            _logger.LogWarning(
                "Entity {EntityType} is marked temporal but has no ValidFrom property configured",
                entityType.ClrType.Name);
            return;
        }
        
        // Global filter is applied by the query rewriter, not here
        // This allows runtime context changes
        _logger.LogDebug(
            "Temporal entity {EntityType} configured with ValidFrom={ValidFrom}, ValidTo={ValidTo}",
            entityType.ClrType.Name,
            validFromProperty,
            validToProperty ?? "(open-ended)");
    }
}
```

---

## 6. Model Building Integration

### 6.1 Model Finalizer

```csharp
namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Finalizes the EF Core model by building DTDE metadata from annotations.
/// </summary>
internal sealed class DtdeModelFinalizer
{
    private readonly IMetadataRegistryBuilder _registryBuilder;
    private readonly ILogger<DtdeModelFinalizer> _logger;
    
    public DtdeModelFinalizer(
        IMetadataRegistryBuilder registryBuilder,
        ILogger<DtdeModelFinalizer> logger)
    {
        _registryBuilder = registryBuilder;
        _logger = logger;
    }
    
    /// <summary>
    /// Processes the EF Core model and builds DTDE metadata.
    /// </summary>
    /// <param name="model">The EF Core model.</param>
    public void FinalizeModel(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            var metadata = BuildEntityMetadata(entityType);
            
            if (metadata is not null)
            {
                _registryBuilder.RegisterEntity(metadata);
                _logger.LogDebug(
                    "Registered entity {EntityType}: Temporal={IsTemporal}, Sharded={IsSharded}",
                    entityType.ClrType.Name,
                    metadata.IsTemporal,
                    metadata.IsSharded);
            }
        }
        
        // Build relations from EF Core navigation properties
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var navigation in entityType.GetNavigations())
            {
                var relationMetadata = BuildRelationMetadata(navigation);
                
                if (relationMetadata is not null)
                {
                    _registryBuilder.RegisterRelation(relationMetadata);
                }
            }
        }
    }
    
    private EntityMetadata? BuildEntityMetadata(IEntityType entityType)
    {
        var isTemporal = entityType.FindAnnotation(DtdeAnnotationNames.IsTemporal)?.Value as bool? ?? false;
        var isSharded = entityType.FindAnnotation(DtdeAnnotationNames.IsSharded)?.Value as bool? ?? false;
        
        if (!isTemporal && !isSharded)
        {
            return null; // Not a DTDE-managed entity
        }
        
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new MetadataConfigurationException(
                $"Entity {entityType.ClrType.Name} must have a primary key.",
                entityType.ClrType);
        
        var builder = new EntityMetadataBuilder(entityType.ClrType)
            .WithTableName(entityType.GetTableName() ?? entityType.ClrType.Name)
            .WithSchema(entityType.GetSchema() ?? "dbo")
            .WithPrimaryKey(BuildPropertyMetadata(primaryKey.Properties.First()));
        
        if (isTemporal)
        {
            var validFromProperty = entityType.FindAnnotation(DtdeAnnotationNames.ValidFromProperty)?.Value as string
                ?? throw new MetadataConfigurationException(
                    $"Temporal entity {entityType.ClrType.Name} must have ValidFrom property configured.",
                    entityType.ClrType);
            
            var validToProperty = entityType.FindAnnotation(DtdeAnnotationNames.ValidToProperty)?.Value as string;
            
            var validFrom = entityType.FindProperty(validFromProperty)
                ?? throw new MetadataConfigurationException(
                    $"Property {validFromProperty} not found on {entityType.ClrType.Name}.",
                    entityType.ClrType);
            
            var validTo = validToProperty is not null 
                ? entityType.FindProperty(validToProperty) 
                : null;
            
            builder.WithValidity(
                BuildPropertyMetadata(validFrom),
                validTo is not null ? BuildPropertyMetadata(validTo) : null);
        }
        
        if (isSharded)
        {
            var shardKeyProperty = entityType.FindAnnotation(DtdeAnnotationNames.ShardKeyProperty)?.Value as string;
            var shardKeyProperties = entityType.FindAnnotation(DtdeAnnotationNames.ShardKeyProperties)?.Value as List<string>;
            var strategy = entityType.FindAnnotation(DtdeAnnotationNames.ShardingStrategy)?.Value as ShardingStrategyType?
                ?? ShardingStrategyType.DateRange;
            
            if (shardKeyProperty is not null)
            {
                var prop = entityType.FindProperty(shardKeyProperty)
                    ?? throw new MetadataConfigurationException(
                        $"Shard key property {shardKeyProperty} not found on {entityType.ClrType.Name}.",
                        entityType.ClrType);
                
                builder.WithSharding(BuildPropertyMetadata(prop), strategy);
            }
            else if (shardKeyProperties is not null)
            {
                var props = shardKeyProperties
                    .Select(p => entityType.FindProperty(p) 
                        ?? throw new MetadataConfigurationException(
                            $"Shard key property {p} not found on {entityType.ClrType.Name}.",
                            entityType.ClrType))
                    .Select(BuildPropertyMetadata)
                    .ToList();
                
                builder.WithCompositeSharding(props);
            }
        }
        
        return builder.Build();
    }
    
    private PropertyMetadata BuildPropertyMetadata(IProperty property)
    {
        return new PropertyMetadata
        {
            PropertyName = property.Name,
            PropertyType = property.ClrType,
            ColumnName = property.GetColumnName() ?? property.Name,
            PropertyInfo = property.PropertyInfo!,
            GetValue = CreateGetter(property),
            SetValue = CreateSetter(property)
        };
    }
    
    private RelationMetadata? BuildRelationMetadata(INavigation navigation)
    {
        // Implementation builds relation metadata from EF Core navigations
        throw new NotImplementedException();
    }
    
    private static Func<object, object?> CreateGetter(IProperty property)
    {
        var parameter = Expression.Parameter(typeof(object), "entity");
        var cast = Expression.Convert(parameter, property.DeclaringType.ClrType);
        var access = Expression.Property(cast, property.PropertyInfo!);
        var convert = Expression.Convert(access, typeof(object));
        
        return Expression.Lambda<Func<object, object?>>(convert, parameter).Compile();
    }
    
    private static Action<object, object?> CreateSetter(IProperty property)
    {
        var entityParam = Expression.Parameter(typeof(object), "entity");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var cast = Expression.Convert(entityParam, property.DeclaringType.ClrType);
        var valueCast = Expression.Convert(valueParam, property.ClrType);
        var access = Expression.Property(cast, property.PropertyInfo!);
        var assign = Expression.Assign(access, valueCast);
        
        return Expression.Lambda<Action<object, object?>>(assign, entityParam, valueParam).Compile();
    }
}
```

---

## 7. Dependency Injection Setup

### 7.1 Service Collection Extensions

```csharp
namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// Extension methods for registering DTDE services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds DTDE services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure DTDE.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDtde(
        this IServiceCollection services,
        Action<DtdeOptionsBuilder>? configure = null)
    {
        // Core services
        services.AddSingleton<IMetadataRegistry, MetadataRegistry>();
        services.AddSingleton<IMetadataRegistryBuilder, MetadataRegistryBuilder>();
        services.AddSingleton<IShardRegistry, ShardRegistry>();
        
        // Sharding strategies
        services.AddSingleton<IShardingStrategy, DateRangeShardingStrategy>();
        services.AddSingleton<IShardingStrategy, HashShardingStrategy>();
        
        // Per-request services
        services.AddScoped<ITemporalContext, TemporalContext>();
        services.AddScoped<IDtdeQueryExecutor, DtdeQueryExecutor>();
        services.AddScoped<IDtdeUpdateProcessor, DtdeUpdateProcessor>();
        services.AddScoped<IResultMerger, ResultMerger>();
        
        // Query pipeline
        services.AddSingleton<DtdeExpressionRewriter>();
        services.AddSingleton<ShardQueryPlanner>();
        
        // Logging and diagnostics
        services.AddSingleton<IDtdeDiagnostics, DtdeDiagnostics>();
        
        if (configure is not null)
        {
            var builder = new DtdeOptionsBuilder();
            configure(builder);
            services.AddSingleton(builder.Build());
        }
        
        return services;
    }
}
```

---

## 8. Test Specifications

Following the `MethodName_Condition_ExpectedResult` pattern:

### 8.1 Fluent API Tests

```csharp
// HasValidity_WithBothProperties_StoresAnnotations
// HasValidity_WithOnlyStartProperty_AllowsOpenEnded
// UseSharding_WithDateStrategy_StoresCorrectAnnotation
// UseCompositeSharding_WithMultipleKeys_StoresAllKeys
// UseCompositeSharding_WithSingleKey_ThrowsArgumentException
```

### 8.2 DbContext Tests

```csharp
// DtdeDbContext_SetTemporalContext_UpdatesContextProperty
// DtdeDbContext_ClearTemporalContext_ResetsToNull
// DtdeDbContext_SaveChangesAsync_CallsUpdateProcessor
// DtdeDbContext_SaveChangesAsync_NonTemporalEntity_UsesBaseMethod
```

### 8.3 Queryable Extension Tests

```csharp
// ValidAt_WithDate_CreatesCorrectExpression
// WithVersions_Called_MarksQueryForHistoricalAccess
// ValidBetween_WithRange_CreatesRangeExpression
// ShardHint_WithShardIds_StoresHintInExpression
```

---

## Next Steps

Continue to [04 - Query Engine](04-query-engine.md) for shard resolution, parallel execution, and result merging details.
