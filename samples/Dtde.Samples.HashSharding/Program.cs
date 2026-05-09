using Dtde.EntityFramework.Extensions;
using Dtde.Samples.HashSharding.Data;

using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Configure DTDE with hash-based sharding (8 logical shards over a single SQLite DB).
builder.Services.AddDtdeDbContext<HashShardingDbContext>(
    (db, conn) => db.UseSqlite(
        conn
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=hash_sharding.db"),
    dtde => dtde.AddShards("0", "1", "2", "3", "4", "5", "6", "7"));

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
    var context = scope.ServiceProvider.GetRequiredService<HashShardingDbContext>();
    await context.EnsureAllShardsCreatedAsync();
}

app.Run();
