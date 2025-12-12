namespace Dtde.Samples.Combined.Entities;

/// <summary>
/// Represents a financial transaction with date-based sharding.
/// Hot (recent) data is easily accessible while historical data is archived.
/// Sharding configured via fluent API using ShardByDate(TransactionDate).
/// </summary>
public class AccountTransaction
{
    public long Id { get; set; }

    /// <summary>
    /// Transaction date determines the monthly shard.
    /// </summary>
    public DateTime TransactionDate { get; set; }

    public required string AccountNumber { get; set; }
    public required string TransactionType { get; set; } // Deposit, Withdrawal, Transfer
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public required string Currency { get; set; }
    public string? Description { get; set; }
    public string? CounterpartyAccount { get; set; }
    public string? Reference { get; set; }
    public required string Status { get; set; } // Pending, Completed, Failed, Reversed
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
