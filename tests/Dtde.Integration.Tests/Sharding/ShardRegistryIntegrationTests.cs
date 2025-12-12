using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;

namespace Dtde.Integration.Tests.Sharding;

/// <summary>
/// Integration tests for ShardRegistry functionality.
/// </summary>
public class ShardRegistryIntegrationTests
{
    [Fact(DisplayName = "ShardRegistry resolves shards by date range")]
    public void ShardRegistry_ResolvesShardsByDateRange()
    {
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

        var shards2023 = registry.GetShardsForDateRange(
            new DateTime(2023, 6, 1),
            new DateTime(2023, 6, 30)).ToList();

        var shards2024 = registry.GetShardsForDateRange(
            new DateTime(2024, 6, 1),
            new DateTime(2024, 6, 30)).ToList();

        var shardsSpanning = registry.GetShardsForDateRange(
            new DateTime(2023, 10, 1),
            new DateTime(2024, 3, 31)).ToList();

        Assert.Single(shards2023);
        Assert.Equal("shard-2023", shards2023[0].ShardId);
        Assert.Single(shards2024);
        Assert.Equal("shard-2024", shards2024[0].ShardId);
        Assert.Equal(2, shardsSpanning.Count);
    }

    [Fact(DisplayName = "ShardRegistry returns shards ordered by priority")]
    public void ShardRegistry_ReturnsShardsOrderedByPriority()
    {
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

        var orderedShards = registry.GetAllShards().ToList();

        Assert.Equal("high-priority", orderedShards[0].ShardId);
        Assert.Equal("default-priority", orderedShards[1].ShardId);
        Assert.Equal("low-priority", orderedShards[2].ShardId);
    }

    [Fact(DisplayName = "ShardRegistry filters writable shards only")]
    public void ShardRegistry_FiltersWritableShardsOnly()
    {
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

        var writableShards = registry.GetWritableShards().ToList();

        Assert.Single(writableShards);
        Assert.Equal("writable-shard", writableShards[0].ShardId);
    }

    [Fact(DisplayName = "ShardRegistry GetShard returns correct shard by ID")]
    public void ShardRegistry_GetShard_ReturnsCorrectShardById()
    {
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("target-shard")
                .WithName("Target Shard")
                .WithConnectionString("Server=target;Database=TestDb")
                .Build()
        };

        var registry = new ShardRegistry(shards);

        var found = registry.GetShard("target-shard");
        var notFound = registry.GetShard("non-existent");

        Assert.NotNull(found);
        Assert.Equal("Target Shard", found!.Name);
        Assert.Null(notFound);
    }

    [Fact(DisplayName = "ShardRegistry distinguishes Hot, Warm, Cold tiers")]
    public void ShardRegistry_DistinguishesHotWarmColdTiers()
    {
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

        var allShards = registry.GetAllShards().ToList();

        Assert.Equal(3, allShards.Count);
        Assert.Equal(ShardTier.Hot, allShards.Single(s => s.ShardId == "hot").Tier);
        Assert.Equal(ShardTier.Warm, allShards.Single(s => s.ShardId == "warm").Tier);
        Assert.Equal(ShardTier.Cold, allShards.Single(s => s.ShardId == "cold").Tier);
    }

    [Fact(DisplayName = "ShardRegistry GetShardsByTier returns shards by tier")]
    public void ShardRegistry_GetShardsByTier_ReturnsShardsByTier()
    {
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

        var hotShards = registry.GetShardsByTier(ShardTier.Hot);
        var coldShards = registry.GetShardsByTier(ShardTier.Cold);
        var warmShards = registry.GetShardsByTier(ShardTier.Warm);

        Assert.Equal(2, hotShards.Count);
        Assert.Single(coldShards);
        Assert.Empty(warmShards);
    }

    [Fact(DisplayName = "ShardRegistry AddShard dynamically adds shard")]
    public void ShardRegistry_AddShard_DynamicallyAddsShard()
    {
        var registry = new ShardRegistry();
        var shard = new ShardMetadataBuilder()
            .WithId("dynamic-shard")
            .WithConnectionString("cs")
            .Build();

        registry.AddShard(shard);

        var allShards = registry.GetAllShards().ToList();
        Assert.Single(allShards);
        Assert.Equal("dynamic-shard", allShards[0].ShardId);
    }

    [Fact(DisplayName = "ShardRegistry handles shards without date ranges")]
    public void ShardRegistry_HandlesShardsWithoutDateRanges()
    {
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("unlimited-shard")
                .WithConnectionString("cs")
                .Build()
        };

        var registry = new ShardRegistry(shards);

        var results = registry.GetShardsForDateRange(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31)).ToList();

        Assert.Single(results);
        Assert.Equal("unlimited-shard", results[0].ShardId);
    }
}
