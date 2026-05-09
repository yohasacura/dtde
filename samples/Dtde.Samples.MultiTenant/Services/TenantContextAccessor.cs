namespace Dtde.Samples.MultiTenant.Services;

/// <summary>
/// Provides the current tenant context for requests.
/// </summary>
public interface ITenantContextAccessor
{
    public string? TenantId { get; }
    public void SetTenant(string tenantId);
}

/// <summary>
/// Default implementation using AsyncLocal for tenant isolation.
/// </summary>
public class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<string?> _currentTenant = new();

    public string? TenantId => _currentTenant.Value;

    public void SetTenant(string tenantId)
    {
        _currentTenant.Value = tenantId;
    }
}
