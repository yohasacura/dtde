using Dtde.EntityFramework.Extensions;
using Dtde.Samples.HashSharding.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Configure DTDE with hash-based sharding
builder.Services.AddDtdeDbContext<HashShardingDbContext>(
    dbOptions =>
    {
        dbOptions.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
    },
    dtdeOptions =>
    {
        // Sharding is configured via fluent API in DbContext.OnModelCreating
        // The ShardByHash() calls define hash-based sharding for each entity
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<HashShardingDbContext>();
    context.Database.EnsureCreated();
}

app.Run();
