namespace Dtde.Samples.RegionSharding.Entities;

/// <summary>
/// Customer entity sharded by Region.
/// Each region (EU, US, APAC) has its own table in the database.
/// </summary>
/// <example>
/// <code>
/// // Customers are automatically routed to region-specific tables:
/// // - Customers_EU for Region = "EU"
/// // - Customers_US for Region = "US"  
/// // - Customers_APAC for Region = "APAC"
/// </code>
/// </example>
public class Customer
{
    /// <summary>
    /// Unique customer identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Customer name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Customer email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Region code - SHARD KEY.
    /// Used to determine which table stores this customer.
    /// Valid values: EU, US, APAC
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Optional phone number.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Customer address.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Customer tier for business classification.
    /// </summary>
    public CustomerTier Tier { get; set; } = CustomerTier.Standard;

    /// <summary>
    /// When the customer was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property to orders.
    /// </summary>
    public ICollection<Order> Orders { get; set; } = [];
}

/// <summary>
/// Customer tier classification.
/// </summary>
public enum CustomerTier
{
    Standard,
    Silver,
    Gold,
    Platinum
}
