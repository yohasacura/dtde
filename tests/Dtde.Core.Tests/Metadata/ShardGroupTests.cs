using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;

namespace Dtde.Core.Tests.Metadata;

public class ShardGroupTests
{
    [Fact]
    public void ShardGroup_OrdersShardsByPriority()
    {
        var a = new ShardMetadataBuilder().WithId("a").WithName("a").WithPriority(50).Build();
        var b = new ShardMetadataBuilder().WithId("b").WithName("b").WithPriority(10).Build();
        var c = new ShardMetadataBuilder().WithId("c").WithName("c").WithPriority(30).Build();

        var group = new ShardGroup("g", new[] { a, b, c });

        Assert.Equal(["b", "c", "a"], group.Shards.Select(s => s.ShardId));
    }

    [Fact]
    public void ShardGroup_GetShard_ReturnsByLocalId()
    {
        var a = new ShardMetadataBuilder().WithId("a").WithName("a").Build();
        var b = new ShardMetadataBuilder().WithId("b").WithName("b").Build();

        var group = new ShardGroup("g", new[] { a, b });

        Assert.Same(a, group.GetShard("a"));
        Assert.Same(b, group.GetShard("b"));
        Assert.Null(group.GetShard("missing"));
    }

    [Fact]
    public void ShardGroup_DuplicateLocalIds_Throws()
    {
        var a1 = new ShardMetadataBuilder().WithId("a").WithName("a").Build();
        var a2 = new ShardMetadataBuilder().WithId("a").WithName("a-too").Build();

        var ex = Assert.Throws<ArgumentException>(() => new ShardGroup("g", new[] { a1, a2 }));
        Assert.Contains("'a'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'g'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShardGroupRegistry_PartitionsShardsByGroupName()
    {
        var euDefault = new ShardMetadataBuilder().WithId("EU").WithName("EU").Build();
        var hash0 = new ShardMetadataBuilder().WithId("0").WithGroup("hash8").WithName("0").Build();
        var hash1 = new ShardMetadataBuilder().WithId("1").WithGroup("hash8").WithName("1").Build();

        var registry = new ShardGroupRegistry(new[] { euDefault, hash0, hash1 });

        Assert.Equal(2, registry.Groups.Count);
        Assert.Single(registry.DefaultGroup.Shards);
        Assert.Equal("EU", registry.DefaultGroup.Shards[0].ShardId);

        var hashGroup = registry.FindGroup("hash8")!;
        Assert.Equal(2, hashGroup.Shards.Count);
        Assert.NotNull(hashGroup.GetShard("0"));
        Assert.NotNull(hashGroup.GetShard("1"));
    }

    [Fact]
    public void ShardGroupRegistry_DefaultGroup_AlwaysPresent_EvenIfEmpty()
    {
        var hash0 = new ShardMetadataBuilder().WithId("0").WithGroup("hash8").Build();
        var registry = new ShardGroupRegistry(new[] { hash0 });

        // No shards in the default group, but DefaultGroup is still non-null
        // (its Shards list is empty).
        Assert.NotNull(registry.DefaultGroup);
        Assert.Empty(registry.DefaultGroup.Shards);
    }

    [Fact]
    public void ShardGroupRegistry_FindGroup_UnknownName_ReturnsNull()
    {
        var registry = new ShardGroupRegistry();
        Assert.Null(registry.FindGroup("nope"));
    }

    [Fact]
    public void ShardGroupRegistry_SameLocalIdInDifferentGroups_DoesNotCollide()
    {
        // Both groups happen to use "0" as a local id; the group registry
        // keeps them isolated.
        var hash3_0 = new ShardMetadataBuilder().WithId("0").WithGroup("hash3").Build();
        var hash8_0 = new ShardMetadataBuilder().WithId("0").WithGroup("hash8").Build();

        var registry = new ShardGroupRegistry(new[] { hash3_0, hash8_0 });

        var fromHash3 = registry.FindGroup("hash3")!.GetShard("0");
        var fromHash8 = registry.FindGroup("hash8")!.GetShard("0");

        Assert.Same(hash3_0, fromHash3);
        Assert.Same(hash8_0, fromHash8);
        Assert.NotSame(fromHash3, fromHash8);
    }

    [Fact]
    public void GroupScopedShardRegistry_OnlyExposesItsGroupsShards()
    {
        var hash0 = new ShardMetadataBuilder().WithId("0").WithGroup("hash8").Build();
        var hash1 = new ShardMetadataBuilder().WithId("1").WithGroup("hash8").Build();
        var year2024 = new ShardMetadataBuilder().WithId("2024").WithGroup("years").Build();

        var groupRegistry = new ShardGroupRegistry(new[] { hash0, hash1, year2024 });
        var hash8Scoped = new GroupScopedShardRegistry(groupRegistry.FindGroup("hash8")!);

        var all = hash8Scoped.GetAllShards();
        Assert.Equal(2, all.Count);
        Assert.All(all, s => Assert.Equal("hash8", s.GroupName));

        Assert.NotNull(hash8Scoped.GetShard("0"));
        Assert.Null(hash8Scoped.GetShard("2024")); // Lives in another group.
    }

    [Fact]
    public void ShardMetadata_DefaultGroupName_IsTheRegistryDefault()
    {
        var shard = new ShardMetadataBuilder().WithId("EU").Build();
        Assert.Equal(IShardGroupRegistry.DefaultGroupName, shard.GroupName);
    }

    [Fact]
    public void ShardMetadataBuilder_WithGroup_SetsGroupName()
    {
        var shard = new ShardMetadataBuilder().WithId("0").WithGroup("hash8").Build();
        Assert.Equal("hash8", shard.GroupName);
    }

    [Fact]
    public void ShardMetadataBuilder_WithGroup_RejectsEmptyName()
    {
        var builder = new ShardMetadataBuilder().WithId("0");
        Assert.Throws<ArgumentException>(() => builder.WithGroup(""));
    }

    [Fact]
    public void ToQualifiedId_DefaultGroup_ReturnsLocalId()
    {
        var shard = new ShardMetadataBuilder().WithId("EU").Build();
        Assert.Equal("EU", shard.ToQualifiedId());
    }

    [Fact]
    public void ToQualifiedId_NamedGroup_ReturnsGroupColonColonId()
    {
        var shard = new ShardMetadataBuilder().WithId("0").WithGroup("hash8").Build();
        Assert.Equal("hash8::0", shard.ToQualifiedId());
    }

    [Fact]
    public void ShardRegistry_GetShard_DefaultGroupLocalIdLookupStillWorks()
    {
        var eu = new ShardMetadataBuilder().WithId("EU").Build();
        var hash0 = new ShardMetadataBuilder().WithId("0").WithGroup("hash8").Build();

        var registry = new ShardRegistry(new[] { eu, hash0 });

        // Default group: plain id.
        Assert.Same(eu, registry.GetShard("EU"));
        // Named group: qualified id.
        Assert.Same(hash0, registry.GetShard("hash8::0"));
        // Plain "0" is NOT in the default group, so this should not match.
        Assert.Null(registry.GetShard("0"));
    }
}
