using System.Reflection;

using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.EntityFramework.Configuration;

namespace Dtde.EntityFramework.Tests.Configuration;

public class DtdeOptionsBuilderTests
{
    [Fact(DisplayName = "AddShard(string) adds a table-mode shard with id-as-key")]
    public void AddShardString_AddsTableModeShard_WithIdAsKey()
    {
        var builder = new DtdeOptionsBuilder();

        builder.AddShard("EU");
        var options = GetBuiltOptions(builder);

        Assert.Single(options.Shards);
        var shard = options.Shards.First();
        Assert.Equal("EU", shard.ShardId);
        Assert.Equal("EU", shard.ShardKeyValue);
        Assert.Equal(ShardStorageMode.Tables, shard.StorageMode);
    }

    [Fact(DisplayName = "AddShard(string, connectionString) adds a database-mode shard")]
    public void AddShardWithConnectionString_AddsDatabaseModeShard()
    {
        var builder = new DtdeOptionsBuilder();

        builder.AddShard("EU", "Server=eu-db;Database=Customers;");
        var options = GetBuiltOptions(builder);

        var shard = options.Shards.First();
        Assert.Equal("EU", shard.ShardId);
        Assert.Equal("Server=eu-db;Database=Customers;", shard.ConnectionString);
        Assert.Equal(ShardStorageMode.Databases, shard.StorageMode);
    }

    [Fact(DisplayName = "AddShards(params) adds multiple table-mode shards")]
    public void AddShardsParams_AddsMultipleTableModeShards()
    {
        var builder = new DtdeOptionsBuilder();

        builder.AddShards("EU", "US", "APAC");
        var options = GetBuiltOptions(builder);

        Assert.Equal(3, options.Shards.Count);
        Assert.Contains(options.Shards, s => s.ShardId == "EU");
        Assert.Contains(options.Shards, s => s.ShardId == "US");
        Assert.Contains(options.Shards, s => s.ShardId == "APAC");
    }

    [Fact(DisplayName = "AddShard with configure action creates and adds shard")]
    public void AddShard_WithConfigureAction_CreatesAndAddsShard()
    {
        var builder = new DtdeOptionsBuilder();

        builder.AddShard(shard =>
        {
            shard.WithId("shard-2024")
                .WithName("2024 Data")
                .WithConnectionString("Server=db;Database=Data2024")
                .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));
        });

        var options = GetBuiltOptions(builder);

        var shards = options.Shards.ToList();
        Assert.Single(shards);
        Assert.Equal("shard-2024", shards[0].ShardId);
        Assert.Equal("2024 Data", shards[0].Name);
        Assert.NotNull(shards[0].DateRange);
        Assert.Equal(new DateTime(2024, 1, 1), shards[0].DateRange!.Value.Start);
        Assert.Equal(new DateTime(2024, 12, 31), shards[0].DateRange!.Value.End);
    }

    [Fact(DisplayName = "SetMaxParallelShards configures parallelism limit")]
    public void SetMaxParallelShards_ConfiguresParallelismLimit()
    {
        var builder = new DtdeOptionsBuilder();

        builder.SetMaxParallelShards(5);
        var options = GetBuiltOptions(builder);

        Assert.Equal(5, options.MaxParallelShards);
    }

    [Fact(DisplayName = "SetMaxParallelShards throws for invalid values")]
    public void SetMaxParallelShards_ThrowsForInvalidValues()
    {
        var builder = new DtdeOptionsBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetMaxParallelShards(0));
    }

    [Fact(DisplayName = "EnableDiagnostics sets diagnostics flag")]
    public void EnableDiagnostics_SetsDiagnosticsFlag()
    {
        var builder = new DtdeOptionsBuilder();

        builder.EnableDiagnostics();
        var options = GetBuiltOptions(builder);

        Assert.True(options.EnableDiagnostics);
    }

    [Fact(DisplayName = "EnableTestMode sets test mode flag")]
    public void EnableTestMode_SetsTestModeFlag()
    {
        var builder = new DtdeOptionsBuilder();

        builder.EnableTestMode();
        var options = GetBuiltOptions(builder);

        Assert.True(options.EnableTestMode);
    }

    [Fact(DisplayName = "SetDefaultTemporalContext configures temporal provider")]
    public void SetDefaultTemporalContext_ConfiguresTemporalProvider()
    {
        var builder = new DtdeOptionsBuilder();
        var expectedDate = new DateTime(2024, 6, 15);

        builder.SetDefaultTemporalContext(() => expectedDate);
        var options = GetBuiltOptions(builder);

        Assert.NotNull(options.DefaultTemporalContextProvider);
        Assert.Equal(expectedDate, options.DefaultTemporalContextProvider!());
    }

    [Fact(DisplayName = "Builder fluent API allows method chaining")]
    public void Builder_FluentApi_AllowsMethodChaining()
    {
        var builder = new DtdeOptionsBuilder()
            .EnableDiagnostics()
            .EnableTestMode()
            .SetMaxParallelShards(20)
            .AddShards("EU", "US");

        var options = GetBuiltOptions(builder);

        Assert.True(options.EnableDiagnostics);
        Assert.True(options.EnableTestMode);
        Assert.Equal(20, options.MaxParallelShards);
        Assert.Equal(2, options.Shards.Count);
    }

    private static DtdeOptions GetBuiltOptions(DtdeOptionsBuilder builder)
    {
        var buildMethod = typeof(DtdeOptionsBuilder)
            .GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance);
        return (DtdeOptions)buildMethod!.Invoke(builder, null)!;
    }
}

public class TestEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
