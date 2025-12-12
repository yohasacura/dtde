using Dtde.Core.Metadata;
using Dtde.EntityFramework;
using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Integration.Tests.Temporal;

public class TemporalQueryIntegrationTests : IDisposable
{
    private readonly TemporalTestDbContext _context;

    public TemporalQueryIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TemporalTestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TemporalTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact(DisplayName = "Temporal query with LINQ Where clause combines correctly")]
    public async Task TemporalQuery_WithLinqWhereClause_CombinesCorrectly()
    {
        var products = new[]
        {
            new Product { Id = 1, Name = "Product A", Category = "Electronics", Price = 100, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null },
            new Product { Id = 2, Name = "Product B", Category = "Electronics", Price = 200, ValidFrom = new DateTime(2024, 1, 1), ValidTo = new DateTime(2024, 6, 30) },
            new Product { Id = 3, Name = "Product C", Category = "Clothing", Price = 50, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null },
            new Product { Id = 4, Name = "Product D", Category = "Electronics", Price = 300, ValidFrom = new DateTime(2024, 7, 1), ValidTo = null }
        };

        await _context.Products.AddRangeAsync(products);
        await _context.SaveChangesAsync();

        var mayElectronics = await _context.ValidAt<Product>(new DateTime(2024, 5, 15))
            .Where(p => p.Category == "Electronics")
            .ToListAsync();

        Assert.Equal(2, mayElectronics.Count);
        var names = mayElectronics.Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(["Product A", "Product B"], names);
    }

    [Fact(DisplayName = "Temporal query with OrderBy works correctly")]
    public async Task TemporalQuery_WithOrderBy_WorksCorrectly()
    {
        var products = new[]
        {
            new Product { Id = 1, Name = "Zebra", Price = 100, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null },
            new Product { Id = 2, Name = "Apple", Price = 200, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null },
            new Product { Id = 3, Name = "Mango", Price = 150, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null }
        };

        await _context.Products.AddRangeAsync(products);
        await _context.SaveChangesAsync();

        var orderedByName = await _context.ValidAt<Product>(new DateTime(2024, 6, 1))
            .OrderBy(p => p.Name)
            .ToListAsync();

        var orderedByPrice = await _context.ValidAt<Product>(new DateTime(2024, 6, 1))
            .OrderByDescending(p => p.Price)
            .ToListAsync();

        Assert.Equal("Apple", orderedByName[0].Name);
        Assert.Equal("Mango", orderedByName[1].Name);
        Assert.Equal("Zebra", orderedByName[2].Name);

        Assert.Equal(200, orderedByPrice[0].Price);
        Assert.Equal(150, orderedByPrice[1].Price);
        Assert.Equal(100, orderedByPrice[2].Price);
    }

    [Fact(DisplayName = "Temporal query with Select projection works correctly")]
    public async Task TemporalQuery_WithSelectProjection_WorksCorrectly()
    {
        var products = new[]
        {
            new Product { Id = 1, Name = "Product A", Category = "Cat1", Price = 100, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null },
            new Product { Id = 2, Name = "Product B", Category = "Cat2", Price = 200, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null }
        };

        await _context.Products.AddRangeAsync(products);
        await _context.SaveChangesAsync();

        var projected = await _context.ValidAt<Product>(new DateTime(2024, 6, 1))
            .Select(p => new { p.Name, p.Price })
            .ToListAsync();

        Assert.Equal(2, projected.Count);
        Assert.Contains(projected, p => p.Name == "Product A" && p.Price == 100);
        Assert.Contains(projected, p => p.Name == "Product B" && p.Price == 200);
    }

    [Fact(DisplayName = "Temporal query with aggregation works correctly")]
    public async Task TemporalQuery_WithAggregation_WorksCorrectly()
    {
        var products = new[]
        {
            new Product { Id = 1, Name = "P1", Price = 100, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null },
            new Product { Id = 2, Name = "P2", Price = 200, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null },
            new Product { Id = 3, Name = "P3", Price = 300, ValidFrom = new DateTime(2024, 1, 1), ValidTo = new DateTime(2024, 3, 31) }
        };

        await _context.Products.AddRangeAsync(products);
        await _context.SaveChangesAsync();

        var count = await _context.ValidAt<Product>(new DateTime(2024, 6, 1)).CountAsync();
        var sum = await _context.ValidAt<Product>(new DateTime(2024, 6, 1)).SumAsync(p => p.Price);
        var avg = await _context.ValidAt<Product>(new DateTime(2024, 6, 1)).AverageAsync(p => p.Price);

        Assert.Equal(2, count);
        Assert.Equal(300, sum);
        Assert.Equal(150, avg);
    }

    [Fact(DisplayName = "Temporal query First/Single work correctly")]
    public async Task TemporalQuery_FirstSingle_WorkCorrectly()
    {
        var products = new[]
        {
            new Product { Id = 1, Name = "Unique", Price = 100, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null },
            new Product { Id = 2, Name = "Other", Price = 200, ValidFrom = new DateTime(2024, 7, 1), ValidTo = null }
        };

        await _context.Products.AddRangeAsync(products);
        await _context.SaveChangesAsync();

        var first = await _context.ValidAt<Product>(new DateTime(2024, 6, 1))
            .FirstAsync();

        var single = await _context.ValidAt<Product>(new DateTime(2024, 6, 1))
            .SingleAsync(p => p.Name == "Unique");

        Assert.Equal("Unique", first.Name);
        Assert.Equal(100, single.Price);
    }

    [Fact(DisplayName = "Temporal query Any/All work correctly")]
    public async Task TemporalQuery_AnyAll_WorkCorrectly()
    {
        var products = new[]
        {
            new Product { Id = 1, Name = "Expensive", Price = 1000, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null },
            new Product { Id = 2, Name = "Cheap", Price = 10, ValidFrom = new DateTime(2024, 1, 1), ValidTo = null }
        };

        await _context.Products.AddRangeAsync(products);
        await _context.SaveChangesAsync();

        var anyExpensive = await _context.ValidAt<Product>(new DateTime(2024, 6, 1))
            .AnyAsync(p => p.Price > 500);

        var allExpensive = await _context.ValidAt<Product>(new DateTime(2024, 6, 1))
            .AllAsync(p => p.Price > 500);

        Assert.True(anyExpensive);
        Assert.False(allExpensive);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class TemporalTestDbContext : DtdeDbContext
{
    public DbSet<Product> Products => Set<Product>();

    public TemporalTestDbContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        optionsBuilder.UseDtde(dtde =>
        {
            dtde.ConfigureEntity<Product>(entity =>
            {
                entity.HasTemporalValidity(
                    validFrom: nameof(Product.ValidFrom),
                    validTo: nameof(Product.ValidTo));
            });

            dtde.EnableTestMode();
        });
    }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
