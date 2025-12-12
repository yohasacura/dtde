using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using FluentAssertions;

namespace Dtde.Integration.Tests.Sharding;

/// <summary>
/// Integration tests for ShardRegistry functionality.
/// </summary>
public class ShardRegistryIntegrationTests
{
    [Fact(DisplayName = "ShardRegistry resolves shards by date range")]
    public void ShardRegistry_ResolvesShardsByDateRange()
    {
        // Arrange
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("shard-2023")
                .WithName("2023 Archive")
                .WithConnectionString("Server=archive;Database=Data2023")
                .WithDateRange(new DateTime(2023, 1, 1), new DateTime(2023, 12, 31))
                .WithTier(ShardTier.Cold)
                .AsReadOnly()
                .Build(),
            new ShardMetadataBuilder()
                .WithId("shard-2024")
                .WithName("2024 Data")
                .WithConnectionString("Server=primary;Database=Data2024")
                .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31))
                .WithTier(ShardTier.Hot)
                .Build()
        };

        var registry = new ShardRegistry(shards);

        // Act
        var shards2023 = registry.GetShardsForDateRange(
            new DateTime(2023, 6, 1),
            new DateTime(2023, 6, 30)).ToList();

        var shards2024 = registry.GetShardsForDateRange(
            new DateTime(2024, 6, 1),
            new DateTime(2024, 6, 30)).ToList();

        var shardsSpanning = registry.GetShardsForDateRange(
            new DateTime(2023, 10, 1),
            new DateTime(2024, 3, 31)).ToList();

        // Assert
        shards2023.Should().ContainSingle().Which.ShardId.Should().Be("shard-2023");
        shards2024.Should().ContainSingle().Which.ShardId.Should().Be("shard-2024");
        shardsSpanning.Should().HaveCount(2);
    }

    [Fact(DisplayName = "ShardRegistry returns shards ordered by priority")]
    public void ShardRegistry_ReturnsShardsOrderedByPriority()
    {
        // Arrange
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("low-priority")
                .WithConnectionString("cs1")
                .WithPriority(200)
                .Build(),
            new ShardMetadataBuilder()
                .WithId("high-priority")
                .WithConnectionString("cs2")
                .WithPriority(50)
                .Build(),
            new ShardMetadataBuilder()
                .WithId("default-priority")
                .WithConnectionString("cs3")
                .WithPriority(100)
                .Build()
        };

        var registry = new ShardRegistry(shards);

        // Act
        var orderedShards = registry.GetAllShards().ToList();

        // Assert - ShardRegistry orders by priority internally
        orderedShards[0].ShardId.Should().Be("high-priority");
        orderedShards[1].ShardId.Should().Be("default-priority");
        orderedShards[2].ShardId.Should().Be("low-priority");
    }

    [Fact(DisplayName = "ShardRegistry filters writable shards only")]
    public void ShardRegistry_FiltersWritableShardsOnly()
    {
        // Arrange
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("writable-shard")
                .WithConnectionString("cs1")
                .Build(),
            new ShardMetadataBuilder()
                .WithId("readonly-shard")
                .WithConnectionString("cs2")
                .AsReadOnly()
                .Build()
        };

        var registry = new ShardRegistry(shards);

        // Act
        var writableShards = registry.GetWritableShards().ToList();

        // Assert
        writableShards.Should().ContainSingle()
            .Which.ShardId.Should().Be("writable-shard");
    }

    [Fact(DisplayName = "ShardRegistry GetShard returns correct shard by ID")]
    public void ShardRegistry_GetShard_ReturnsCorrectShardById()
    {
        // Arrange
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("target-shard")
                .WithName("Target Shard")
                .WithConnectionString("Server=target;Database=TestDb")
                .Build()
        };

        var registry = new ShardRegistry(shards);

        // Act
        var found = registry.GetShard("target-shard");
        var notFound = registry.GetShard("non-existent");

        // Assert
        found.Should().NotBeNull();
        found!.Name.Should().Be("Target Shard");
        notFound.Should().BeNull();
    }

    [Fact(DisplayName = "ShardRegistry distinguishes Hot, Warm, Cold tiers")]
    public void ShardRegistry_DistinguishesHotWarmColdTiers()
    {
        // Arrange
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("hot")
                .WithConnectionString("cs")
                .WithTier(ShardTier.Hot)
                .Build(),
            new ShardMetadataBuilder()
                .WithId("warm")
                .WithConnectionString("cs")
                .WithTier(ShardTier.Warm)
                .Build(),
            new ShardMetadataBuilder()
                .WithId("cold")
                .WithConnectionString("cs")
                .WithTier(ShardTier.Cold)
                .Build()
        };

        var registry = new ShardRegistry(shards);

        // Act
        var allShards = registry.GetAllShards().ToList();

        // Assert
        allShards.Should().HaveCount(3);
        allShards.Single(s => s.ShardId == "hot").Tier.Should().Be(ShardTier.Hot);
        allShards.Single(s => s.ShardId == "warm").Tier.Should().Be(ShardTier.Warm);
        allShards.Single(s => s.ShardId == "cold").Tier.Should().Be(ShardTier.Cold);
    }

    [Fact(DisplayName = "ShardRegistry GetShardsByTier returns shards by tier")]
    public void ShardRegistry_GetShardsByTier_ReturnsShardsByTier()
    {
        // Arrange
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("hot-1")
                .WithConnectionString("cs1")
                .WithTier(ShardTier.Hot)
                .Build(),
            new ShardMetadataBuilder()
                .WithId("hot-2")
                .WithConnectionString("cs2")
                .WithTier(ShardTier.Hot)
                .Build(),
            new ShardMetadataBuilder()
                .WithId("cold-1")
                .WithConnectionString("cs3")
                .WithTier(ShardTier.Cold)
                .Build()
        };

        var registry = new ShardRegistry(shards);

        // Act
        var hotShards = registry.GetShardsByTier(ShardTier.Hot);
        var coldShards = registry.GetShardsByTier(ShardTier.Cold);
        var warmShards = registry.GetShardsByTier(ShardTier.Warm);

        // Assert
        hotShards.Should().HaveCount(2);
        coldShards.Should().ContainSingle();
        warmShards.Should().BeEmpty();
    }

    [Fact(DisplayName = "ShardRegistry AddShard dynamically adds shard")]
    public void ShardRegistry_AddShard_DynamicallyAddsShard()
    {
        // Arrange
        var registry = new ShardRegistry();
        var shard = new ShardMetadataBuilder()
            .WithId("dynamic-shard")
            .WithConnectionString("cs")
            .Build();

        // Act
        registry.AddShard(shard);

        // Assert
        registry.GetAllShards().Should().ContainSingle()
            .Which.ShardId.Should().Be("dynamic-shard");
    }

    [Fact(DisplayName = "ShardRegistry handles shards without date ranges")]
    public void ShardRegistry_HandlesShardsWithoutDateRanges()
    {
        // Arrange - A shard with no date range should match all date queries
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("unlimited-shard")
                .WithConnectionString("cs")
                .Build() // No date range
        };

        var registry = new ShardRegistry(shards);

        // Act
        var results = registry.GetShardsForDateRange(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31)).ToList();

        // Assert - Shards without date range should be returned for any query
        results.Should().ContainSingle()
            .Which.ShardId.Should().Be("unlimited-shard");
    }
}
