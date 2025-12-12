using Dtde.Core.Metadata;
using Dtde.EntityFramework.Configuration;

namespace Dtde.EntityFramework.Tests.Configuration;

public class DtdeOptionsBuilderTests
{
    [Fact(DisplayName = "ConfigureEntity registers entity metadata in registry")]
    public void ConfigureEntity_RegistersEntityMetadata_InRegistry()
    {
        var builder = new DtdeOptionsBuilder();

        builder.ConfigureEntity<TestEntity>(entity =>
        {
            entity.HasTemporalValidity(
                validFrom: nameof(TestEntity.ValidFrom),
                validTo: nameof(TestEntity.ValidTo));
        });

        var options = GetBuiltOptions(builder);

        var metadata = options.MetadataRegistry.GetEntityMetadata<TestEntity>();
        Assert.NotNull(metadata);
        Assert.NotNull(metadata!.Validity);
        Assert.Equal("ValidFrom", metadata.Validity!.ValidFromProperty.PropertyName);
        Assert.Equal("ValidTo", metadata.Validity!.ValidToProperty!.PropertyName);
    }

    [Fact(DisplayName = "AddShard adds shard metadata to options")]
    public void AddShard_AddsShardMetadata_ToOptions()
    {
        var builder = new DtdeOptionsBuilder();
        var shard = new ShardMetadataBuilder()
            .WithId("shard-1")
            .WithName("Test Shard")
            .WithConnectionString("Server=test;Database=TestDb")
            .Build();

        builder.AddShard(shard);
        var options = GetBuiltOptions(builder);

        Assert.Single(options.Shards);
        Assert.Equal("shard-1", options.Shards.First().ShardId);
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

    [Fact(DisplayName = "AddShards adds multiple shards at once")]
    public void AddShards_AddsMultipleShards_AtOnce()
    {
        var builder = new DtdeOptionsBuilder();
        var shards = new[]
        {
            new ShardMetadataBuilder().WithId("s1").WithConnectionString("cs1").Build(),
            new ShardMetadataBuilder().WithId("s2").WithConnectionString("cs2").Build(),
            new ShardMetadataBuilder().WithId("s3").WithConnectionString("cs3").Build()
        };

        builder.AddShards(shards);
        var options = GetBuiltOptions(builder);

        Assert.Equal(3, options.Shards.Count);
    }

    [Fact(DisplayName = "Builder fluent API allows method chaining")]
    public void Builder_FluentApi_AllowsMethodChaining()
    {
        var builder = new DtdeOptionsBuilder()
            .EnableDiagnostics()
            .EnableTestMode()
            .SetMaxParallelShards(20)
            .AddShard(s => s.WithId("test").WithConnectionString("cs"))
            .ConfigureEntity<TestEntity>(e => e.HasTemporalValidity("ValidFrom", "ValidTo"));

        var options = GetBuiltOptions(builder);

        Assert.True(options.EnableDiagnostics);
        Assert.True(options.EnableTestMode);
        Assert.Equal(20, options.MaxParallelShards);
        Assert.Single(options.Shards);
        Assert.NotNull(options.MetadataRegistry.GetEntityMetadata<TestEntity>());
    }

    /// <summary>
    /// Helper to access the internal Build method via reflection.
    /// </summary>
    private static DtdeOptions GetBuiltOptions(DtdeOptionsBuilder builder)
    {
        var buildMethod = typeof(DtdeOptionsBuilder)
            .GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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
