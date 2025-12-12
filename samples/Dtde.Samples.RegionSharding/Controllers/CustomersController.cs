using Dtde.Samples.RegionSharding.Data;
using Dtde.Samples.RegionSharding.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.RegionSharding.Controllers;

/// <summary>
/// API controller demonstrating region-based sharding queries.
/// Shows how to query data from specific regions or across all regions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly RegionShardingDbContext _context;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(
        RegionShardingDbContext context,
        ILogger<CustomersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all customers across all regions (fan-out query).
    /// DTDE automatically queries all region shards and merges results.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomerDto>>> GetAllCustomers()
    {
        _logger.LogInformation("Querying customers across all regions");

        // This query fans out to all region shards automatically
        var customers = await _context.Customers
            .OrderBy(c => c.Name)
            .Select(c => new CustomerDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Region = c.Region,
                Tier = c.Tier.ToString()
            })
            .ToListAsync();

        return Ok(customers);
    }

    /// <summary>
    /// Get customers from a specific region (targeted query).
    /// DTDE routes this query to only the relevant shard.
    /// </summary>
    [HttpGet("region/{region}")]
    public async Task<ActionResult<IEnumerable<CustomerDto>>> GetCustomersByRegion(string region)
    {
        _logger.LogInformation("Querying customers for region: {Region}", region);

        // This query is routed to a single shard based on the Region filter
        var customers = await _context.Customers
            .Where(c => c.Region == region.ToUpper())
            .OrderBy(c => c.Name)
            .Select(c => new CustomerDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Region = c.Region,
                Tier = c.Tier.ToString()
            })
            .ToListAsync();

        return Ok(customers);
    }

    /// <summary>
    /// Get a specific customer by ID.
    /// Note: Without region context, this may query all shards.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CustomerDetailDto>> GetCustomer(int id)
    {
        var customer = await _context.Customers
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer is null)
            return NotFound();

        return Ok(new CustomerDetailDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Region = customer.Region,
            Phone = customer.Phone,
            Address = customer.Address,
            Tier = customer.Tier.ToString(),
            OrderCount = customer.Orders.Count
        });
    }

    /// <summary>
    /// Get customer by ID with region hint (optimized single-shard query).
    /// </summary>
    [HttpGet("{id}/region/{region}")]
    public async Task<ActionResult<CustomerDetailDto>> GetCustomerWithRegion(int id, string region)
    {
        _logger.LogInformation("Querying customer {Id} from region {Region}", id, region);

        // Region filter allows DTDE to route to single shard
        var customer = await _context.Customers
            .Include(c => c.Orders)
            .Where(c => c.Region == region.ToUpper())
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer is null)
            return NotFound();

        return Ok(new CustomerDetailDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Region = customer.Region,
            Phone = customer.Phone,
            Address = customer.Address,
            Tier = customer.Tier.ToString(),
            OrderCount = customer.Orders.Count
        });
    }

    /// <summary>
    /// Create a new customer in a specific region.
    /// DTDE routes the insert to the correct shard based on Region.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CustomerDto>> CreateCustomer(CreateCustomerRequest request)
    {
        var region = request.Region?.ToUpper() ?? "US";

        if (!IsValidRegion(region))
        {
            return BadRequest($"Invalid region. Valid regions: EU, US, APAC");
        }

        _logger.LogInformation("Creating customer in region: {Region}", region);

        var customer = new Customer
        {
            Name = request.Name,
            Email = request.Email,
            Region = region,
            Phone = request.Phone,
            Address = request.Address,
            Tier = request.Tier ?? CustomerTier.Standard,
            CreatedAt = DateTime.UtcNow
        };

        // DTDE automatically routes this to the correct shard based on Region
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetCustomerWithRegion),
            new { id = customer.Id, region = customer.Region },
            new CustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                Email = customer.Email,
                Region = customer.Region,
                Tier = customer.Tier.ToString()
            });
    }

    /// <summary>
    /// Get customer count per region (demonstrates aggregation across shards).
    /// </summary>
    [HttpGet("stats/by-region")]
    public async Task<ActionResult<IEnumerable<RegionStats>>> GetStatsByRegion()
    {
        var stats = await _context.Customers
            .GroupBy(c => c.Region)
            .Select(g => new RegionStats
            {
                Region = g.Key,
                CustomerCount = g.Count(),
                TotalOrders = g.SelectMany(c => c.Orders).Count()
            })
            .ToListAsync();

        return Ok(stats);
    }

    /// <summary>
    /// Search customers across all regions.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<CustomerDto>>> SearchCustomers(
        [FromQuery] string? name,
        [FromQuery] string? email,
        [FromQuery] string? region,
        [FromQuery] CustomerTier? tier)
    {
        var query = _context.Customers.AsQueryable();

        // If region is specified, DTDE routes to single shard
        if (!string.IsNullOrEmpty(region))
        {
            query = query.Where(c => c.Region == region.ToUpper());
        }

        if (!string.IsNullOrEmpty(name))
        {
            query = query.Where(c => c.Name.Contains(name));
        }

        if (!string.IsNullOrEmpty(email))
        {
            query = query.Where(c => c.Email.Contains(email));
        }

        if (tier.HasValue)
        {
            query = query.Where(c => c.Tier == tier.Value);
        }

        var customers = await query
            .OrderBy(c => c.Name)
            .Take(100)
            .Select(c => new CustomerDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Region = c.Region,
                Tier = c.Tier.ToString()
            })
            .ToListAsync();

        return Ok(customers);
    }

    private static bool IsValidRegion(string region) =>
        region is "EU" or "US" or "APAC";
}

// DTOs
public record CustomerDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Tier { get; init; } = string.Empty;
}

public record CustomerDetailDto : CustomerDto
{
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public int OrderCount { get; init; }
}

public record CreateCustomerRequest
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public string? Region { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public CustomerTier? Tier { get; init; }
}

public record RegionStats
{
    public string Region { get; init; } = string.Empty;
    public int CustomerCount { get; init; }
    public int TotalOrders { get; init; }
}
