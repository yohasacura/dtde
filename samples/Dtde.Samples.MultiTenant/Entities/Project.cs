namespace Dtde.Samples.MultiTenant.Entities;

/// <summary>
/// Represents a project within a tenant's workspace.
/// Uses TenantId as shard key for complete tenant data isolation.
/// Sharding configured via fluent API in DbContext.
/// </summary>
public class Project
{
    public long Id { get; set; }

    /// <summary>
    /// Tenant identifier - ensures complete data isolation per tenant.
    /// </summary>
    public required string TenantId { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Status { get; set; } // Active, Archived, Deleted
    public string? OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string? SettingsJson { get; set; }
}
