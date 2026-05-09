using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.Transactions.Data;
using Dtde.Samples.Transactions.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

// ============================================================================
// DTDE Transactions Sample
// ============================================================================
// Demonstrates the full cross-shard transaction surface:
//   • BeginCrossShardTransactionAsync — explicit 2PC across shards.
//   • Savepoints — within-shard partial rollback.
//   • Read-after-write — queries see uncommitted writes inside the tx.
//   • Crash-recovery transaction log — durable record of in-flight tx state.
// ----------------------------------------------------------------------------
// File-based transaction log is registered explicitly so RecoverAsync has
// something to replay after a process restart.
// ============================================================================

builder.Services.AddSingleton<ITransactionLog>(_ =>
    new FileBasedTransactionLog(Path.Combine(AppContext.BaseDirectory, "tx-log.jsonl")));

builder.Services.AddDtdeDbContext<TransactionsDbContext>(
    (db, conn) => db.UseSqlite(conn ?? "Data Source=transactions.db"),
    dtde => dtde.AddShards("EU", "US", "APAC"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
    await ctx.EnsureAllShardsCreatedAsync();

    // Replay any in-doubt transactions from a previous run.
    var coordinator = scope.ServiceProvider.GetRequiredService<ICrossShardTransactionCoordinator>();
    await coordinator.RecoverAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ----------------------------------------------------------------------------
// 1. Atomic cross-shard transfer between two regions.
// ----------------------------------------------------------------------------
app.MapPost("/transfer", async (
    [FromBody] TransferRequest request,
    TransactionsDbContext db,
    CancellationToken ct) =>
{
    await using var tx = await db.BeginCrossShardTransactionAsync(
        new CrossShardTransactionOptions
        {
            IsolationLevel = CrossShardIsolationLevel.Serializable,
        }, ct);

    var crossShardTx = (CrossShardTransaction)tx;

    var fromShard = db.ShardRegistry.GetShard(request.FromRegion)!;
    var toShard = db.ShardRegistry.GetShard(request.ToRegion)!;

    var fromParticipant = await crossShardTx.GetOrCreateParticipantAsync(fromShard, ct);
    var toParticipant = await crossShardTx.GetOrCreateParticipantAsync(toShard, ct);

    var sender = await fromParticipant.Context.Set<Account>()
        .FirstOrDefaultAsync(a => a.Id == request.FromAccountId, ct);
    var receiver = await toParticipant.Context.Set<Account>()
        .FirstOrDefaultAsync(a => a.Id == request.ToAccountId, ct);

    if (sender is null || receiver is null)
    {
        return Results.NotFound("One of the accounts does not exist.");
    }

    if (sender.Balance < request.Amount)
    {
        return Results.BadRequest("Insufficient funds.");
    }

    sender.Balance -= request.Amount;
    receiver.Balance += request.Amount;

    await fromParticipant.Context.SaveChangesAsync(ct);
    await toParticipant.Context.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return Results.Ok(new
    {
        Status = "transferred",
        request.Amount,
        FromAccount = sender.Id,
        ToAccount = receiver.Id,
    });
})
.WithName("Transfer")
.WithDescription("Atomic cross-shard transfer between two accounts in different regions.");

// ----------------------------------------------------------------------------
// 2. Savepoint demo: try an optional bonus crediting; roll back if it fails.
// ----------------------------------------------------------------------------
app.MapPost("/credit-with-bonus", async (
    [FromBody] CreditWithBonusRequest request,
    TransactionsDbContext db,
    CancellationToken ct) =>
{
    await using var tx = await db.BeginCrossShardTransactionAsync(ct);
    var crossShardTx = (CrossShardTransaction)tx;
    var shard = db.ShardRegistry.GetShard(request.Region)!;
    var participant = await crossShardTx.GetOrCreateParticipantAsync(shard, ct);

    var account = await participant.Context.Set<Account>()
        .FirstOrDefaultAsync(a => a.Id == request.AccountId, ct);
    if (account is null)
    {
        return Results.NotFound();
    }

    // Step 1: required credit.
    account.Balance += request.BaseAmount;
    await participant.Context.SaveChangesAsync(ct);

    // Step 2: bonus credit, behind a savepoint so we can back it out.
    await participant.CreateSavepointAsync("bonus", ct);

    account.Balance += request.BonusAmount;
    await participant.Context.SaveChangesAsync(ct);

    if (request.RejectBonus)
    {
        // Whatever business rule we wanted to evaluate decided no bonus.
        // Roll back to the savepoint — base credit survives, bonus is gone.
        await participant.RollbackToSavepointAsync("bonus", ct);
    }

    await tx.CommitAsync(ct);

    var final = await participant.Context.Set<Account>()
        .AsNoTracking()
        .FirstAsync(a => a.Id == request.AccountId, ct);

    return Results.Ok(new
    {
        Status = "credited",
        FinalBalance = final.Balance,
        BonusApplied = !request.RejectBonus,
    });
})
.WithName("CreditWithBonus")
.WithDescription("Demonstrates a savepoint inside a cross-shard transaction.");

// ----------------------------------------------------------------------------
// 3. Read-after-write: query inside a transaction sees uncommitted writes
//    on the same shard.
// ----------------------------------------------------------------------------
app.MapPost("/within-tx-rollup", async (
    [FromBody] WithinTxRollupRequest request,
    TransactionsDbContext db,
    Dtde.EntityFramework.Query.IShardedQueryExecutor executor,
    CancellationToken ct) =>
{
    await using var tx = await db.BeginCrossShardTransactionAsync(ct);
    var crossShardTx = (CrossShardTransaction)tx;
    var shard = db.ShardRegistry.GetShard(request.Region)!;
    var participant = await crossShardTx.GetOrCreateParticipantAsync(shard, ct);

    // Insert a new account inside the transaction.
    participant.Context.Set<Account>().Add(new Account
    {
        Id = request.NewAccountId,
        Region = request.Region,
        Balance = request.InitialBalance,
    });
    await participant.Context.SaveChangesAsync(ct);

    // Now query through the executor — it consults the ambient transaction,
    // reuses the participant's open context, and sees the new row.
    var totalBalance = await executor.ExecuteAsync(
        db.Set<Account>().Where(a => a.Region == request.Region).AsQueryable(),
        ct);

    var sum = totalBalance.Sum(a => a.Balance);

    await tx.CommitAsync(ct);

    return Results.Ok(new
    {
        TotalBalanceInRegion = sum,
        AccountCount = totalBalance.Count,
    });
})
.WithName("WithinTxRollup")
.WithDescription("Demonstrates read-after-write semantics inside a cross-shard transaction.");

// ----------------------------------------------------------------------------
// 4. Recovery status — manually trigger a recovery scan.
// ----------------------------------------------------------------------------
app.MapGet("/recovery", async (
    ICrossShardTransactionCoordinator coordinator,
    ITransactionLog log,
    CancellationToken ct) =>
{
    var inDoubt = await log.GetInDoubtTransactionsAsync(ct);
    var resolved = await coordinator.RecoverAsync(ct);
    return Results.Ok(new
    {
        InDoubtBeforeRecovery = inDoubt.Count,
        Resolved = resolved,
        InDoubtNow = (await log.GetInDoubtTransactionsAsync(ct)).Count,
    });
})
.WithName("Recovery");

app.MapGet("/", () => Results.Ok(new
{
    Sample = "Dtde.Samples.Transactions",
    Endpoints = new[]
    {
        "POST /transfer            — atomic cross-shard transfer",
        "POST /credit-with-bonus   — savepoint demo",
        "POST /within-tx-rollup    — read-after-write inside tx",
        "GET  /recovery            — replay the durable log",
    },
}));

app.Run();

public sealed record TransferRequest(
    string FromRegion,
    int FromAccountId,
    string ToRegion,
    int ToAccountId,
    decimal Amount);

public sealed record CreditWithBonusRequest(
    string Region,
    int AccountId,
    decimal BaseAmount,
    decimal BonusAmount,
    bool RejectBonus);

public sealed record WithinTxRollupRequest(
    string Region,
    int NewAccountId,
    decimal InitialBalance);
