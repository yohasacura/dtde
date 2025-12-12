using Dtde.Core.Metadata;
using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Tests.Metadata;

public class ShardRegistryTests
{
    [Fact]
    public void GetAllShards_ReturnsAllShards()
    {
        // Arrange
        var shards = new[]
        {
            CreateShard("Shard1", ShardTier.Hot, 1),
            CreateShard("Shard2", ShardTier.Warm, 2),
            CreateShard("Shard3", ShardTier.Cold, 3)
        };
        var registry = new ShardRegistry(shards);

        // Act
        var result = registry.GetAllShards();

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GetAllShards_ReturnsInPriorityOrder()
    {
        // Arrange
        var shards = new[]
        {
            CreateShard("Shard3", ShardTier.Cold, 3),
            CreateShard("Shard1", ShardTier.Hot, 1),
            CreateShard("Shard2", ShardTier.Warm, 2)
        };
        var registry = new ShardRegistry(shards);

        // Act
        var result = registry.GetAllShards();

        // Assert
        Assert.Equal("Shard1", result[0].ShardId);
        Assert.Equal("Shard2", result[1].ShardId);
        Assert.Equal("Shard3", result[2].ShardId);
    }

    [Fact]
    public void GetShard_ReturnsCorrectShard()
    {
        // Arrange
        var shards = new[]
        {
            CreateShard("Shard1", ShardTier.Hot, 1),
            CreateShard("Shard2", ShardTier.Warm, 2)
        };
        var registry = new ShardRegistry(shards);

        // Act
        var result = registry.GetShard("Shard2");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Shard2", result.ShardId);
    }

    [Fact]
    public void GetShard_ReturnsNullForNonExistent()
    {
        // Arrange
        var shards = new[] { CreateShard("Shard1", ShardTier.Hot, 1) };
        var registry = new ShardRegistry(shards);

        // Act
        var result = registry.GetShard("NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetShardsByTier_FiltersCorrectly()
    {
        // Arrange
        var shards = new[]
        {
            CreateShard("Hot1", ShardTier.Hot, 1),
            CreateShard("Hot2", ShardTier.Hot, 2),
            CreateShard("Warm1", ShardTier.Warm, 3)
        };
        var registry = new ShardRegistry(shards);

        // Act
        var result = registry.GetShardsByTier(ShardTier.Hot);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.Equal(ShardTier.Hot, s.Tier));
    }

    [Fact]
    public void GetWritableShards_ExcludesReadOnly()
    {
        // Arrange
        var shards = new[]
        {
            CreateShard("Writable1", ShardTier.Hot, 1, false),
            CreateShard("ReadOnly1", ShardTier.Archive, 2, true),
            CreateShard("Writable2", ShardTier.Warm, 3, false)
        };
        var registry = new ShardRegistry(shards);

        // Act
        var result = registry.GetWritableShards();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, s => s.IsReadOnly);
    }

    [Fact]
    public void GetShardsForDateRange_ReturnsOverlappingShards()
    {
        // Arrange
        var q1Start = new DateTime(2024, 1, 1);
        var q1End = new DateTime(2024, 4, 1);
        var q2Start = new DateTime(2024, 4, 1);
        var q2End = new DateTime(2024, 7, 1);

        var shards = new[]
        {
            CreateShardWithDateRange("Q1-2024", q1Start, q1End),
            CreateShardWithDateRange("Q2-2024", q2Start, q2End)
        };
        var registry = new ShardRegistry(shards);

        // Act - query for February
        var result = registry.GetShardsForDateRange(
            new DateTime(2024, 2, 1),
            new DateTime(2024, 2, 28));

        // Assert
        Assert.Single(result);
        Assert.Equal("Q1-2024", result[0].ShardId);
    }

    [Fact]
    public void GetShardsForDateRange_ReturnsMultipleShardsForSpanningRange()
    {
        // Arrange
        var q1Start = new DateTime(2024, 1, 1);
        var q1End = new DateTime(2024, 4, 1);
        var q2Start = new DateTime(2024, 4, 1);
        var q2End = new DateTime(2024, 7, 1);

        var shards = new[]
        {
            CreateShardWithDateRange("Q1-2024", q1Start, q1End),
            CreateShardWithDateRange("Q2-2024", q2Start, q2End)
        };
        var registry = new ShardRegistry(shards);

        // Act - query spanning Q1 and Q2
        var result = registry.GetShardsForDateRange(
            new DateTime(2024, 3, 1),
            new DateTime(2024, 5, 1));

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void AddShard_AddsNewShard()
    {
        // Arrange
        var registry = new ShardRegistry();
        var shard = CreateShard("NewShard", ShardTier.Hot, 1);

        // Act
        registry.AddShard(shard);
        var result = registry.GetShard("NewShard");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("NewShard", result.ShardId);
    }

    private static IShardMetadata CreateShard(
        string id,
        ShardTier tier,
        int priority,
        bool isReadOnly = false)
    {
        var builder = new ShardMetadataBuilder()
            .WithId(id)
            .WithName(id)
            .WithTier(tier)
            .WithPriority(priority)
            .WithConnectionString($"Server=localhost;Database={id}");

        if (isReadOnly)
        {
            builder.AsReadOnly();
        }

        return builder.Build();
    }

    private static IShardMetadata CreateShardWithDateRange(
        string id,
        DateTime start,
        DateTime end)
    {
        return new ShardMetadataBuilder()
            .WithId(id)
            .WithName(id)
            .WithDateRange(start, end)
            .WithConnectionString($"Server=localhost;Database={id}")
            .Build();
    }
}
