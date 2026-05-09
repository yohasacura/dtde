using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.Combined.Data;

using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Configure DTDE DbContext with combined sharding strategies
// All sharding configuration is in CombinedDbContext.OnModelCreating()
builder.Services.AddDtdeDbContext<CombinedDbContext>(
    (db, conn) => db.UseSqlite(
        conn
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=combined.db"),
    dtde => dtde.AddShards(
        // Region-sharded entities (Account, RegulatoryDocument)
        "EU", "US", "APAC",
        // Date-sharded entities (AccountTransaction)
        "2024", "2025",
        // Hash-sharded entities (ComplianceAudit) — 8 buckets
        "h0", "h1", "h2", "h3", "h4", "h5", "h6", "h7"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<CombinedDbContext>();
    await context.EnsureAllShardsCreatedAsync();
}

app.Run();
