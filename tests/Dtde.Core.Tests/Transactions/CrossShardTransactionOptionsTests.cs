using Dtde.Abstractions.Transactions;

namespace Dtde.Core.Tests.Transactions;

/// <summary>
/// Tests for <see cref="CrossShardTransactionOptions"/>.
/// </summary>
public class CrossShardTransactionOptionsTests
{
    [Fact(DisplayName = "Default options have expected values")]
    public void Default_HasExpectedValues()
    {
        var options = CrossShardTransactionOptions.Default;

        Assert.Equal(CrossShardTransactionOptions.DefaultTimeout, options.Timeout);
        Assert.Equal(CrossShardTransactionOptions.DefaultIsolationLevel, options.IsolationLevel);
        Assert.True(options.EnableRetry);
        Assert.Equal(3, options.MaxRetryAttempts);
        Assert.True(options.UseExponentialBackoff);
        Assert.Null(options.TransactionName);
        Assert.False(options.EnableRecovery);
    }

    [Fact(DisplayName = "ShortLived preset has quick timeout")]
    public void ShortLived_HasQuickTimeout()
    {
        var options = CrossShardTransactionOptions.ShortLived;

        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
        Assert.Equal(2, options.MaxRetryAttempts);
        Assert.False(options.EnableRecovery);
    }

    [Fact(DisplayName = "LongRunning preset has extended timeout")]
    public void LongRunning_HasExtendedTimeout()
    {
        var options = CrossShardTransactionOptions.LongRunning;

        Assert.Equal(TimeSpan.FromMinutes(5), options.Timeout);
        Assert.Equal(5, options.MaxRetryAttempts);
        Assert.True(options.EnableRecovery);
    }

    [Fact(DisplayName = "Can customize all properties")]
    public void CanCustomizeAllProperties()
    {
        var options = new CrossShardTransactionOptions
        {
            Timeout = TimeSpan.FromMinutes(3),
            IsolationLevel = CrossShardIsolationLevel.Serializable,
            EnableRetry = false,
            MaxRetryAttempts = 5,
            RetryDelay = TimeSpan.FromSeconds(2),
            UseExponentialBackoff = false,
            MaxRetryDelay = TimeSpan.FromSeconds(30),
            TransactionName = "CustomTransaction",
            EnableRecovery = true
        };

        Assert.Equal(TimeSpan.FromMinutes(3), options.Timeout);
        Assert.Equal(CrossShardIsolationLevel.Serializable, options.IsolationLevel);
        Assert.False(options.EnableRetry);
        Assert.Equal(5, options.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(2), options.RetryDelay);
        Assert.False(options.UseExponentialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxRetryDelay);
        Assert.Equal("CustomTransaction", options.TransactionName);
        Assert.True(options.EnableRecovery);
    }

    [Fact(DisplayName = "Static defaults can be modified")]
    public void StaticDefaults_CanBeModified()
    {
        var originalTimeout = CrossShardTransactionOptions.DefaultTimeout;
        var originalIsolation = CrossShardTransactionOptions.DefaultIsolationLevel;

        try
        {
            CrossShardTransactionOptions.DefaultTimeout = TimeSpan.FromMinutes(10);
            CrossShardTransactionOptions.DefaultIsolationLevel = CrossShardIsolationLevel.Snapshot;

            var options = new CrossShardTransactionOptions();

            Assert.Equal(TimeSpan.FromMinutes(10), options.Timeout);
            Assert.Equal(CrossShardIsolationLevel.Snapshot, options.IsolationLevel);
        }
        finally
        {
            // Restore original values
            CrossShardTransactionOptions.DefaultTimeout = originalTimeout;
            CrossShardTransactionOptions.DefaultIsolationLevel = originalIsolation;
        }
    }
}
