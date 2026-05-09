using Dtde.EntityFramework.Extensions;
using Dtde.Sample.WebApi.Data;

using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// One canonical call wires DTDE into EF Core.
//   db    — configure the underlying provider (any EF Core provider works).
//   dtde  — declare the available shards. This sample uses a single primary
//           shard; the temporal entities live in the same database.
builder.Services.AddDtdeDbContext<SampleDbContext>(
    (db, conn) => db.UseSqlite(
        conn
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=sample.db"),
    dtde => dtde.AddShard("Primary"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    await context.EnsureAllShardsCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
