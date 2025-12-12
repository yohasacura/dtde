using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.EntityFramework.Extensions;
using Dtde.Sample.WebApi.Data;
using Dtde.Sample.WebApi.Entities;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Configure DTDE with DbContext
builder.Services.AddDtdeDbContext<SampleDbContext>(
    // Configure DbContext
    dbOptions =>
    {
        dbOptions.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=sample.db");
    },
    // Configure DTDE
    dtdeOptions =>
    {
        // Configure metadata for Contract entity
        dtdeOptions.ConfigureEntity<Contract>(entity =>
        {
            entity.HasTemporalValidity(
                validFrom: nameof(Contract.ValidFrom),
                validTo: nameof(Contract.ValidTo));
        });

        // Configure metadata for ContractLineItem entity
        dtdeOptions.ConfigureEntity<ContractLineItem>(entity =>
        {
            entity.HasTemporalValidity(
                validFrom: nameof(ContractLineItem.ValidFrom),
                validTo: nameof(ContractLineItem.ValidTo));
        });

        // Configure shards (using primary for sample)
        dtdeOptions.AddShard(new ShardMetadataBuilder()
            .WithId("Primary")
            .WithName("Primary Shard")
            .WithConnectionString(builder.Configuration.GetConnectionString("DefaultConnection") ?? "")
            .WithTier(ShardTier.Hot)
            .Build());
    });

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    context.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();


