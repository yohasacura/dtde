namespace Dtde.Benchmarks.Entities;

/// <summary>
/// Order item entity for single table benchmarks.
/// </summary>
public class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalPrice => Quantity * UnitPrice - Discount;

    // Navigation
    public Order? Order { get; set; }
    public Product? Product { get; set; }
}

/// <summary>
/// Sharded order item entity.
/// </summary>
public class ShardedOrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Region { get; set; } = string.Empty; // Co-located with order
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalPrice => Quantity * UnitPrice - Discount;

    // Navigation
    public ShardedOrder? Order { get; set; }
    public ShardedProduct? Product { get; set; }
}
