using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.Combined.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "DTDE Combined Sharding Sample",
        Version = "v1",
        Description = """
            Demonstrates combining multiple sharding strategies in a single application.
            This is a comprehensive example showing how different entity types can use
            different sharding strategies based on their access patterns and requirements.
            
            ## Sharding Strategies Configured in DbContext
            
            ### 1. Property-Based Sharding (Accounts)
            - ShardBy(a => a.Region) for data residency compliance
            - Regions: EU, US, APAC
            - Enables regulatory compliance for regional data storage
            
            ### 2. Date-Based Sharding (Transactions)
            - ShardByDate(t => t.TransactionDate, DateShardInterval.Month)
            - Hot data easily accessible in recent shards
            - Historical data automatically partitioned by month
            
            ### 3. Property-Based Sharding (Regulatory Documents)
            - ShardBy(d => d.DocumentType) for logical grouping
            - Document types: Policy, Guideline, Report
            - Organized by regulatory category
            
            ### 4. Hash-Based Sharding (Compliance Audits)
            - ShardByHash(a => a.EntityReference, shardCount: 8)
            - Even distribution across shards
            - Prevents hotspots from sequential access patterns
            
            ## Use Cases
            - Financial services with multi-region compliance
            - Banking systems with transaction history
            - Regulatory document management
            - Audit trail and compliance tracking
            """
    });
});

// Configure DTDE DbContext with combined sharding strategies
// All sharding configuration is in CombinedDbContext.OnModelCreating()
builder.Services.AddDtdeDbContext<CombinedDbContext>(
    dbOptions => dbOptions.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection") 
        ?? "Data Source=combined.db"),
    dtdeOptions =>
    {
        // Sharding is configured in DbContext.OnModelCreating using fluent API
        // No additional builder configuration needed for basic scenarios
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
    var context = scope.ServiceProvider.GetRequiredService<CombinedDbContext>();
    context.Database.EnsureCreated();
}

app.Run();
