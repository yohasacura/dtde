namespace Dtde.Abstractions.Exceptions;

/// <summary>
/// Exception thrown when a shard cannot be found for a given criteria.
/// </summary>
public class ShardNotFoundException : DtdeException
{
    /// <summary>
    /// Creates a new ShardNotFoundException with a default message.
    /// </summary>
    public ShardNotFoundException() : base("The requested shard was not found.")
    {
    }

    /// <summary>
    /// Creates a new ShardNotFoundException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ShardNotFoundException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new ShardNotFoundException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ShardNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base exception for all DTDE-specific exceptions.
/// </summary>
public class DtdeException : Exception
{
    /// <summary>
    /// Creates a new DtdeException with a default message.
    /// </summary>
    public DtdeException() : base("A DTDE operation failed.")
    {
    }

    /// <summary>
    /// Creates a new DtdeException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public DtdeException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new DtdeException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DtdeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when metadata configuration is invalid.
/// </summary>
public class MetadataConfigurationException : DtdeException
{
    /// <summary>
    /// Creates a new MetadataConfigurationException with a default message.
    /// </summary>
    public MetadataConfigurationException() : base("Metadata configuration is invalid.")
    {
    }

    /// <summary>
    /// Creates a new MetadataConfigurationException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MetadataConfigurationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new MetadataConfigurationException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public MetadataConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a temporal operation fails.
/// </summary>
public class TemporalOperationException : DtdeException
{
    /// <summary>
    /// Creates a new TemporalOperationException with a default message.
    /// </summary>
    public TemporalOperationException() : base("A temporal operation failed.")
    {
    }

    /// <summary>
    /// Creates a new TemporalOperationException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TemporalOperationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new TemporalOperationException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TemporalOperationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a version conflict is detected.
/// </summary>
public class VersionConflictException : DtdeException
{
    /// <summary>
    /// Gets the entity type involved in the conflict.
    /// </summary>
    public Type? EntityType { get; }

    /// <summary>
    /// Gets the entity key involved in the conflict.
    /// </summary>
    public object? EntityKey { get; }

    /// <summary>
    /// Creates a new VersionConflictException with a default message.
    /// </summary>
    public VersionConflictException() : base("A version conflict was detected.")
    {
    }

    /// <summary>
    /// Creates a new VersionConflictException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public VersionConflictException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new VersionConflictException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public VersionConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new VersionConflictException with entity details.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="entityType">The entity type involved.</param>
    /// <param name="entityKey">The entity key involved.</param>
    public VersionConflictException(string message, Type entityType, object? entityKey) : base(message)
    {
        EntityType = entityType;
        EntityKey = entityKey;
    }
}
