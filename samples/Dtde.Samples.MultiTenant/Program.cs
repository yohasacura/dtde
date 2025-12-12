using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.MultiTenant.Data;
using Dtde.Samples.MultiTenant.Entities;
using Dtde.Samples.MultiTenant.Middleware;
using Dtde.Samples.MultiTenant.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "DTDE Multi-Tenant Sharding Sample",
        Version = "v1",
        Description = """
            Demonstrates multi-tenant sharding with complete data isolation.
            
            ## Multi-Tenant Architecture
            - **Tenant-Based Sharding**: Each tenant's data is isolated via ShardBy(TenantId)
            - **Data Isolation**: Complete tenant data separation
            - **Scalability**: Sharding by TenantId enables horizontal scaling
            - **Co-location**: Related entities (Project, Task, Comment) share same shard
            
            ## Tenant Resolution
            - Via `X-Tenant-Id` header
            - Via route parameter `/api/tenant/{tenantId}/...`
            - Via query parameter `?tenantId=...`
            
            ## Example Tenants
            - `acme-corp`: Enterprise tenant (US)
            - `globex-inc`: Premium tenant (EU)
            - `initech-llc`: Basic tenant (APAC)
            """
    });
});

// Register tenant context accessor
builder.Services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();

// Configure DTDE with multi-tenant sharding
// Sharding is configured in OnModelCreating using ShardBy(e => e.TenantId)
builder.Services.AddDtdeDbContext<MultiTenantDbContext>(
    dbOptions => dbOptions.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection") 
        ?? "Data Source=multitenant.db"),
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

// Add tenant context middleware
app.UseTenantContext();

app.UseAuthorization();
app.MapControllers();

// Ensure databases are created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MultiTenantDbContext>();
    context.Database.EnsureCreated();

    // Seed sample tenants
    if (!context.Tenants.Any())
    {
        context.Tenants.AddRange(
            new Tenant
            {
                TenantId = "acme-corp",
                Name = "Acme Corporation",
                Plan = "Enterprise",
                Domain = "acme.example.com",
                CreatedAt = DateTime.UtcNow
            },
            new Tenant
            {
                TenantId = "globex-inc",
                Name = "Globex Inc",
                Plan = "Premium",
                Domain = "globex.example.com",
                CreatedAt = DateTime.UtcNow
            },
            new Tenant
            {
                TenantId = "initech-llc",
                Name = "Initech LLC",
                Plan = "Basic",
                Domain = "initech.example.com",
                CreatedAt = DateTime.UtcNow
            }
        );
        context.SaveChanges();
    }
}

app.Run();
