using Dtde.Core.Metadata;
using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Tests.Metadata;

public class ShardMetadataTests
{
    [Fact]
    public void ShardMetadataBuilder_CreatesValidShard()
    {
        var shard = new ShardMetadataBuilder()
            .WithId("TestShard")
            .WithName("Test Shard")
            .WithConnectionString("Server=localhost;Database=Test")
            .WithTier(ShardTier.Hot)
            .WithPriority(1)
            .Build();

        Assert.Equal("TestShard", shard.ShardId);
        Assert.Equal("Test Shard", shard.Name);
        Assert.Equal("Server=localhost;Database=Test", shard.ConnectionString);
        Assert.Equal(ShardTier.Hot, shard.Tier);
        Assert.Equal(1, shard.Priority);
        Assert.False(shard.IsReadOnly);
    }

    [Fact]
    public void ShardMetadataBuilder_WithDateRange_SetsDateRange()
    {
        var start = new DateTime(2024, 1, 1);
        var end = new DateTime(2024, 4, 1);

        var shard = new ShardMetadataBuilder()
            .WithId("Q1-2024")
            .WithName("Q1-2024")
            .WithConnectionString("Server=localhost;Database=Q1")
            .WithDateRange(start, end)
            .Build();

        Assert.NotNull(shard.DateRange);
        Assert.Equal(start, shard.DateRange.Value.Start);
        Assert.Equal(end, shard.DateRange.Value.End);
    }

    [Fact]
    public void ShardMetadataBuilder_WithReadOnly_SetsReadOnly()
    {
        var shard = new ShardMetadataBuilder()
            .WithId("ArchiveShard")
            .WithName("Archive Shard")
            .WithConnectionString("Server=localhost;Database=Archive")
            .AsReadOnly()
            .WithTier(ShardTier.Archive)
            .Build();

        Assert.True(shard.IsReadOnly);
        Assert.Equal(ShardTier.Archive, shard.Tier);
    }

    [Fact]
    public void ShardMetadataBuilder_ThrowsForEmptyShardId()
    {
        Assert.Throws<InvalidOperationException>(() => new ShardMetadataBuilder()
            .WithName("Test")
            .WithConnectionString("Server=localhost")
            .Build());
    }

    [Fact]
    public void ShardMetadataBuilder_WithStorageMode_SetsStorageMode()
    {
        var shard = new ShardMetadataBuilder()
            .WithId("TableShard")
            .WithName("Table Shard")
            .WithStorageMode(ShardStorageMode.Tables)
            .WithTable("Customers_EU")
            .Build();

        Assert.Equal(ShardStorageMode.Tables, shard.StorageMode);
        Assert.Equal("Customers_EU", shard.TableName);
    }

    [Fact]
    public void ShardMetadataBuilder_WithTable_SetsTableAndSchema()
    {
        var shard = new ShardMetadataBuilder()
            .WithId("TableShard")
            .WithName("Table Shard")
            .WithStorageMode(ShardStorageMode.Tables)
            .WithTable("Orders_2024", "archive")
            .Build();

        Assert.Equal("Orders_2024", shard.TableName);
        Assert.Equal("archive", shard.SchemaName);
        Assert.Equal(ShardStorageMode.Tables, shard.StorageMode);
    }

    [Fact]
    public void ShardMetadataBuilder_WithShardKeyValue_SetsKeyValue()
    {
        var shard = new ShardMetadataBuilder()
            .WithId("Customers_EU")
            .WithName("EU Customers")
            .WithStorageMode(ShardStorageMode.Tables)
            .WithTable("Customers_EU")
            .WithShardKeyValue("EU")
            .Build();

        Assert.Equal("EU", shard.ShardKeyValue);
    }

    [Fact]
    public void ForTable_CreatesTableBasedShard()
    {
        var shard = ShardMetadata.ForTable(
            "Customers_EU",
            "Customers_EU",
            shardKeyValue: "EU",
            schemaName: "sales");

        Assert.Equal("Customers_EU", shard.ShardId);
        Assert.Equal("Customers_EU", shard.TableName);
        Assert.Equal("sales", shard.SchemaName);
        Assert.Equal("EU", shard.ShardKeyValue);
        Assert.Equal(ShardStorageMode.Tables, shard.StorageMode);
    }

    [Fact]
    public void ForDatabase_CreatesDatabaseBasedShard()
    {
        var shard = ShardMetadata.ForDatabase(
            "DB_US",
            "US Database",
            "Server=us.db.local;Database=Customers",
            shardKeyValue: "US");

        Assert.Equal("DB_US", shard.ShardId);
        Assert.Equal("US Database", shard.Name);
        Assert.Equal("Server=us.db.local;Database=Customers", shard.ConnectionString);
        Assert.Equal("US", shard.ShardKeyValue);
        Assert.Equal(ShardStorageMode.Databases, shard.StorageMode);
    }

    [Fact]
    public void ShardMetadata_DefaultStorageMode_WhenConnectionString_IsDatabases()
    {
        var shard = new ShardMetadataBuilder()
            .WithId("DefaultShard")
            .WithName("Default Shard")
            .WithConnectionString("Server=localhost")
            .Build();

        Assert.Equal(ShardStorageMode.Databases, shard.StorageMode);
    }

    [Fact]
    public void ShardMetadata_ManualMode_AllowsPreCreatedTables()
    {
        var shard = new ShardMetadataBuilder()
            .WithId("Manual_Orders_Archive")
            .WithName("Archive Orders")
            .WithStorageMode(ShardStorageMode.Manual)
            .WithTable("Orders_Archive_2023", "archive")
            .AsReadOnly()
            .Build();

        Assert.Equal(ShardStorageMode.Manual, shard.StorageMode);
        Assert.Equal("Orders_Archive_2023", shard.TableName);
        Assert.True(shard.IsReadOnly);
    }
}
