using Dtde.Samples.Combined.Data;
using Dtde.Samples.Combined.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.Combined.Controllers;

/// <summary>
/// API controller demonstrating property-based (regional) sharding with temporal versioning.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly CombinedDbContext _context;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(
        CombinedDbContext context,
        ILogger<AccountsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get accounts by region (single shard access).
    /// </summary>
    [HttpGet("region/{region}")]
    public async Task<ActionResult<IEnumerable<AccountDto>>> GetAccountsByRegion(
        string region,
        [FromQuery] string? accountType,
        [FromQuery] string? status)
    {
        _logger.LogInformation(
            "Fetching accounts for region {Region} (regional database shard)",
            region);

        var query = _context.Accounts.Where(a => a.Region == region.ToUpper());

        if (!string.IsNullOrEmpty(accountType))
            query = query.Where(a => a.AccountType == accountType);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);

        var accounts = await query
            .OrderByDescending(a => a.OpenedAt)
            .Select(a => new AccountDto
            {
                Id = a.Id,
                AccountNumber = a.AccountNumber,
                AccountType = a.AccountType,
                Region = a.Region,
                Currency = a.Currency,
                Balance = a.Balance,
                Status = a.Status,
                OpenedAt = a.OpenedAt
            })
            .ToListAsync();

        return Ok(accounts);
    }

    /// <summary>
    /// Get a specific account.
    /// </summary>
    [HttpGet("{accountNumber}")]
    public async Task<ActionResult<AccountDetailDto>> GetAccount(string accountNumber)
    {
        var account = await _context.Accounts
            .Where(a => a.AccountNumber == accountNumber)
            .FirstOrDefaultAsync();

        if (account == null)
            return NotFound();

        // Get recent transactions (cross-shard query to transaction shards)
        var recentTransactions = await _context.Transactions
            .Where(t => t.AccountNumber == accountNumber)
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .Select(t => new TransactionSummaryDto
            {
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                TransactionDate = t.TransactionDate,
                Status = t.Status
            })
            .ToListAsync();

        return Ok(new AccountDetailDto
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            AccountType = account.AccountType,
            Region = account.Region,
            Currency = account.Currency,
            Balance = account.Balance,
            Status = account.Status,
            HolderId = account.HolderId,
            OpenedAt = account.OpenedAt,
            ValidFrom = account.ValidFrom,
            ValidTo = account.ValidTo,
            RecentTransactions = recentTransactions
        });
    }

    /// <summary>
    /// Create a new account.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AccountDto>> CreateAccount(CreateAccountRequest request)
    {
        _logger.LogInformation(
            "Creating account in region {Region} (routed to regional shard)",
            request.Region);

        var account = new Account
        {
            Region = request.Region.ToUpper(),
            AccountNumber = GenerateAccountNumber(),
            AccountType = request.AccountType,
            Currency = request.Currency,
            Balance = request.InitialDeposit ?? 0,
            HolderId = request.HolderId,
            OpenedAt = DateTime.UtcNow,
            Status = "Active",
            ValidFrom = DateTime.UtcNow
        };

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        // Create initial deposit transaction if specified
        if (request.InitialDeposit.HasValue && request.InitialDeposit > 0)
        {
            var transaction = new AccountTransaction
            {
                TransactionDate = DateTime.UtcNow,
                AccountNumber = account.AccountNumber,
                TransactionType = "Deposit",
                Amount = request.InitialDeposit.Value,
                BalanceBefore = 0,
                BalanceAfter = request.InitialDeposit.Value,
                Currency = request.Currency,
                Description = "Initial deposit",
                Status = "Completed",
                CreatedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetAccount), new { accountNumber = account.AccountNumber }, new AccountDto
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            AccountType = account.AccountType,
            Region = account.Region,
            Currency = account.Currency,
            Balance = account.Balance,
            Status = account.Status,
            OpenedAt = account.OpenedAt
        });
    }

    /// <summary>
    /// Get account balance history (temporal versioning query).
    /// </summary>
    [HttpGet("{accountNumber}/history")]
    public async Task<ActionResult<IEnumerable<AccountHistoryDto>>> GetAccountHistory(
        string accountNumber,
        [FromQuery] DateTime? asOf)
    {
        _logger.LogInformation(
            "Querying account history for {AccountNumber} (temporal versioning)",
            accountNumber);

        // In a real implementation, this would query the temporal history table
        var account = await _context.Accounts
            .Where(a => a.AccountNumber == accountNumber)
            .FirstOrDefaultAsync();

        if (account == null)
            return NotFound();

        // Simulated history - real implementation would query temporal tables
        var history = new List<AccountHistoryDto>
        {
            new()
            {
                AccountNumber = account.AccountNumber,
                Balance = account.Balance,
                Status = account.Status,
                ValidFrom = account.ValidFrom,
                ValidTo = account.ValidTo,
                IsCurrent = true
            }
        };

        return Ok(history);
    }

    /// <summary>
    /// Get distribution statistics across regions.
    /// </summary>
    [HttpGet("stats/distribution")]
    public async Task<ActionResult<RegionalDistributionStats>> GetRegionalDistribution()
    {
        _logger.LogInformation("Fetching regional distribution (fan-out across region shards)");

        var stats = await _context.Accounts
            .GroupBy(a => a.Region)
            .Select(g => new RegionalStats
            {
                Region = g.Key,
                AccountCount = g.Count(),
                TotalBalance = g.Sum(a => a.Balance),
                AverageBalance = g.Average(a => a.Balance)
            })
            .ToListAsync();

        return Ok(new RegionalDistributionStats
        {
            TotalAccounts = stats.Sum(s => s.AccountCount),
            TotalBalance = stats.Sum(s => s.TotalBalance),
            RegionStats = stats
        });
    }

    private static string GenerateAccountNumber()
    {
        // Use cryptographically secure random number generator
        var randomNumber = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
        return $"ACC{DateTime.UtcNow:yyyyMMdd}{randomNumber}";
    }
}

// DTOs
public record AccountDto
{
    public long Id { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime OpenedAt { get; init; }
}

public record AccountDetailDto : AccountDto
{
    public string HolderId { get; init; } = string.Empty;
    public DateTime ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
    public IEnumerable<TransactionSummaryDto> RecentTransactions { get; init; } = [];
}

public record CreateAccountRequest
{
    public required string Region { get; init; }
    public required string AccountType { get; init; }
    public required string Currency { get; init; }
    public required string HolderId { get; init; }
    public decimal? InitialDeposit { get; init; }
}

public record TransactionSummaryDto
{
    public string TransactionType { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime TransactionDate { get; init; }
    public string Status { get; init; } = string.Empty;
}

public record AccountHistoryDto
{
    public string AccountNumber { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
    public bool IsCurrent { get; init; }
}

public record RegionalDistributionStats
{
    public int TotalAccounts { get; init; }
    public decimal TotalBalance { get; init; }
    public IEnumerable<RegionalStats> RegionStats { get; init; } = [];
}

public record RegionalStats
{
    public string Region { get; init; } = string.Empty;
    public int AccountCount { get; init; }
    public decimal TotalBalance { get; init; }
    public decimal AverageBalance { get; init; }
}
