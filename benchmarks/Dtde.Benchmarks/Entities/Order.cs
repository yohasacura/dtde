namespace Dtde.Benchmarks.Entities;

/// <summary>
/// Order entity for single table benchmarks (non-sharded).
/// </summary>
public class Order
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string Region { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public OrderStatus Status { get; set; }
    public string? ShippingAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // Navigation properties
    public Customer? Customer { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

/// <summary>
/// Sharded order entity - same structure but configured for sharding.
/// </summary>
public class ShardedOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string Region { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public OrderStatus Status { get; set; }
    public string? ShippingAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // Navigation properties
    public ShardedCustomer? Customer { get; set; }
    public List<ShardedOrderItem> Items { get; set; } = [];
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}
