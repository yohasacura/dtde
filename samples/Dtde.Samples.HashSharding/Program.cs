using Dtde.EntityFramework.Extensions;
using Dtde.Samples.HashSharding.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "DTDE Hash Sharding Sample",
        Version = "v1",
        Description = """
            Demonstrates hash-based sharding with consistent distribution.
            
            ## Hash Sharding Benefits
            - **Even Distribution**: Hash function ensures balanced data across shards
            - **Predictable Routing**: Same key always routes to same shard
            - **Co-location**: Related entities with same shard key stay together
            - **Scalable**: Easy to add more shards for horizontal scaling
            
            ## Shard Configuration
            - 8 hash shards (0-7) for even distribution
            - All user data co-located by UserId
            - Consistent hashing for predictable routing
            """
    });
});

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
    app.UseSwagger();
    app.UseSwaggerUI();
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
