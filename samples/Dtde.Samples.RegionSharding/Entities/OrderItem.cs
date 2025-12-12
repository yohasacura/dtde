namespace Dtde.Samples.RegionSharding.Entities;

/// <summary>
/// Order line item - follows the order's region sharding.
/// </summary>
public class OrderItem
{
    /// <summary>
    /// Unique item identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to Order.
    /// </summary>
    public int OrderId { get; set; }

    /// <summary>
    /// Region code - SHARD KEY (denormalized for routing).
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Product SKU.
    /// </summary>
    public string ProductSku { get; set; } = string.Empty;

    /// <summary>
    /// Product name.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Quantity ordered.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Unit price at time of order.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Line total (Quantity * UnitPrice).
    /// </summary>
    public decimal LineTotal => Quantity * UnitPrice;

    /// <summary>
    /// Navigation property to Order.
    /// </summary>
    public Order? Order { get; set; }
}
