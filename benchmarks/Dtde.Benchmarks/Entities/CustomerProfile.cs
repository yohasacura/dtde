namespace Dtde.Benchmarks.Entities;

/// <summary>
/// Customer profile for nested navigation benchmarks.
/// </summary>
public class CustomerProfile
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? PreferredCurrency { get; set; }
    public bool EmailNotifications { get; set; } = true;
    public bool SmsNotifications { get; set; }
    public int LoyaltyPoints { get; set; }

    public Customer? Customer { get; set; }
    public CustomerPreferences? Preferences { get; set; }
}

/// <summary>
/// Sharded customer profile.
/// </summary>
public class ShardedCustomerProfile
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Region { get; set; } = string.Empty; // Co-located
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? PreferredCurrency { get; set; }
    public bool EmailNotifications { get; set; } = true;
    public bool SmsNotifications { get; set; }
    public int LoyaltyPoints { get; set; }

    public ShardedCustomer? Customer { get; set; }
    public ShardedCustomerPreferences? Preferences { get; set; }
}

/// <summary>
/// Deeply nested preferences for complex navigation benchmarks.
/// </summary>
public class CustomerPreferences
{
    public int Id { get; set; }
    public int CustomerProfileId { get; set; }
    public string? Theme { get; set; }
    public string? FontSize { get; set; }
    public bool DarkMode { get; set; }
    public string? DefaultShippingMethod { get; set; }
    public string? DefaultPaymentMethod { get; set; }

    public CustomerProfile? Profile { get; set; }
}

/// <summary>
/// Sharded customer preferences.
/// </summary>
public class ShardedCustomerPreferences
{
    public int Id { get; set; }
    public int CustomerProfileId { get; set; }
    public string? Theme { get; set; }
    public string? FontSize { get; set; }
    public bool DarkMode { get; set; }
    public string? DefaultShippingMethod { get; set; }
    public string? DefaultPaymentMethod { get; set; }

    public ShardedCustomerProfile? Profile { get; set; }
}
