using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.EntityFramework.Update;
using Dtde.Samples.BulkOperations;
using Dtde.Samples.BulkOperations.Data;
using Dtde.Samples.BulkOperations.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

// ============================================================================
// DTDE Bulk Operations Sample
// ============================================================================
// Demonstrates:
//   • BulkInsertAsync          — routes per shard, single round-trip per shard.
//   • BulkUpdateAsync          — set-based UPDATE fan-out across the group.
//   • BulkDeleteAsync          — set-based DELETE fan-out across the group.
//   • ExecuteStreamingAsync    — IAsyncEnumerable<T> with bounded buffering.
//   • Custom IBulkInsertProvider — plug in your own bulk path (SqlBulkCopy etc).
// ----------------------------------------------------------------------------
// The custom provider is registered BEFORE AddDtdeDbContext so it sits in
// front of the default; it logs each bulk-insert call so you can watch it
// take effect.
// ============================================================================

builder.Services.AddSingleton<IBulkInsertProvider, LoggingBulkInsertProvider>();

builder.Services.AddDtdeDbContext<BulkOpsDbContext>(
    (db, conn) => db.UseSqlite(conn ?? "Data Source=bulk_ops.db"),
    dtde => dtde.AddShards("EU", "US", "APAC"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<BulkOpsDbContext>();
    await ctx.EnsureAllShardsCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ----------------------------------------------------------------------------
// 1. Bulk insert — generates synthetic events across all three regions.
// ----------------------------------------------------------------------------
app.MapPost("/seed", async (
    [FromQuery] int count,
    BulkOpsDbContext db,
    CancellationToken ct) =>
{
    if (count <= 0)
    {
        return Results.BadRequest("count must be positive.");
    }

    var regions = new[] { "EU", "US", "APAC" };
    var rand = new Random(42);
    var events = Enumerable.Range(1, count).Select(i => new Event
    {
        Id = i,
        Region = regions[rand.Next(regions.Length)],
        Type = (rand.NextDouble() < 0.5) ? "click" : "view",
        Payload = $"event-{i}",
        CreatedAt = DateTime.UtcNow,
    }).ToList();

    var inserted = await db.BulkInsertAsync(events, ct);
    return Results.Ok(new { Status = "seeded", Inserted = inserted });
})
.WithName("Seed");

// ----------------------------------------------------------------------------
// 2. Bulk update — set-based UPDATE across every shard in the group.
// ----------------------------------------------------------------------------
app.MapPost("/anonymise-clicks", async (
    BulkOpsDbContext db,
    CancellationToken ct) =>
{
#if NET10_0_OR_GREATER
    var updated = await db.BulkUpdateAsync<Event>(
        e => e.Type == "click",
        setters => setters.SetProperty(e => e.Payload, "<redacted>"),
        ct);
#else
    var updated = await db.BulkUpdateAsync<Event>(
        e => e.Type == "click",
        p => p.SetProperty(e => e.Payload, "<redacted>"),
        ct);
#endif

    return Results.Ok(new { Status = "anonymised", RowsUpdated = updated });
})
.WithName("AnonymiseClicks");

// ----------------------------------------------------------------------------
// 3. Bulk delete — set-based DELETE across every shard in the group.
// ----------------------------------------------------------------------------
app.MapPost("/purge-old", async (
    [FromQuery] DateTime before,
    BulkOpsDbContext db,
    CancellationToken ct) =>
{
    var deleted = await db.BulkDeleteAsync<Event>(e => e.CreatedAt < before, ct);
    return Results.Ok(new { Status = "purged", RowsDeleted = deleted });
})
.WithName("PurgeOld");

// ----------------------------------------------------------------------------
// 4. Streaming fan-out — pulls each shard's results into a bounded
//    Channel<T> and yields entities as IAsyncEnumerable. Constant memory,
//    regardless of total result-set size.
// ----------------------------------------------------------------------------
app.MapGet("/stream", (
    [FromQuery] int? bufferSize,
    BulkOpsDbContext db,
    Dtde.EntityFramework.Query.IShardedQueryExecutor executor,
    CancellationToken ct) =>
{
    return Results.Ok(StreamEvents(db, executor, bufferSize, ct));
})
.WithName("Stream");

static async IAsyncEnumerable<Event> StreamEvents(
    BulkOpsDbContext db,
    Dtde.EntityFramework.Query.IShardedQueryExecutor executor,
    int? bufferSize,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var ev in executor.ExecuteStreamingAsync(
        db.Set<Event>().AsQueryable(),
        bufferSize,
        ct))
    {
        yield return ev;
    }
}

// ----------------------------------------------------------------------------
// 5. Streaming + projection — like /stream but only forwards a small DTO.
//    Useful for very large fan-outs where you don't want to allocate the
//    full entity per row.
// ----------------------------------------------------------------------------
app.MapGet("/stream-summary", (
    BulkOpsDbContext db,
    Dtde.EntityFramework.Query.IShardedQueryExecutor executor,
    CancellationToken ct) =>
{
    return Results.Ok(StreamSummaries(db, executor, ct));
})
.WithName("StreamSummary");

static async IAsyncEnumerable<EventSummary> StreamSummaries(
    BulkOpsDbContext db,
    Dtde.EntityFramework.Query.IShardedQueryExecutor executor,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var ev in executor.ExecuteStreamingAsync(
        db.Set<Event>().AsQueryable(),
        bufferSize: null,
        ct))
    {
        yield return new EventSummary(ev.Id, ev.Region, ev.Type);
    }
}

app.MapGet("/", () => Results.Ok(new
{
    Sample = "Dtde.Samples.BulkOperations",
    Endpoints = new[]
    {
        "POST /seed?count=N            — bulk-insert N synthetic events",
        "POST /anonymise-clicks        — bulk-update all 'click' events' payloads",
        "POST /purge-old?before=DATE   — bulk-delete events before a cutoff",
        "GET  /stream?bufferSize=N     — IAsyncEnumerable streaming fan-out",
        "GET  /stream-summary          — streaming + projection",
    },
}));

app.Run();

public sealed record EventSummary(int Id, string Region, string Type);
