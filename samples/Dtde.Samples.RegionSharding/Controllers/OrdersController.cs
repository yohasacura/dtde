using Dtde.Samples.RegionSharding.Data;
using Dtde.Samples.RegionSharding.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.RegionSharding.Controllers;

/// <summary>
/// API controller demonstrating cross-region order queries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly RegionShardingDbContext _context;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        RegionShardingDbContext context,
        ILogger<OrdersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get orders for a specific region (targeted query).
    /// </summary>
    [HttpGet("region/{region}")]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrdersByRegion(
        string region,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        _logger.LogInformation("Querying orders for region: {Region}", region);

        var query = _context.Orders
            .Where(o => o.Region == region.ToUpper());

        if (fromDate.HasValue)
            query = query.Where(o => o.OrderDate >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(o => o.OrderDate <= toDate.Value);

        var orders = await query
            .OrderByDescending(o => o.OrderDate)
            .Take(100)
            .Select(o => new OrderDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                CustomerId = o.CustomerId,
                Region = o.Region,
                OrderDate = o.OrderDate,
                TotalAmount = o.TotalAmount,
                Currency = o.Currency,
                Status = o.Status.ToString()
            })
            .ToListAsync();

        return Ok(orders);
    }

    /// <summary>
    /// Get orders for a specific customer (uses customer's region for routing).
    /// </summary>
    [HttpGet("customer/{customerId}")]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrdersByCustomer(
        int customerId,
        [FromQuery] string? region)
    {
        var query = _context.Orders
            .Where(o => o.CustomerId == customerId);

        // If region is provided, route to single shard
        if (!string.IsNullOrEmpty(region))
        {
            query = query.Where(o => o.Region == region.ToUpper());
        }

        var orders = await query
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                CustomerId = o.CustomerId,
                Region = o.Region,
                OrderDate = o.OrderDate,
                TotalAmount = o.TotalAmount,
                Currency = o.Currency,
                Status = o.Status.ToString()
            })
            .ToListAsync();

        return Ok(orders);
    }

    /// <summary>
    /// Create a new order (region is inherited from customer).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderRequest request)
    {
        // Get customer to determine region
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId);

        if (customer is null)
            return NotFound($"Customer {request.CustomerId} not found");

        _logger.LogInformation(
            "Creating order for customer {CustomerId} in region {Region}",
            request.CustomerId,
            customer.Region);

        var order = new Order
        {
            OrderNumber = GenerateOrderNumber(customer.Region),
            CustomerId = request.CustomerId,
            Region = customer.Region, // Inherit region from customer
            OrderDate = DateTime.UtcNow,
            TotalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice),
            Currency = GetCurrencyForRegion(customer.Region),
            Status = OrderStatus.Pending,
            ShippingAddress = request.ShippingAddress ?? customer.Address,
            Items = request.Items.Select(i => new OrderItem
            {
                Region = customer.Region,
                ProductSku = i.ProductSku,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetOrdersByRegion),
            new { region = order.Region },
            new OrderDto
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                CustomerId = order.CustomerId,
                Region = order.Region,
                OrderDate = order.OrderDate,
                TotalAmount = order.TotalAmount,
                Currency = order.Currency,
                Status = order.Status.ToString()
            });
    }

    /// <summary>
    /// Get order statistics across all regions.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<OrderStats>> GetOrderStats()
    {
        // Fan-out query across all region shards
        var stats = await _context.Orders
            .GroupBy(o => o.Region)
            .Select(g => new RegionOrderStats
            {
                Region = g.Key,
                OrderCount = g.Count(),
                TotalRevenue = g.Sum(o => o.TotalAmount),
                AverageOrderValue = g.Average(o => o.TotalAmount)
            })
            .ToListAsync();

        return Ok(new OrderStats
        {
            TotalOrders = stats.Sum(s => s.OrderCount),
            TotalRevenue = stats.Sum(s => s.TotalRevenue),
            RegionBreakdown = stats
        });
    }

    private static string GenerateOrderNumber(string region) =>
        $"{region}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

    private static string GetCurrencyForRegion(string region) => region switch
    {
        "EU" => "EUR",
        "US" => "USD",
        "APAC" => "SGD",
        _ => "USD"
    };
}

// DTOs
public record OrderDto
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string Region { get; init; } = string.Empty;
    public DateTime OrderDate { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public record CreateOrderRequest
{
    public int CustomerId { get; init; }
    public string? ShippingAddress { get; init; }
    public required List<OrderItemRequest> Items { get; init; }
}

public record OrderItemRequest
{
    public required string ProductSku { get; init; }
    public required string ProductName { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

public record OrderStats
{
    public int TotalOrders { get; init; }
    public decimal TotalRevenue { get; init; }
    public IEnumerable<RegionOrderStats> RegionBreakdown { get; init; } = [];
}

public record RegionOrderStats
{
    public string Region { get; init; } = string.Empty;
    public int OrderCount { get; init; }
    public decimal TotalRevenue { get; init; }
    public decimal AverageOrderValue { get; init; }
}
