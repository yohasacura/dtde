using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Metadata describing a relationship between two entities.
/// </summary>
public sealed class RelationMetadata : IRelationMetadata
{
    /// <inheritdoc />
    public IEntityMetadata ParentEntity { get; }

    /// <inheritdoc />
    public IEntityMetadata ChildEntity { get; }

    /// <inheritdoc />
    public RelationType RelationType { get; }

    /// <inheritdoc />
    public IPropertyMetadata ParentKey { get; }

    /// <inheritdoc />
    public IPropertyMetadata ChildForeignKey { get; }

    /// <inheritdoc />
    public TemporalContainmentRule ContainmentRule { get; }

    /// <summary>
    /// Creates a new relation metadata.
    /// </summary>
    /// <param name="parentEntity">The parent entity metadata.</param>
    /// <param name="childEntity">The child entity metadata.</param>
    /// <param name="relationType">The type of relationship.</param>
    /// <param name="parentKey">The parent's key property.</param>
    /// <param name="childForeignKey">The child's foreign key property.</param>
    /// <param name="containmentRule">The temporal containment rule.</param>
    public RelationMetadata(
        IEntityMetadata parentEntity,
        IEntityMetadata childEntity,
        RelationType relationType,
        IPropertyMetadata parentKey,
        IPropertyMetadata childForeignKey,
        TemporalContainmentRule containmentRule = TemporalContainmentRule.None)
    {
        ParentEntity = parentEntity ?? throw new ArgumentNullException(nameof(parentEntity));
        ChildEntity = childEntity ?? throw new ArgumentNullException(nameof(childEntity));
        RelationType = relationType;
        ParentKey = parentKey ?? throw new ArgumentNullException(nameof(parentKey));
        ChildForeignKey = childForeignKey ?? throw new ArgumentNullException(nameof(childForeignKey));
        ContainmentRule = containmentRule;
    }
}
