using Dtde.Samples.DateSharding.Data;
using Dtde.Samples.DateSharding.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.DateSharding.Controllers;

/// <summary>
/// API controller demonstrating date-based sharding queries.
/// Shows efficient time-range queries that target specific shards.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TransactionsController : ControllerBase
{
    private readonly DateShardingDbContext _context;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        DateShardingDbContext context,
        ILogger<TransactionsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get transactions for a date range.
    /// DTDE automatically routes to the relevant monthly shards.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TransactionListResponse>> GetTransactions(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] string? accountNumber,
        [FromQuery] TransactionType? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        _logger.LogInformation(
            "Querying transactions from {FromDate} to {ToDate}",
            fromDate, toDate);

        // Date range filter enables DTDE to query only relevant monthly shards
        var query = _context.Transactions
            .Where(t => t.TransactionDate >= fromDate && t.TransactionDate <= toDate);

        if (!string.IsNullOrEmpty(accountNumber))
        {
            query = query.Where(t => t.AccountNumber == accountNumber);
        }

        if (type.HasValue)
        {
            query = query.Where(t => t.Type == type.Value);
        }

        var totalCount = await query.CountAsync();

        var transactions = await query
            .OrderByDescending(t => t.TransactionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto
            {
                Id = t.Id,
                TransactionRef = t.TransactionRef,
                AccountNumber = t.AccountNumber,
                TransactionDate = t.TransactionDate,
                Amount = t.Amount,
                Type = t.Type.ToString(),
                Description = t.Description,
                Category = t.Category,
                Merchant = t.Merchant
            })
            .ToListAsync();

        return Ok(new TransactionListResponse
        {
            Transactions = transactions,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// Get transactions for a specific month (single shard query).
    /// Most efficient query as it targets exactly one shard.
    /// </summary>
    [HttpGet("month/{year}/{month}")]
    public async Task<ActionResult<IEnumerable<TransactionDto>>> GetTransactionsByMonth(
        int year,
        int month,
        [FromQuery] string? accountNumber)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1);

        _logger.LogInformation(
            "Querying transactions for {Year}-{Month:D2} (single shard)",
            year, month);

        var query = _context.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate < endDate);

        if (!string.IsNullOrEmpty(accountNumber))
        {
            query = query.Where(t => t.AccountNumber == accountNumber);
        }

        var transactions = await query
            .OrderByDescending(t => t.TransactionDate)
            .Take(1000)
            .Select(t => new TransactionDto
            {
                Id = t.Id,
                TransactionRef = t.TransactionRef,
                AccountNumber = t.AccountNumber,
                TransactionDate = t.TransactionDate,
                Amount = t.Amount,
                Type = t.Type.ToString(),
                Description = t.Description,
                Category = t.Category,
                Merchant = t.Merchant
            })
            .ToListAsync();

        return Ok(transactions);
    }

    /// <summary>
    /// Get account statement for a period.
    /// </summary>
    [HttpGet("statement/{accountNumber}")]
    public async Task<ActionResult<AccountStatement>> GetStatement(
        string accountNumber,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        _logger.LogInformation(
            "Generating statement for account {Account} from {From} to {To}",
            accountNumber, fromDate, toDate);

        var transactions = await _context.Transactions
            .Where(t => t.AccountNumber == accountNumber)
            .Where(t => t.TransactionDate >= fromDate && t.TransactionDate <= toDate)
            .OrderBy(t => t.TransactionDate)
            .ToListAsync();

        var statement = new AccountStatement
        {
            AccountNumber = accountNumber,
            FromDate = fromDate,
            ToDate = toDate,
            OpeningBalance = transactions.FirstOrDefault()?.BalanceAfter - 
                             transactions.FirstOrDefault()?.Amount ?? 0,
            ClosingBalance = transactions.LastOrDefault()?.BalanceAfter ?? 0,
            TotalCredits = transactions.Where(t => t.Amount > 0).Sum(t => t.Amount),
            TotalDebits = transactions.Where(t => t.Amount < 0).Sum(t => Math.Abs(t.Amount)),
            TransactionCount = transactions.Count,
            Transactions = transactions.Select(t => new TransactionDto
            {
                Id = t.Id,
                TransactionRef = t.TransactionRef,
                AccountNumber = t.AccountNumber,
                TransactionDate = t.TransactionDate,
                Amount = t.Amount,
                Type = t.Type.ToString(),
                Description = t.Description,
                Category = t.Category,
                Merchant = t.Merchant
            }).ToList()
        };

        return Ok(statement);
    }

    /// <summary>
    /// Create a new transaction.
    /// DTDE routes to the correct monthly shard based on TransactionDate.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TransactionDto>> CreateTransaction(CreateTransactionRequest request)
    {
        var transactionDate = request.TransactionDate ?? DateTime.UtcNow;

        _logger.LogInformation(
            "Creating transaction for account {Account} on {Date}",
            request.AccountNumber, transactionDate);

        var transaction = new Transaction
        {
            TransactionRef = GenerateTransactionRef(),
            AccountNumber = request.AccountNumber,
            TransactionDate = transactionDate,
            Amount = request.Type == TransactionType.Debit ? -Math.Abs(request.Amount) : Math.Abs(request.Amount),
            Type = request.Type,
            Description = request.Description,
            Category = request.Category,
            Merchant = request.Merchant,
            CreatedAt = DateTime.UtcNow
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetTransactionsByMonth),
            new { year = transactionDate.Year, month = transactionDate.Month },
            new TransactionDto
            {
                Id = transaction.Id,
                TransactionRef = transaction.TransactionRef,
                AccountNumber = transaction.AccountNumber,
                TransactionDate = transaction.TransactionDate,
                Amount = transaction.Amount,
                Type = transaction.Type.ToString(),
                Description = transaction.Description,
                Category = transaction.Category,
                Merchant = transaction.Merchant
            });
    }

    /// <summary>
    /// Get monthly summary statistics.
    /// </summary>
    [HttpGet("stats/monthly")]
    public async Task<ActionResult<IEnumerable<MonthlySummary>>> GetMonthlyStats(
        [FromQuery] int year,
        [FromQuery] string? accountNumber)
    {
        var startDate = new DateTime(year, 1, 1);
        var endDate = startDate.AddYears(1);

        var query = _context.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate < endDate);

        if (!string.IsNullOrEmpty(accountNumber))
        {
            query = query.Where(t => t.AccountNumber == accountNumber);
        }

        // Group by month (queries all relevant monthly shards)
        var stats = await query
            .GroupBy(t => new { t.TransactionDate.Year, t.TransactionDate.Month })
            .Select(g => new MonthlySummary
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                TransactionCount = g.Count(),
                TotalCredits = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                TotalDebits = g.Where(t => t.Amount < 0).Sum(t => Math.Abs(t.Amount)),
                NetChange = g.Sum(t => t.Amount)
            })
            .OrderBy(s => s.Year)
            .ThenBy(s => s.Month)
            .ToListAsync();

        return Ok(stats);
    }

    private static string GenerateTransactionRef() =>
        $"TXN-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..6].ToUpper()}";
}

// DTOs
public record TransactionDto
{
    public long Id { get; init; }
    public string TransactionRef { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public DateTime TransactionDate { get; init; }
    public decimal Amount { get; init; }
    public string Type { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? Merchant { get; init; }
}

public record TransactionListResponse
{
    public IEnumerable<TransactionDto> Transactions { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}

public record CreateTransactionRequest
{
    public required string AccountNumber { get; init; }
    public decimal Amount { get; init; }
    public TransactionType Type { get; init; }
    public DateTime? TransactionDate { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? Merchant { get; init; }
}

public record AccountStatement
{
    public string AccountNumber { get; init; } = string.Empty;
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public decimal OpeningBalance { get; init; }
    public decimal ClosingBalance { get; init; }
    public decimal TotalCredits { get; init; }
    public decimal TotalDebits { get; init; }
    public int TransactionCount { get; init; }
    public IEnumerable<TransactionDto> Transactions { get; init; } = [];
}

public record MonthlySummary
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int TransactionCount { get; init; }
    public decimal TotalCredits { get; init; }
    public decimal TotalDebits { get; init; }
    public decimal NetChange { get; init; }
}
