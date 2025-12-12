using Dtde.Core.Metadata;
using Dtde.EntityFramework.Configuration;
using FluentAssertions;

namespace Dtde.EntityFramework.Tests.Configuration;

public class DtdeOptionsBuilderTests
{
    [Fact(DisplayName = "ConfigureEntity registers entity metadata in registry")]
    public void ConfigureEntity_RegistersEntityMetadata_InRegistry()
    {
        // Arrange
        var builder = new DtdeOptionsBuilder();

        // Act
        builder.ConfigureEntity<TestEntity>(entity =>
        {
            entity.HasTemporalValidity(
                validFrom: nameof(TestEntity.ValidFrom),
                validTo: nameof(TestEntity.ValidTo));
        });

        var options = GetBuiltOptions(builder);

        // Assert
        var metadata = options.MetadataRegistry.GetEntityMetadata<TestEntity>();
        metadata.Should().NotBeNull();
        metadata!.Validity.Should().NotBeNull();
        metadata.Validity!.ValidFromProperty.PropertyName.Should().Be("ValidFrom");
        metadata.Validity!.ValidToProperty!.PropertyName.Should().Be("ValidTo");
    }

    [Fact(DisplayName = "AddShard adds shard metadata to options")]
    public void AddShard_AddsShardMetadata_ToOptions()
    {
        // Arrange
        var builder = new DtdeOptionsBuilder();
        var shard = new ShardMetadataBuilder()
            .WithId("shard-1")
            .WithName("Test Shard")
            .WithConnectionString("Server=test;Database=TestDb")
            .Build();

        // Act
        builder.AddShard(shard);
        var options = GetBuiltOptions(builder);

        // Assert
        options.Shards.Should().ContainSingle()
            .Which.ShardId.Should().Be("shard-1");
    }

    [Fact(DisplayName = "AddShard with configure action creates and adds shard")]
    public void AddShard_WithConfigureAction_CreatesAndAddsShard()
    {
        // Arrange
        var builder = new DtdeOptionsBuilder();

        // Act
        builder.AddShard(shard =>
        {
            shard.WithId("shard-2024")
                .WithName("2024 Data")
                .WithConnectionString("Server=db;Database=Data2024")
                .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));
        });

        var options = GetBuiltOptions(builder);

        // Assert
        var shards = options.Shards.ToList();
        shards.Should().ContainSingle();
        shards[0].ShardId.Should().Be("shard-2024");
        shards[0].Name.Should().Be("2024 Data");
        shards[0].DateRange.Should().NotBeNull();
        shards[0].DateRange!.Value.Start.Should().Be(new DateTime(2024, 1, 1));
        shards[0].DateRange!.Value.End.Should().Be(new DateTime(2024, 12, 31));
    }

    [Fact(DisplayName = "SetMaxParallelShards configures parallelism limit")]
    public void SetMaxParallelShards_ConfiguresParallelismLimit()
    {
        // Arrange
        var builder = new DtdeOptionsBuilder();

        // Act
        builder.SetMaxParallelShards(5);
        var options = GetBuiltOptions(builder);

        // Assert
        options.MaxParallelShards.Should().Be(5);
    }

    [Fact(DisplayName = "SetMaxParallelShards throws for invalid values")]
    public void SetMaxParallelShards_ThrowsForInvalidValues()
    {
        // Arrange
        var builder = new DtdeOptionsBuilder();

        // Act
        var act = () => builder.SetMaxParallelShards(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "EnableDiagnostics sets diagnostics flag")]
    public void EnableDiagnostics_SetsDiagnosticsFlag()
    {
        // Arrange
        var builder = new DtdeOptionsBuilder();

        // Act
        builder.EnableDiagnostics();
        var options = GetBuiltOptions(builder);

        // Assert
        options.EnableDiagnostics.Should().BeTrue();
    }

    [Fact(DisplayName = "EnableTestMode sets test mode flag")]
    public void EnableTestMode_SetsTestModeFlag()
    {
        // Arrange
        var builder = new DtdeOptionsBuilder();

        // Act
        builder.EnableTestMode();
        var options = GetBuiltOptions(builder);

        // Assert
        options.EnableTestMode.Should().BeTrue();
    }

    [Fact(DisplayName = "SetDefaultTemporalContext configures temporal provider")]
    public void SetDefaultTemporalContext_ConfiguresTemporalProvider()
    {
        // Arrange
        var builder = new DtdeOptionsBuilder();
        var expectedDate = new DateTime(2024, 6, 15);

        // Act
        builder.SetDefaultTemporalContext(() => expectedDate);
        var options = GetBuiltOptions(builder);

        // Assert
        options.DefaultTemporalContextProvider.Should().NotBeNull();
        options.DefaultTemporalContextProvider!().Should().Be(expectedDate);
    }

    [Fact(DisplayName = "AddShards adds multiple shards at once")]
    public void AddShards_AddsMultipleShards_AtOnce()
    {
        // Arrange
        var builder = new DtdeOptionsBuilder();
        var shards = new[]
        {
            new ShardMetadataBuilder().WithId("s1").WithConnectionString("cs1").Build(),
            new ShardMetadataBuilder().WithId("s2").WithConnectionString("cs2").Build(),
            new ShardMetadataBuilder().WithId("s3").WithConnectionString("cs3").Build()
        };

        // Act
        builder.AddShards(shards);
        var options = GetBuiltOptions(builder);

        // Assert
        options.Shards.Should().HaveCount(3);
    }

    [Fact(DisplayName = "Builder fluent API allows method chaining")]
    public void Builder_FluentApi_AllowsMethodChaining()
    {
        // Arrange & Act
        var builder = new DtdeOptionsBuilder()
            .EnableDiagnostics()
            .EnableTestMode()
            .SetMaxParallelShards(20)
            .AddShard(s => s.WithId("test").WithConnectionString("cs"))
            .ConfigureEntity<TestEntity>(e => e.HasTemporalValidity("ValidFrom", "ValidTo"));

        var options = GetBuiltOptions(builder);

        // Assert
        options.EnableDiagnostics.Should().BeTrue();
        options.EnableTestMode.Should().BeTrue();
        options.MaxParallelShards.Should().Be(20);
        options.Shards.Should().ContainSingle();
        options.MetadataRegistry.GetEntityMetadata<TestEntity>().Should().NotBeNull();
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
