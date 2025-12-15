using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.Core.Sharding;

namespace Dtde.Core.Tests.Metadata;

/// <summary>
/// Tests for <see cref="ShardingConfiguration"/>.
/// </summary>
public class ShardingConfigurationTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesConfiguration()
    {
        var properties = new List<IPropertyMetadata>
        {
            PropertyMetadata.FromExpression<TestEntity, string>(e => e.Region)
        };
        var strategy = new PropertyBasedShardingStrategy();

        var config = new ShardingConfiguration(
            ShardingStrategyType.PropertyValue,
            ShardStorageMode.Tables,
            null,
            properties,
            strategy);

        Assert.Equal(ShardingStrategyType.PropertyValue, config.StrategyType);
        Assert.Equal(ShardStorageMode.Tables, config.StorageMode);
        Assert.Single(config.ShardKeyProperties);
        Assert.Same(strategy, config.Strategy);
    }

    [Fact]
    public void Constructor_ThrowsForNullShardKeyProperties()
    {
        var strategy = new PropertyBasedShardingStrategy();

        Assert.Throws<ArgumentNullException>(() =>
            new ShardingConfiguration(
                ShardingStrategyType.PropertyValue,
                ShardStorageMode.Tables,
                null,
                null!,
                strategy));
    }

    [Fact]
    public void Constructor_ThrowsForNullStrategy()
    {
        var properties = new List<IPropertyMetadata>();

        Assert.Throws<ArgumentNullException>(() =>
            new ShardingConfiguration(
                ShardingStrategyType.PropertyValue,
                ShardStorageMode.Tables,
                null,
                properties,
                null!));
    }

    [Fact]
    public void Create_WithPropertyValueStrategy_CreatesValidConfiguration()
    {
        var strategy = new PropertyBasedShardingStrategy();

        var config = ShardingConfiguration.Create<TestEntity, string>(
            e => e.Region,
            ShardStorageMode.Tables,
            strategy);

        Assert.Equal(ShardingStrategyType.PropertyValue, config.StrategyType);
        Assert.Equal(ShardStorageMode.Tables, config.StorageMode);
        Assert.Single(config.ShardKeyProperties);
        Assert.Equal("Region", config.ShardKeyProperties[0].PropertyName);
        Assert.NotNull(config.ShardKeyExpression);
    }

    [Fact]
    public void Create_WithDatabaseStorageMode_SetsStorageMode()
    {
        var strategy = new PropertyBasedShardingStrategy();

        var config = ShardingConfiguration.Create<TestEntity, string>(
            e => e.Region,
            ShardStorageMode.Databases,
            strategy);

        Assert.Equal(ShardStorageMode.Databases, config.StorageMode);
    }

    [Fact]
    public void Create_WithManualStorageMode_SetsStorageMode()
    {
        var strategy = new PropertyBasedShardingStrategy();

        var config = ShardingConfiguration.Create<TestEntity, string>(
            e => e.Region,
            ShardStorageMode.Manual,
            strategy);

        Assert.Equal(ShardStorageMode.Manual, config.StorageMode);
    }

    [Fact]
    public void CreateDateBased_WithDateRangeStrategy_CreatesValidConfiguration()
    {
        var strategy = new DateRangeShardingStrategy();

        var config = ShardingConfiguration.CreateDateBased<TestTemporalEntity>(
            e => e.ValidFrom,
            DateShardInterval.Month,
            ShardStorageMode.Tables,
            strategy);

        Assert.Equal(ShardingStrategyType.DateRange, config.StrategyType);
        Assert.Equal(DateShardInterval.Month, config.DateInterval);
        Assert.Single(config.ShardKeyProperties);
        Assert.Equal("ValidFrom", config.ShardKeyProperties[0].PropertyName);
    }

    [Fact]
    public void CreateDateBased_WithQuarterlyInterval_SetsInterval()
    {
        var strategy = new DateRangeShardingStrategy();

        var config = ShardingConfiguration.CreateDateBased<TestTemporalEntity>(
            e => e.ValidFrom,
            DateShardInterval.Quarter,
            ShardStorageMode.Tables,
            strategy);

        Assert.Equal(DateShardInterval.Quarter, config.DateInterval);
    }

    [Fact]
    public void CreateDateBased_WithYearlyInterval_SetsInterval()
    {
        var strategy = new DateRangeShardingStrategy();

        var config = ShardingConfiguration.CreateDateBased<TestTemporalEntity>(
            e => e.ValidFrom,
            DateShardInterval.Year,
            ShardStorageMode.Tables,
            strategy);

        Assert.Equal(DateShardInterval.Year, config.DateInterval);
    }

    [Fact]
    public void MigrationsEnabled_DefaultsToTrue()
    {
        var properties = new List<IPropertyMetadata>();
        var strategy = new PropertyBasedShardingStrategy();

        var config = new ShardingConfiguration(
            ShardingStrategyType.PropertyValue,
            ShardStorageMode.Tables,
            null,
            properties,
            strategy);

        Assert.True(config.MigrationsEnabled);
    }

    [Fact]
    public void MigrationsEnabled_CanBeSetToFalse()
    {
        var properties = new List<IPropertyMetadata>();
        var strategy = new PropertyBasedShardingStrategy();

        var config = new ShardingConfiguration(
            ShardingStrategyType.PropertyValue,
            ShardStorageMode.Tables,
            null,
            properties,
            strategy)
        {
            MigrationsEnabled = false
        };

        Assert.False(config.MigrationsEnabled);
    }

    [Fact]
    public void TableNamePattern_DefaultsToNull()
    {
        var properties = new List<IPropertyMetadata>();
        var strategy = new PropertyBasedShardingStrategy();

        var config = new ShardingConfiguration(
            ShardingStrategyType.PropertyValue,
            ShardStorageMode.Tables,
            null,
            properties,
            strategy);

        Assert.Null(config.TableNamePattern);
    }

    [Fact]
    public void TableNamePattern_CanBeSet()
    {
        var properties = new List<IPropertyMetadata>();
        var strategy = new PropertyBasedShardingStrategy();

        var config = new ShardingConfiguration(
            ShardingStrategyType.PropertyValue,
            ShardStorageMode.Tables,
            null,
            properties,
            strategy)
        {
            TableNamePattern = "{EntityName}_{ShardKey}"
        };

        Assert.Equal("{EntityName}_{ShardKey}", config.TableNamePattern);
    }

    [Fact]
    public void DateInterval_DefaultsToNull()
    {
        var properties = new List<IPropertyMetadata>();
        var strategy = new PropertyBasedShardingStrategy();

        var config = new ShardingConfiguration(
            ShardingStrategyType.PropertyValue,
            ShardStorageMode.Tables,
            null,
            properties,
            strategy);

        Assert.Null(config.DateInterval);
    }

    [Fact]
    public void Create_WithIntegerShardKey_CreatesConfiguration()
    {
        var strategy = new HashShardingStrategy(4);

        var config = ShardingConfiguration.Create<TestEntity, int>(
            e => e.Id,
            ShardStorageMode.Databases,
            strategy);

        Assert.Single(config.ShardKeyProperties);
        Assert.Equal("Id", config.ShardKeyProperties[0].PropertyName);
        Assert.Equal(typeof(int), config.ShardKeyProperties[0].PropertyType);
    }

    [Fact]
    public void ShardKeyExpression_IsSetCorrectly()
    {
        var strategy = new PropertyBasedShardingStrategy();

        var config = ShardingConfiguration.Create<TestEntity, string>(
            e => e.Region,
            ShardStorageMode.Tables,
            strategy);

        Assert.NotNull(config.ShardKeyExpression);

        var func = (Func<TestEntity, string>)config.ShardKeyExpression.Compile();
        var entity = new TestEntity { Region = "EU" };
        Assert.Equal("EU", func(entity));
    }

    private class TestEntity
    {
        public int Id { get; set; }
        public string Region { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private class TestTemporalEntity
    {
        public int Id { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
    }
}
