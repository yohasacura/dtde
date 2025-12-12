namespace Dtde.Samples.RegionSharding.Entities;

/// <summary>
/// Order entity - follows the same region sharding as Customer.
/// Orders are sharded based on the customer's region.
/// </summary>
public class Order
{
    /// <summary>
    /// Unique order identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Order number for display.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to Customer.
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// Region code - SHARD KEY (denormalized from Customer for routing).
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Order date.
    /// </summary>
    public DateTime OrderDate { get; set; }

    /// <summary>
    /// Total amount.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Order status.
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    /// Currency code (EUR for EU, USD for US, etc.).
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Shipping address.
    /// </summary>
    public string? ShippingAddress { get; set; }

    /// <summary>
    /// Navigation property to Customer.
    /// </summary>
    public Customer? Customer { get; set; }

    /// <summary>
    /// Order line items.
    /// </summary>
    public ICollection<OrderItem> Items { get; set; } = [];
}

/// <summary>
/// Order status enumeration.
/// </summary>
public enum OrderStatus
{
    Pending,
    Confirmed,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}
