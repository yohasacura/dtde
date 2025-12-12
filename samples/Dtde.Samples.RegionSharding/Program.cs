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
    // Configure EF Core DbContext
    dbOptions =>
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=region_sharding.db";
        dbOptions.UseSqlite(connectionString);
    },
    // Configure DTDE sharding options
    dtdeOptions =>
    {
        // Sharding is configured in DbContext.OnModelCreating using fluent API
        // No additional builder configuration needed for basic scenarios
    });

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<RegionShardingDbContext>();
    context.Database.EnsureCreated();
}

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
