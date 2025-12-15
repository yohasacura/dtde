using Dtde.Samples.Combined.Data;
using Dtde.Samples.Combined.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.Combined.Controllers;

/// <summary>
/// API controller demonstrating transparent cross-shard transactions.
///
/// DTDE's TransparentShardingInterceptor automatically handles:
/// - Regular SaveChangesAsync() with entities spanning multiple shards
/// - Explicit transactions that modify data across different shards
/// - Automatic 2PC (Two-Phase Commit) coordination when needed
///
/// From the application code perspective, you write normal EF Core code
/// and DTDE handles the cross-shard coordination transparently.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CrossShardTransactionsController : ControllerBase
{
    private readonly CombinedDbContext _context;
    private readonly ILogger<CrossShardTransactionsController> _logger;

    public CrossShardTransactionsController(
        CombinedDbContext context,
        ILogger<CrossShardTransactionsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Transparent SaveChanges - No Explicit Transaction Needed

    /// <summary>
    /// Creates accounts in multiple regions in a single SaveChanges call.
    /// DTDE automatically detects the cross-shard operation and coordinates the transaction.
    ///
    /// Example: Creating accounts in EU and US regions atomically.
    /// </summary>
    [HttpPost("accounts/multi-region")]
    public async Task<ActionResult<MultiRegionAccountResult>> CreateMultiRegionAccounts(
        CreateMultiRegionAccountsRequest request)
    {
        _logger.LogInformation(
            "Creating accounts across {Count} regions (transparent cross-shard transaction)",
            request.Accounts.Count);

        var createdAccounts = new List<AccountDto>();

        // Just add all entities - DTDE handles the cross-shard coordination
        foreach (var accountRequest in request.Accounts)
        {
            var account = new Account
            {
                Region = accountRequest.Region.ToUpper(),
                AccountNumber = GenerateAccountNumber(),
                AccountType = accountRequest.AccountType,
                Currency = accountRequest.Currency,
                Balance = accountRequest.InitialDeposit ?? 0,
                HolderId = request.HolderId,
                OpenedAt = DateTime.UtcNow,
                Status = "Active",
                ValidFrom = DateTime.UtcNow
            };

            _context.Accounts.Add(account);

            createdAccounts.Add(new AccountDto
            {
                AccountNumber = account.AccountNumber,
                AccountType = account.AccountType,
                Region = account.Region,
                Currency = account.Currency,
                Balance = account.Balance,
                Status = account.Status,
                OpenedAt = account.OpenedAt
            });
        }

        // Single SaveChanges - DTDE transparently coordinates across regional shards
        await _context.SaveChangesAsync();

        return Ok(new MultiRegionAccountResult
        {
            HolderId = request.HolderId,
            Message = $"Successfully created {createdAccounts.Count} accounts across multiple regions",
            Accounts = createdAccounts
        });
    }

    /// <summary>
    /// Transfers funds between accounts that may be in different regional shards.
    /// Creates a transaction record (date-sharded) and updates both accounts (region-sharded).
    /// All in a single SaveChanges - DTDE handles the complexity.
    /// </summary>
    [HttpPost("transfer")]
    public async Task<ActionResult<TransferResult>> TransferBetweenAccounts(TransferRequest request)
    {
        var sourceAccount = await _context.Accounts
            .FirstOrDefaultAsync(a => a.AccountNumber == request.SourceAccountNumber);

        var destAccount = await _context.Accounts
            .FirstOrDefaultAsync(a => a.AccountNumber == request.DestinationAccountNumber);

        if (sourceAccount == null)
            return NotFound($"Source account {request.SourceAccountNumber} not found");

        if (destAccount == null)
            return NotFound($"Destination account {request.DestinationAccountNumber} not found");

        if (sourceAccount.Balance < request.Amount)
            return BadRequest("Insufficient funds");

        // Log using sanitized internal account data, not direct user input
        _logger.LogInformation(
            "Cross-shard transfer: {Amount} from account in region {SourceRegion} to account in region {DestRegion}",
            request.Amount, sourceAccount.Region, destAccount.Region);

        // Update source account balance (region shard: sourceAccount.Region)
        var sourcePreviousBalance = sourceAccount.Balance;
        sourceAccount.Balance -= request.Amount;

        // Update destination account balance (region shard: destAccount.Region)
        var destPreviousBalance = destAccount.Balance;
        destAccount.Balance += request.Amount;

        // Create debit transaction (date shard: current month)
        var debitTransaction = new AccountTransaction
        {
            TransactionDate = DateTime.UtcNow,
            AccountNumber = sourceAccount.AccountNumber,
            TransactionType = "Transfer-Debit",
            Amount = -request.Amount,
            BalanceBefore = sourcePreviousBalance,
            BalanceAfter = sourceAccount.Balance,
            Currency = sourceAccount.Currency,
            Description = request.Description ?? $"Transfer to {destAccount.AccountNumber}",
            CounterpartyAccount = destAccount.AccountNumber,
            Reference = GenerateReference(),
            Status = "Completed",
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };

        // Create credit transaction (date shard: current month)
        var creditTransaction = new AccountTransaction
        {
            TransactionDate = DateTime.UtcNow,
            AccountNumber = destAccount.AccountNumber,
            TransactionType = "Transfer-Credit",
            Amount = request.Amount,
            BalanceBefore = destPreviousBalance,
            BalanceAfter = destAccount.Balance,
            Currency = destAccount.Currency,
            Description = request.Description ?? $"Transfer from {sourceAccount.AccountNumber}",
            CounterpartyAccount = sourceAccount.AccountNumber,
            Reference = debitTransaction.Reference,
            Status = "Completed",
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };

        _context.Transactions.Add(debitTransaction);
        _context.Transactions.Add(creditTransaction);

        // Single SaveChanges handles:
        // - Update to source account (EU shard)
        // - Update to destination account (US shard)
        // - Insert debit transaction (date shard)
        // - Insert credit transaction (date shard)
        // All atomically coordinated by DTDE
        await _context.SaveChangesAsync();

        return Ok(new TransferResult
        {
            TransferReference = debitTransaction.Reference!,
            SourceAccount = request.SourceAccountNumber,
            DestinationAccount = request.DestinationAccountNumber,
            Amount = request.Amount,
            SourceNewBalance = sourceAccount.Balance,
            DestinationNewBalance = destAccount.Balance,
            Message = sourceAccount.Region == destAccount.Region
                ? "Transfer completed (same region)"
                : $"Cross-region transfer completed ({sourceAccount.Region} → {destAccount.Region})"
        });
    }

    #endregion

    #region Explicit Transactions - Also Transparent

    /// <summary>
    /// Demonstrates explicit transaction spanning multiple operations across shards.
    /// Useful when you need multiple SaveChanges calls within a single atomic unit.
    ///
    /// DTDE intercepts BeginTransaction/Commit/Rollback and manages cross-shard coordination.
    /// </summary>
    [HttpPost("batch-operations")]
    public async Task<ActionResult<BatchOperationResult>> ExecuteBatchOperations(
        BatchOperationRequest request)
    {
        var results = new List<string>();

        _logger.LogInformation(
            "Starting explicit transaction for batch operations across shards");

        // Start explicit transaction - DTDE intercepts this
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Operation 1: Create accounts in multiple regions
            foreach (var accountReq in request.AccountsToCreate)
            {
                var account = new Account
                {
                    Region = accountReq.Region.ToUpper(),
                    AccountNumber = GenerateAccountNumber(),
                    AccountType = accountReq.AccountType,
                    Currency = accountReq.Currency,
                    Balance = accountReq.InitialDeposit ?? 0,
                    HolderId = accountReq.HolderId ?? "batch-holder",
                    OpenedAt = DateTime.UtcNow,
                    Status = "Active",
                    ValidFrom = DateTime.UtcNow
                };
                _context.Accounts.Add(account);
                results.Add($"Created account {account.AccountNumber} in {account.Region}");
            }

            // First SaveChanges - creates accounts across regional shards
            await _context.SaveChangesAsync();

            // Operation 2: Create transactions for existing accounts
            foreach (var txnReq in request.TransactionsToCreate)
            {
                var account = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.AccountNumber == txnReq.AccountNumber);

                if (account is not null)
                {
                    var previousBalance = account.Balance;
                    account.Balance += txnReq.Amount;

                    var transaction2 = new AccountTransaction
                    {
                        TransactionDate = DateTime.UtcNow,
                        AccountNumber = account.AccountNumber,
                        TransactionType = txnReq.Amount >= 0 ? "Deposit" : "Withdrawal",
                        Amount = txnReq.Amount,
                        BalanceBefore = previousBalance,
                        BalanceAfter = account.Balance,
                        Currency = account.Currency,
                        Description = txnReq.Description,
                        Status = "Completed",
                        CreatedAt = DateTime.UtcNow,
                        ProcessedAt = DateTime.UtcNow
                    };
                    _context.Transactions.Add(transaction2);
                    results.Add($"Created {transaction2.TransactionType} of {txnReq.Amount} for {account.AccountNumber}");
                }
            }

            // Second SaveChanges - updates accounts and creates transactions
            await _context.SaveChangesAsync();

            // Operation 3: Create audit records (hash-sharded)
            var audit = new ComplianceAudit
            {
                EntityType = "BatchOperation",
                EntityReference = Guid.NewGuid().ToString(),
                AuditType = "BatchTransaction",
                PerformedAt = DateTime.UtcNow,
                PerformedBy = request.PerformedBy ?? "System",
                Reason = $"Batch: {request.AccountsToCreate.Count} accounts, {request.TransactionsToCreate.Count} transactions",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };
            _context.ComplianceAudits.Add(audit);

            // Third SaveChanges - creates audit record in hash shard
            await _context.SaveChangesAsync();

            // Commit the explicit transaction - DTDE coordinates 2PC across all involved shards
            await transaction.CommitAsync();

            results.Add("Transaction committed successfully across all shards");

            return Ok(new BatchOperationResult
            {
                Success = true,
                Operations = results,
                Message = "All batch operations completed atomically across shards"
            });
        }
#pragma warning disable CA1031 // API error handler must catch all exceptions for proper HTTP response
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Rollback - DTDE ensures all shards are rolled back
            await transaction.RollbackAsync();

            _logger.LogError(ex, "Batch operation failed, rolled back across all shards");

            return StatusCode(500, new BatchOperationResult
            {
                Success = false,
                Operations = results,
                Message = $"Operation failed and was rolled back: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Demonstrates a rollback scenario where we intentionally fail after some operations.
    /// This verifies that cross-shard rollback works correctly.
    /// </summary>
    [HttpPost("test-rollback")]
    public async Task<ActionResult<RollbackTestResult>> TestCrossShardRollback(
        RollbackTestRequest request)
    {
        _logger.LogInformation("Testing cross-shard rollback scenario");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Create account in EU region
            var euAccount = new Account
            {
                Region = "EU",
                AccountNumber = GenerateAccountNumber(),
                AccountType = "Testing",
                Currency = "EUR",
                Balance = 1000,
                HolderId = request.HolderId,
                OpenedAt = DateTime.UtcNow,
                Status = "Active",
                ValidFrom = DateTime.UtcNow
            };
            _context.Accounts.Add(euAccount);
            await _context.SaveChangesAsync();

            // Create account in US region
            var usAccount = new Account
            {
                Region = "US",
                AccountNumber = GenerateAccountNumber(),
                AccountType = "Testing",
                Currency = "USD",
                Balance = 1000,
                HolderId = request.HolderId,
                OpenedAt = DateTime.UtcNow,
                Status = "Active",
                ValidFrom = DateTime.UtcNow
            };
            _context.Accounts.Add(usAccount);
            await _context.SaveChangesAsync();

            // Create transaction record
            var txn = new AccountTransaction
            {
                TransactionDate = DateTime.UtcNow,
                AccountNumber = euAccount.AccountNumber,
                TransactionType = "Deposit",
                Amount = 1000,
                BalanceBefore = 0,
                BalanceAfter = 1000,
                Currency = "EUR",
                Description = "Initial deposit",
                Status = "Completed",
                CreatedAt = DateTime.UtcNow
            };
            _context.Transactions.Add(txn);
            await _context.SaveChangesAsync();

            if (request.ShouldFail)
            {
                // Simulate a failure - this triggers rollback
                throw new InvalidOperationException("Simulated failure for rollback testing");
            }

            await transaction.CommitAsync();

            return Ok(new RollbackTestResult
            {
                WasRolledBack = false,
                Message = "Transaction committed (shouldFail was false)",
                EuAccountNumber = euAccount.AccountNumber,
                UsAccountNumber = usAccount.AccountNumber
            });
        }
#pragma warning disable CA1031 // API error handler must catch all exceptions for proper HTTP response
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await transaction.RollbackAsync();

            return Ok(new RollbackTestResult
            {
                WasRolledBack = true,
                Message = $"Transaction rolled back across all shards: {ex.Message}",
                EuAccountNumber = null,
                UsAccountNumber = null
            });
        }
    }

    #endregion

    #region Cross-Shard Queries

    /// <summary>
    /// Demonstrates querying across multiple regional shards.
    /// DTDE can route queries to appropriate shards based on the query predicates.
    /// </summary>
    [HttpGet("accounts/global")]
    public async Task<ActionResult<GlobalAccountSummary>> GetGlobalAccountSummary()
    {
        _logger.LogInformation("Fetching global account summary across all regional shards");

        var accountsByRegion = await _context.Accounts
            .GroupBy(a => a.Region)
            .Select(g => new RegionSummary
            {
                Region = g.Key,
                AccountCount = g.Count(),
                TotalBalance = g.Sum(a => a.Balance),
                ActiveAccounts = g.Count(a => a.Status == "Active")
            })
            .ToListAsync();

        return Ok(new GlobalAccountSummary
        {
            Regions = accountsByRegion,
            TotalAccounts = accountsByRegion.Sum(r => r.AccountCount),
            TotalBalance = accountsByRegion.Sum(r => r.TotalBalance)
        });
    }

    /// <summary>
    /// Gets a customer's complete portfolio across all regions.
    /// This query spans multiple regional shards based on the holderId.
    /// </summary>
    [HttpGet("portfolio/{holderId}")]
    public async Task<ActionResult<CustomerPortfolio>> GetCustomerPortfolio(string holderId)
    {
        // Sanitize holderId for logging (remove newlines/control chars that could forge log entries)
        var sanitizedHolderId = SanitizeForLogging(holderId);
        _logger.LogInformation(
            "Fetching portfolio for holder {HolderId} across all regional shards",
            sanitizedHolderId);

        var accounts = await _context.Accounts
            .Where(a => a.HolderId == holderId)
            .OrderBy(a => a.Region)
            .ThenBy(a => a.AccountType)
            .ToListAsync();

        if (!accounts.Any())
            return NotFound($"No accounts found for holder {holderId}");

        var accountNumbers = accounts.Select(a => a.AccountNumber).ToList();

        // Get recent transactions across all accounts (spans date shards)
        var recentTransactions = await _context.Transactions
            .Where(t => accountNumbers.Contains(t.AccountNumber))
            .OrderByDescending(t => t.TransactionDate)
            .Take(20)
            .ToListAsync();

        return Ok(new CustomerPortfolio
        {
            HolderId = holderId,
            Accounts = accounts.Select(a => new AccountDto
            {
                Id = a.Id,
                AccountNumber = a.AccountNumber,
                AccountType = a.AccountType,
                Region = a.Region,
                Currency = a.Currency,
                Balance = a.Balance,
                Status = a.Status,
                OpenedAt = a.OpenedAt
            }).ToList(),
            RecentTransactions = recentTransactions.Select(t => new PortfolioTransactionDto
            {
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                TransactionDate = t.TransactionDate,
                Status = t.Status,
                AccountNumber = t.AccountNumber
            }).ToList(),
            TotalBalanceByRegion = accounts
                .GroupBy(a => a.Region)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Balance))
        });
    }

    #endregion

    #region Helper Methods

    private static string GenerateAccountNumber() =>
        $"ACC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";

    private static string GenerateReference() =>
        $"TRF-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

    /// <summary>
    /// Sanitizes user input for safe logging to prevent log forging attacks.
    /// Removes newlines and control characters that could inject fake log entries.
    /// </summary>
    private static string SanitizeForLogging(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Replace newlines and control characters to prevent log injection
        return input
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);
    }

    #endregion
}

#region DTOs

public record CreateMultiRegionAccountsRequest
{
    public required string HolderId { get; init; }
    public required List<RegionalAccountRequest> Accounts { get; init; }
}

public record RegionalAccountRequest
{
    public required string Region { get; init; }
    public required string AccountType { get; init; }
    public required string Currency { get; init; }
    public decimal? InitialDeposit { get; init; }
    public string? HolderId { get; init; }
}

public record MultiRegionAccountResult
{
    public required string HolderId { get; init; }
    public required string Message { get; init; }
    public required List<AccountDto> Accounts { get; init; }
}

public record TransferRequest
{
    public required string SourceAccountNumber { get; init; }
    public required string DestinationAccountNumber { get; init; }
    public decimal Amount { get; init; }
    public string? Description { get; init; }
}

public record TransferResult
{
    public required string TransferReference { get; init; }
    public required string SourceAccount { get; init; }
    public required string DestinationAccount { get; init; }
    public decimal Amount { get; init; }
    public decimal SourceNewBalance { get; init; }
    public decimal DestinationNewBalance { get; init; }
    public required string Message { get; init; }
}

public record BatchOperationRequest
{
    public List<RegionalAccountRequest> AccountsToCreate { get; init; } = [];
    public List<TransactionRequest> TransactionsToCreate { get; init; } = [];
    public string? PerformedBy { get; init; }
}

public record TransactionRequest
{
    public required string AccountNumber { get; init; }
    public decimal Amount { get; init; }
    public string? Description { get; init; }
}

public record BatchOperationResult
{
    public bool Success { get; init; }
    public List<string> Operations { get; init; } = [];
    public required string Message { get; init; }
}

public record RollbackTestRequest
{
    public required string HolderId { get; init; }
    public bool ShouldFail { get; init; }
}

public record RollbackTestResult
{
    public bool WasRolledBack { get; init; }
    public required string Message { get; init; }
    public string? EuAccountNumber { get; init; }
    public string? UsAccountNumber { get; init; }
}

public record GlobalAccountSummary
{
    public required List<RegionSummary> Regions { get; init; }
    public int TotalAccounts { get; init; }
    public decimal TotalBalance { get; init; }
}

public record RegionSummary
{
    public required string Region { get; init; }
    public int AccountCount { get; init; }
    public decimal TotalBalance { get; init; }
    public int ActiveAccounts { get; init; }
}

public record CustomerPortfolio
{
    public required string HolderId { get; init; }
    public required List<AccountDto> Accounts { get; init; }
    public required List<PortfolioTransactionDto> RecentTransactions { get; init; }
    public required Dictionary<string, decimal> TotalBalanceByRegion { get; init; }
}

/// <summary>
/// Transaction DTO with account number for portfolio view.
/// </summary>
public record PortfolioTransactionDto
{
    public required string TransactionType { get; init; }
    public decimal Amount { get; init; }
    public DateTime TransactionDate { get; init; }
    public required string Status { get; init; }
    public required string AccountNumber { get; init; }
}

#endregion
