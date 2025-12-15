using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;

namespace Dtde.Core.Tests.Metadata;

/// <summary>
/// Tests for <see cref="RelationMetadata"/>.
/// </summary>
public class RelationMetadataTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesRelation()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.OneToMany,
            parentKey,
            childForeignKey);

        Assert.Same(parentMetadata, relation.ParentEntity);
        Assert.Same(childMetadata, relation.ChildEntity);
        Assert.Equal(RelationType.OneToMany, relation.RelationType);
        Assert.Same(parentKey, relation.ParentKey);
        Assert.Same(childForeignKey, relation.ChildForeignKey);
        Assert.Equal(TemporalContainmentRule.None, relation.ContainmentRule);
    }

    [Fact]
    public void Constructor_ThrowsForNullParentEntity()
    {
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        Assert.Throws<ArgumentNullException>(() =>
            new RelationMetadata(null!, childMetadata, RelationType.OneToMany, parentKey, childForeignKey));
    }

    [Fact]
    public void Constructor_ThrowsForNullChildEntity()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        Assert.Throws<ArgumentNullException>(() =>
            new RelationMetadata(parentMetadata, null!, RelationType.OneToMany, parentKey, childForeignKey));
    }

    [Fact]
    public void Constructor_ThrowsForNullParentKey()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        Assert.Throws<ArgumentNullException>(() =>
            new RelationMetadata(parentMetadata, childMetadata, RelationType.OneToMany, null!, childForeignKey));
    }

    [Fact]
    public void Constructor_ThrowsForNullChildForeignKey()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");

        Assert.Throws<ArgumentNullException>(() =>
            new RelationMetadata(parentMetadata, childMetadata, RelationType.OneToMany, parentKey, null!));
    }

    [Fact]
    public void Constructor_WithTemporalContainmentRule_SetsRule()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.OneToMany,
            parentKey,
            childForeignKey,
            TemporalContainmentRule.ChildWithinParent);

        Assert.Equal(TemporalContainmentRule.ChildWithinParent, relation.ContainmentRule);
    }

    [Fact]
    public void Constructor_WithLooseContainment_SetsRule()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.OneToMany,
            parentKey,
            childForeignKey,
            TemporalContainmentRule.None);

        Assert.Equal(TemporalContainmentRule.None, relation.ContainmentRule);
    }

    [Fact]
    public void Constructor_WithOneToOneRelationType_SetsType()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.OneToOne,
            parentKey,
            childForeignKey);

        Assert.Equal(RelationType.OneToOne, relation.RelationType);
    }

    [Fact]
    public void Constructor_WithManyToManyRelationType_SetsType()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.ManyToMany,
            parentKey,
            childForeignKey);

        Assert.Equal(RelationType.ManyToMany, relation.RelationType);
    }

    [Fact]
    public void RelationType_DefaultContainmentRule_IsNone()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.OneToMany,
            parentKey,
            childForeignKey);

        Assert.Equal(TemporalContainmentRule.None, relation.ContainmentRule);
    }

    [Fact]
    public void ParentEntity_ReturnsCorrectMetadata()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.OneToMany,
            parentKey,
            childForeignKey);

        Assert.Equal(typeof(ParentEntity), relation.ParentEntity.ClrType);
    }

    [Fact]
    public void ChildEntity_ReturnsCorrectMetadata()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.OneToMany,
            parentKey,
            childForeignKey);

        Assert.Equal(typeof(ChildEntity), relation.ChildEntity.ClrType);
    }

    [Fact]
    public void ParentKey_ReturnsCorrectPropertyMetadata()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.OneToMany,
            parentKey,
            childForeignKey);

        Assert.Equal("Id", relation.ParentKey.PropertyName);
    }

    [Fact]
    public void ChildForeignKey_ReturnsCorrectPropertyMetadata()
    {
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        var parentKey = CreatePropertyMetadata<ParentEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<ChildEntity>("ParentId");

        var relation = new RelationMetadata(
            parentMetadata,
            childMetadata,
            RelationType.OneToMany,
            parentKey,
            childForeignKey);

        Assert.Equal("ParentId", relation.ChildForeignKey.PropertyName);
    }

    [Fact]
    public void Relation_WithSameParentAndChild_IsAllowed()
    {
        var entityMetadata = CreateEntityMetadata<SelfReferencingEntity>();
        var parentKey = CreatePropertyMetadata<SelfReferencingEntity>("Id");
        var childForeignKey = CreatePropertyMetadata<SelfReferencingEntity>("ParentId");

        var relation = new RelationMetadata(
            entityMetadata,
            entityMetadata,
            RelationType.OneToMany,
            parentKey,
            childForeignKey);

        Assert.Same(relation.ParentEntity, relation.ChildEntity);
    }

    private static IEntityMetadata CreateEntityMetadata<T>() where T : class
    {
        return new TestEntityMetadata(typeof(T));
    }

    private static IPropertyMetadata CreatePropertyMetadata<T>(string propertyName)
    {
        var propInfo = typeof(T).GetProperty(propertyName)!;
        return new PropertyMetadata(propInfo);
    }

    private sealed class TestEntityMetadata : IEntityMetadata
    {
        public TestEntityMetadata(Type clrType)
        {
            ClrType = clrType;
        }

        public Type ClrType { get; }
        public string TableName => ClrType.Name;
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey => null;
        public IValidityConfiguration? Validity => null;
        public IShardingConfiguration? Sharding => null;
        public bool IsTemporal => false;
        public bool IsSharded => false;
    }

    private class ParentEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class ChildEntity
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    private class SelfReferencingEntity
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
