using Dtde.Abstractions.Exceptions;

namespace Dtde.Abstractions.Tests;

/// <summary>
/// Tests for DTDE custom exceptions.
/// </summary>
public class DtdeExceptionsTests
{
    [Fact]
    public void DtdeException_DefaultConstructor_SetsDefaultMessage()
    {
        var exception = new DtdeException();

        Assert.Equal("A DTDE operation failed.", exception.Message);
    }

    [Fact]
    public void DtdeException_WithMessage_SetsMessage()
    {
        var exception = new DtdeException("Custom error message");

        Assert.Equal("Custom error message", exception.Message);
    }

    [Fact]
    public void DtdeException_WithMessageAndInnerException_SetsProperties()
    {
        var innerException = new InvalidOperationException("Inner error");
        var exception = new DtdeException("Outer error", innerException);

        Assert.Equal("Outer error", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void DtdeException_IsExceptionBaseType()
    {
        var exception = new DtdeException();

        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void ShardNotFoundException_DefaultConstructor_SetsDefaultMessage()
    {
        var exception = new ShardNotFoundException();

        Assert.Equal("The requested shard was not found.", exception.Message);
    }

    [Fact]
    public void ShardNotFoundException_WithMessage_SetsMessage()
    {
        var exception = new ShardNotFoundException("Shard 'EU' not found");

        Assert.Equal("Shard 'EU' not found", exception.Message);
    }

    [Fact]
    public void ShardNotFoundException_WithMessageAndInnerException_SetsProperties()
    {
        var innerException = new ArgumentException("Invalid shard key");
        var exception = new ShardNotFoundException("Shard lookup failed", innerException);

        Assert.Equal("Shard lookup failed", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void ShardNotFoundException_InheritsFromDtdeException()
    {
        var exception = new ShardNotFoundException();

        Assert.IsAssignableFrom<DtdeException>(exception);
    }

    [Fact]
    public void MetadataConfigurationException_DefaultConstructor_SetsDefaultMessage()
    {
        var exception = new MetadataConfigurationException();

        Assert.Equal("Metadata configuration is invalid.", exception.Message);
    }

    [Fact]
    public void MetadataConfigurationException_WithMessage_SetsMessage()
    {
        var exception = new MetadataConfigurationException("Entity 'Order' has no primary key");

        Assert.Equal("Entity 'Order' has no primary key", exception.Message);
    }

    [Fact]
    public void MetadataConfigurationException_WithMessageAndInnerException_SetsProperties()
    {
        var innerException = new ArgumentNullException("metadata");
        var exception = new MetadataConfigurationException("Configuration failed", innerException);

        Assert.Equal("Configuration failed", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void MetadataConfigurationException_InheritsFromDtdeException()
    {
        var exception = new MetadataConfigurationException();

        Assert.IsAssignableFrom<DtdeException>(exception);
    }

    [Fact]
    public void TemporalOperationException_DefaultConstructor_SetsDefaultMessage()
    {
        var exception = new TemporalOperationException();

        Assert.Equal("A temporal operation failed.", exception.Message);
    }

    [Fact]
    public void TemporalOperationException_WithMessage_SetsMessage()
    {
        var exception = new TemporalOperationException("Cannot create version with past date");

        Assert.Equal("Cannot create version with past date", exception.Message);
    }

    [Fact]
    public void TemporalOperationException_WithMessageAndInnerException_SetsProperties()
    {
        var innerException = new ArgumentOutOfRangeException("effectiveDate");
        var exception = new TemporalOperationException("Temporal operation failed", innerException);

        Assert.Equal("Temporal operation failed", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void TemporalOperationException_InheritsFromDtdeException()
    {
        var exception = new TemporalOperationException();

        Assert.IsAssignableFrom<DtdeException>(exception);
    }

    [Fact]
    public void VersionConflictException_DefaultConstructor_SetsDefaultMessage()
    {
        var exception = new VersionConflictException();

        Assert.Equal("A version conflict was detected.", exception.Message);
        Assert.Null(exception.EntityType);
        Assert.Null(exception.EntityKey);
    }

    [Fact]
    public void VersionConflictException_WithMessage_SetsMessage()
    {
        var exception = new VersionConflictException("Concurrent modification detected");

        Assert.Equal("Concurrent modification detected", exception.Message);
    }

    [Fact]
    public void VersionConflictException_WithMessageAndInnerException_SetsProperties()
    {
        var innerException = new InvalidOperationException("Conflict");
        var exception = new VersionConflictException("Version conflict", innerException);

        Assert.Equal("Version conflict", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void VersionConflictException_WithEntityDetails_SetsAllProperties()
    {
        var exception = new VersionConflictException(
            "Version conflict for entity Order with key 123",
            typeof(TestOrder),
            123);

        Assert.Equal("Version conflict for entity Order with key 123", exception.Message);
        Assert.Equal(typeof(TestOrder), exception.EntityType);
        Assert.Equal(123, exception.EntityKey);
    }

    [Fact]
    public void VersionConflictException_WithEntityDetails_AllowsNullKey()
    {
        var exception = new VersionConflictException(
            "Conflict detected",
            typeof(TestOrder),
            null);

        Assert.Equal(typeof(TestOrder), exception.EntityType);
        Assert.Null(exception.EntityKey);
    }

    [Fact]
    public void VersionConflictException_InheritsFromDtdeException()
    {
        var exception = new VersionConflictException();

        Assert.IsAssignableFrom<DtdeException>(exception);
    }

    [Fact]
    public void AllExceptions_CanBeCaughtAsDtdeException()
    {
        var exceptions = new DtdeException[]
        {
            new ShardNotFoundException(),
            new MetadataConfigurationException(),
            new TemporalOperationException(),
            new VersionConflictException()
        };

        foreach (var exception in exceptions)
        {
            try
            {
                throw exception;
            }
            catch (DtdeException caught)
            {
                Assert.NotNull(caught);
            }
        }
    }

    [Fact]
    public void AllExceptions_CanBeCaughtAsException()
    {
        var exceptions = new Exception[]
        {
            new DtdeException(),
            new ShardNotFoundException(),
            new MetadataConfigurationException(),
            new TemporalOperationException(),
            new VersionConflictException()
        };

        foreach (var exception in exceptions)
        {
            try
            {
                throw exception;
            }
            catch (Exception caught)
            {
                Assert.NotNull(caught);
            }
        }
    }

    [Fact]
    public void ShardNotFoundException_CanBeUsedWithTryCatch()
    {
        string shardId = "NonExistent";
        ShardNotFoundException? caughtException = null;

        try
        {
            throw new ShardNotFoundException($"Shard '{shardId}' not found");
        }
        catch (ShardNotFoundException ex)
        {
            caughtException = ex;
        }

        Assert.NotNull(caughtException);
        Assert.Contains(shardId, caughtException.Message);
    }

    [Fact]
    public void VersionConflictException_WithGuidKey_WorksCorrectly()
    {
        var key = Guid.NewGuid();
        var exception = new VersionConflictException(
            $"Conflict on entity with key {key}",
            typeof(TestOrder),
            key);

        Assert.Equal(key, exception.EntityKey);
    }

    [Fact]
    public void VersionConflictException_WithCompositeKey_WorksCorrectly()
    {
        var compositeKey = new { OrderId = 1, ItemId = 42 };
        var exception = new VersionConflictException(
            "Conflict on composite key",
            typeof(TestOrder),
            compositeKey);

        Assert.NotNull(exception.EntityKey);
        var key = exception.EntityKey;
        Assert.Equal(1, key!.GetType().GetProperty("OrderId")!.GetValue(key));
        Assert.Equal(42, key.GetType().GetProperty("ItemId")!.GetValue(key));
    }

    private class TestOrder
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
    }
}
