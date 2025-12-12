namespace Dtde.Samples.MultiTenant.Entities;

/// <summary>
/// Represents a tenant organization.
/// </summary>
public class Tenant
{
    public long Id { get; set; }
    public required string TenantId { get; set; }
    public required string Name { get; set; }
    public required string Plan { get; set; } // Free, Basic, Premium, Enterprise
    public string? Domain { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SettingsJson { get; set; }
}
