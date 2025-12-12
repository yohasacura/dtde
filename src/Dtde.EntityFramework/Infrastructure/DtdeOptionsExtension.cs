using Dtde.EntityFramework.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.EntityFramework.Infrastructure;

/// <summary>
/// EF Core options extension for DTDE configuration.
/// </summary>
public sealed class DtdeOptionsExtension : IDbContextOptionsExtension
{
    private DtdeOptions _options = new();
    private ExtensionInfo? _info;

    /// <summary>
    /// Initializes a new instance of the <see cref="DtdeOptionsExtension"/> class.
    /// </summary>
    public DtdeOptionsExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance by copying from an existing instance.
    /// </summary>
    /// <param name="copyFrom">The instance to copy from.</param>
    private DtdeOptionsExtension(DtdeOptionsExtension copyFrom)
    {
        _options = copyFrom._options;
    }

    /// <summary>
    /// Gets the DTDE options.
    /// </summary>
    public DtdeOptions Options => _options;

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>
    /// Creates a new instance with the specified options.
    /// </summary>
    /// <param name="options">The options to use.</param>
    /// <returns>A new instance with the specified options.</returns>
    public DtdeOptionsExtension WithOptions(DtdeOptions options)
    {
        var clone = new DtdeOptionsExtension(this)
        {
            _options = options
        };
        return clone;
    }

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
        // Register DTDE services
        services.AddSingleton(_options);
        services.AddSingleton(_options.MetadataRegistry);
        services.AddSingleton(_options.ShardRegistry);
        services.AddSingleton(_options.TemporalContext);
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        // Validate options are properly configured
        _options.MetadataRegistry.Validate();
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(DtdeOptionsExtension extension) : base(extension)
        {
        }

        private new DtdeOptionsExtension Extension => (DtdeOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using DTDE ";

        public override int GetServiceProviderHashCode()
        {
            return Extension._options.GetHashCode();
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is ExtensionInfo otherInfo
                && Extension._options == otherInfo.Extension._options;
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["DTDE:Enabled"] = "true";
            debugInfo["DTDE:ShardCount"] = Extension._options.ShardRegistry.GetAllShards().Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            debugInfo["DTDE:EntityCount"] = Extension._options.MetadataRegistry.GetAllEntityMetadata().Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
