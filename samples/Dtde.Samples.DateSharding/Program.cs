using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.DateSharding.Data;

using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ============================================================================
// DTDE Date-Based Table Sharding Configuration
// ============================================================================
// This sample demonstrates sharding by date ranges using fluent API.
// All sharding configuration is in DateShardingDbContext.OnModelCreating():
// - Transactions: ShardByDate(t => t.TransactionDate)
// - AuditLogs: ShardByDate(a => a.Timestamp) 
// - Metrics: ShardByDate(m => m.Timestamp)
// ============================================================================

builder.Services.AddDtdeDbContext<DateShardingDbContext>(
    (db, conn) => db.UseSqlite(
        conn
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=date_sharding.db"),
    dtde => dtde.AddShards("2023", "2024", "2025"));

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DateShardingDbContext>();
    await context.EnsureAllShardsCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
