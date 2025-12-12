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
    dbOptions =>
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=date_sharding.db";
        dbOptions.UseSqlite(connectionString);
    },
    dtdeOptions =>
    {
        // Sharding is configured in DbContext.OnModelCreating using fluent API
        // No additional builder configuration needed for basic scenarios
    });

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DateShardingDbContext>();
    context.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
