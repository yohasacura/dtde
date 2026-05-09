using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.RegionSharding.Data;

using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ============================================================================
// DTDE Region-Based Table Sharding Configuration
// ============================================================================
// This sample demonstrates sharding by property value (Region).
// All sharding configuration is in RegionShardingDbContext.OnModelCreating():
// - Customer: ShardBy(c => c.Region)
// - Order: ShardBy(o => o.Region)
// - OrderItem: ShardBy(i => i.Region)
//
// Tables are created per region: Customers_EU, Customers_US, Customers_APAC, etc.
// ============================================================================

builder.Services.AddDtdeDbContext<RegionShardingDbContext>(
    (db, conn) => db.UseSqlite(
        conn
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=region_sharding.db"),
    dtde => dtde.AddShards("EU", "US", "APAC"));

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<RegionShardingDbContext>();
    await context.EnsureAllShardsCreatedAsync();
}

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
