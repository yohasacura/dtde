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
    app.MapOpenApi();
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
