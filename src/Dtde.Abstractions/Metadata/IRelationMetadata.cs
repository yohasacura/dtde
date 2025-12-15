namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Rules for temporal validity containment between parent and child entities.
/// </summary>
public enum TemporalContainmentRule
{
    /// <summary>
    /// No temporal containment enforced between parent and child.
    /// </summary>
    None,

    /// <summary>
    /// Child validity period must be contained within parent validity period.
    /// The child cannot be valid outside the parent's validity window.
    /// </summary>
    ChildWithinParent,

    /// <summary>
    /// Child validity period must exactly match parent validity period.
    /// Both start and end dates must be identical.
    /// </summary>
    ExactMatch
}

/// <summary>
/// Types of entity relationships.
/// </summary>
public enum RelationType
{
    /// <summary>
    /// One-to-one relationship.
    /// </summary>
    OneToOne,

    /// <summary>
    /// One-to-many relationship.
    /// </summary>
    OneToMany,

    /// <summary>
    /// Many-to-many relationship.
    /// </summary>
    ManyToMany
}

/// <summary>
/// Metadata describing a relationship between two entities.
/// </summary>
public interface IRelationMetadata
{
    /// <summary>
    /// Gets the parent entity metadata.
    /// </summary>
    IEntityMetadata ParentEntity { get; }

    /// <summary>
    /// Gets the child entity metadata.
    /// </summary>
    IEntityMetadata ChildEntity { get; }

    /// <summary>
    /// Gets the type of relationship.
    /// </summary>
    RelationType RelationType { get; }

    /// <summary>
    /// Gets the parent's key property for the relationship.
    /// </summary>
    IPropertyMetadata ParentKey { get; }

    /// <summary>
    /// Gets the child's foreign key property.
    /// </summary>
    IPropertyMetadata ChildForeignKey { get; }

    /// <summary>
    /// Gets the temporal containment rule for this relationship.
    /// </summary>
    TemporalContainmentRule ContainmentRule { get; }
}
