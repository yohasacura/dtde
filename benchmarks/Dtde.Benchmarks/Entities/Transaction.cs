namespace Dtde.Benchmarks.Entities;

/// <summary>
/// Transaction entity for date-based sharding benchmarks.
/// </summary>
public class Transaction
{
    public long Id { get; set; }
    public string TransactionRef { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Merchant { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Status { get; set; } = "Completed";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Sharded transaction entity - date-based sharding by TransactionDate.
/// </summary>
public class ShardedTransaction
{
    public long Id { get; set; }
    public string TransactionRef { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Merchant { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Status { get; set; } = "Completed";
    public DateTime CreatedAt { get; set; }
}

public enum TransactionType
{
    Credit,
    Debit,
    Transfer,
    Fee,
    Interest,
    Refund
}
