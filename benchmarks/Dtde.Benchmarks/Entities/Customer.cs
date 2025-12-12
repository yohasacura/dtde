namespace Dtde.Benchmarks.Entities;

/// <summary>
/// Customer entity for single table benchmarks.
/// </summary>
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public CustomerTier Tier { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public List<Order> Orders { get; set; } = [];
    public CustomerProfile? Profile { get; set; }
}

/// <summary>
/// Sharded customer entity - same structure but configured for sharding.
/// </summary>
public class ShardedCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public CustomerTier Tier { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public List<ShardedOrder> Orders { get; set; } = [];
    public ShardedCustomerProfile? Profile { get; set; }
}

public enum CustomerTier
{
    Bronze,
    Standard,
    Silver,
    Gold,
    Platinum
}
