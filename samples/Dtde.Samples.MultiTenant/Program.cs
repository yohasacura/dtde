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
builder.Services.AddOpenApi();

// Register tenant context accessor
builder.Services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();

// Configure DTDE with multi-tenant sharding. Each tenant gets its own
// per-tenant table (Projects_acme, Projects_globex, ...) inside the same DB.
builder.Services.AddDtdeDbContext<MultiTenantDbContext>(
    (db, conn) => db.UseSqlite(
        conn
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=multitenant.db"),
    dtde => dtde.AddShards("acme", "globex", "initech"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Add tenant context middleware (extracts tenant id from header / route / query)
app.UseTenantContext();

app.MapControllers();

// Ensure databases are created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MultiTenantDbContext>();
    await context.EnsureAllShardsCreatedAsync();

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
