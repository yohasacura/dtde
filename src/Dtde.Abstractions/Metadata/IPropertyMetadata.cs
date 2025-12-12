using System.Reflection;

namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Metadata about a single property on an entity.
/// Provides property access information including compiled getters and setters for fast access.
/// </summary>
public interface IPropertyMetadata
{
    /// <summary>
    /// Gets the name of the property as defined in the CLR type.
    /// </summary>
    string PropertyName { get; }

    /// <summary>
    /// Gets the CLR type of the property.
    /// </summary>
    Type PropertyType { get; }

    /// <summary>
    /// Gets the database column name mapped to this property.
    /// </summary>
    string ColumnName { get; }

    /// <summary>
    /// Gets the reflection PropertyInfo for this property.
    /// </summary>
    PropertyInfo PropertyInfo { get; }

    /// <summary>
    /// Gets the value of this property from an entity instance.
    /// </summary>
    /// <param name="entity">The entity instance to read from.</param>
    /// <returns>The property value.</returns>
    object? GetValue(object entity);

    /// <summary>
    /// Sets the value of this property on an entity instance.
    /// </summary>
    /// <param name="entity">The entity instance to modify.</param>
    /// <param name="value">The value to set.</param>
    void SetValue(object entity, object? value);
}
