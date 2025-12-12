using Dtde.Samples.Combined.Data;
using Dtde.Samples.Combined.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.Combined.Controllers;

/// <summary>
/// API controller demonstrating date-based sharding for transactions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TransactionsController : ControllerBase
{
    private readonly CombinedDbContext _context;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        CombinedDbContext context,
        ILogger<TransactionsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get transactions for a specific month (single shard access).
    /// </summary>
    [HttpGet("month/{year}/{month}")]
    public async Task<ActionResult<IEnumerable<TransactionDto>>> GetTransactionsByMonth(
        int year, int month,
        [FromQuery] string? accountNumber,
        [FromQuery] string? transactionType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1);

        _logger.LogInformation(
            "Fetching transactions for {Year}-{Month:D2} (monthly shard txn-{Year:D4}-{Month:D2})",
            year, month, year, month);

        var query = _context.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate < endDate);

        if (!string.IsNullOrEmpty(accountNumber))
            query = query.Where(t => t.AccountNumber == accountNumber);

        if (!string.IsNullOrEmpty(transactionType))
            query = query.Where(t => t.TransactionType == transactionType);

        var transactions = await query
            .OrderByDescending(t => t.TransactionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto
            {
                Id = t.Id,
                AccountNumber = t.AccountNumber,
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                Currency = t.Currency,
                TransactionDate = t.TransactionDate,
                Description = t.Description,
                Status = t.Status
            })
            .ToListAsync();

        return Ok(transactions);
    }

    /// <summary>
    /// Get transactions for an account within a date range (may span multiple shards).
    /// </summary>
    [HttpGet("account/{accountNumber}")]
    public async Task<ActionResult<TransactionRangeResult>> GetAccountTransactions(
        string accountNumber,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        // Calculate which monthly shards will be queried
        var shardsQueried = new List<string>();
        var current = new DateTime(fromDate.Year, fromDate.Month, 1);
        while (current <= toDate)
        {
            shardsQueried.Add($"txn-{current:yyyy-MM}");
            current = current.AddMonths(1);
        }

        _logger.LogInformation(
            "Fetching transactions for account {AccountNumber} from {FromDate} to {ToDate} (spanning {ShardCount} monthly shards)",
            accountNumber, fromDate, toDate, shardsQueried.Count);

        var query = _context.Transactions
            .Where(t => t.AccountNumber == accountNumber &&
                       t.TransactionDate >= fromDate &&
                       t.TransactionDate <= toDate);

        var totalCount = await query.CountAsync();

        var transactions = await query
            .OrderByDescending(t => t.TransactionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto
            {
                Id = t.Id,
                AccountNumber = t.AccountNumber,
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                BalanceBefore = t.BalanceBefore,
                BalanceAfter = t.BalanceAfter,
                Currency = t.Currency,
                TransactionDate = t.TransactionDate,
                Description = t.Description,
                Reference = t.Reference,
                Status = t.Status
            })
            .ToListAsync();

        return Ok(new TransactionRangeResult
        {
            AccountNumber = accountNumber,
            FromDate = fromDate,
            ToDate = toDate,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            ShardsQueried = shardsQueried,
            Transactions = transactions
        });
    }

    /// <summary>
    /// Create a new transaction.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TransactionDto>> CreateTransaction(CreateTransactionRequest request)
    {
        var transactionDate = request.TransactionDate ?? DateTime.UtcNow;
        var shardId = $"txn-{transactionDate:yyyy-MM}";

        _logger.LogInformation(
            "Creating transaction for account {AccountNumber} (routed to shard {ShardId})",
            request.AccountNumber, shardId);

        // Get current account balance
        var account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.AccountNumber == request.AccountNumber);

        if (account == null)
            return NotFound($"Account {request.AccountNumber} not found");

        var balanceBefore = account.Balance;
        var balanceAfter = request.TransactionType switch
        {
            "Deposit" => balanceBefore + request.Amount,
            "Withdrawal" => balanceBefore - request.Amount,
            "Transfer" => balanceBefore - request.Amount,
            _ => balanceBefore
        };

        var transaction = new AccountTransaction
        {
            TransactionDate = transactionDate,
            AccountNumber = request.AccountNumber,
            TransactionType = request.TransactionType,
            Amount = request.Amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Currency = request.Currency ?? account.Currency,
            Description = request.Description,
            CounterpartyAccount = request.CounterpartyAccount,
            Reference = Guid.NewGuid().ToString("N")[..12].ToUpper(),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _context.Transactions.Add(transaction);

        // Update account balance
        account.Balance = balanceAfter;

        // Process immediately for demo purposes
        transaction.Status = "Completed";
        transaction.ProcessedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetAccountTransactions),
            new { accountNumber = transaction.AccountNumber },
            new TransactionDto
            {
                Id = transaction.Id,
                AccountNumber = transaction.AccountNumber,
                TransactionType = transaction.TransactionType,
                Amount = transaction.Amount,
                BalanceBefore = transaction.BalanceBefore,
                BalanceAfter = transaction.BalanceAfter,
                Currency = transaction.Currency,
                TransactionDate = transaction.TransactionDate,
                Description = transaction.Description,
                Reference = transaction.Reference,
                Status = transaction.Status
            });
    }

    /// <summary>
    /// Get monthly transaction statistics.
    /// </summary>
    [HttpGet("stats/monthly")]
    public async Task<ActionResult<IEnumerable<MonthlyStats>>> GetMonthlyStats(
        [FromQuery] int months = 6)
    {
        var since = DateTime.UtcNow.AddMonths(-months);

        _logger.LogInformation(
            "Fetching monthly statistics for last {Months} months (aggregating across shards)",
            months);

        var stats = await _context.Transactions
            .Where(t => t.TransactionDate >= since)
            .GroupBy(t => new { t.TransactionDate.Year, t.TransactionDate.Month })
            .Select(g => new MonthlyStats
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                TransactionCount = g.Count(),
                TotalDeposits = g.Where(t => t.TransactionType == "Deposit").Sum(t => t.Amount),
                TotalWithdrawals = g.Where(t => t.TransactionType == "Withdrawal").Sum(t => t.Amount),
                ShardId = $"txn-{g.Key.Year:D4}-{g.Key.Month:D2}"
            })
            .OrderByDescending(s => s.Year)
            .ThenByDescending(s => s.Month)
            .ToListAsync();

        return Ok(stats);
    }
}

// DTOs
public record TransactionDto
{
    public long Id { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string TransactionType { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal BalanceBefore { get; init; }
    public decimal BalanceAfter { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTime TransactionDate { get; init; }
    public string? Description { get; init; }
    public string? Reference { get; init; }
    public string Status { get; init; } = string.Empty;
}

public record CreateTransactionRequest
{
    public required string AccountNumber { get; init; }
    public required string TransactionType { get; init; }
    public required decimal Amount { get; init; }
    public string? Currency { get; init; }
    public string? Description { get; init; }
    public string? CounterpartyAccount { get; init; }
    public DateTime? TransactionDate { get; init; }
}

public record TransactionRangeResult
{
    public string AccountNumber { get; init; } = string.Empty;
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public IEnumerable<string> ShardsQueried { get; init; } = [];
    public IEnumerable<TransactionDto> Transactions { get; init; } = [];
}

public record MonthlyStats
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int TransactionCount { get; init; }
    public decimal TotalDeposits { get; init; }
    public decimal TotalWithdrawals { get; init; }
    public string ShardId { get; init; } = string.Empty;
}
