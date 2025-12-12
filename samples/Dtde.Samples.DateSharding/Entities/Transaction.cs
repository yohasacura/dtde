namespace Dtde.Samples.DateSharding.Entities;

/// <summary>
/// Transaction entity sharded by TransactionDate.
/// Data is partitioned by month for efficient time-range queries.
/// </summary>
/// <example>
/// <code>
/// // Transactions are automatically routed to monthly tables:
/// // - Transactions_2024_01 for January 2024
/// // - Transactions_2024_02 for February 2024
/// // - etc.
/// </code>
/// </example>
public class Transaction
{
    /// <summary>
    /// Unique transaction identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Transaction reference number.
    /// </summary>
    public string TransactionRef { get; set; } = string.Empty;

    /// <summary>
    /// Account number.
    /// </summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>
    /// Transaction date - SHARD KEY.
    /// Used to determine which monthly table stores this transaction.
    /// </summary>
    public DateTime TransactionDate { get; set; }

    /// <summary>
    /// Transaction amount (positive for credits, negative for debits).
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Transaction type.
    /// </summary>
    public TransactionType Type { get; set; }

    /// <summary>
    /// Transaction description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Category for reporting.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Merchant or counterparty name.
    /// </summary>
    public string? Merchant { get; set; }

    /// <summary>
    /// Running balance after this transaction.
    /// </summary>
    public decimal? BalanceAfter { get; set; }

    /// <summary>
    /// When the transaction was created in the system.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Transaction type enumeration.
/// </summary>
public enum TransactionType
{
    Credit,
    Debit,
    Transfer,
    Fee,
    Interest,
    Refund
}
