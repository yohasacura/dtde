namespace Dtde.Samples.MultiTenant.Middleware;

using Dtde.Samples.MultiTenant.Services;

/// <summary>
/// Middleware that extracts tenant ID from request headers.
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantAccessor)
    {
        // Extract tenant from header
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantId) &&
            !string.IsNullOrEmpty(tenantId))
        {
            tenantAccessor.SetTenant(tenantId.ToString() ?? string.Empty);
            _logger.LogDebug("Tenant context set to: {TenantId}", tenantId.ToString());
        }
        else
        {
            // Try to extract from route or query string
            if (context.Request.RouteValues.TryGetValue("tenantId", out var routeTenant))
            {
                tenantAccessor.SetTenant(routeTenant?.ToString()!);
            }
            else if (context.Request.Query.TryGetValue("tenantId", out var queryTenant))
            {
                tenantAccessor.SetTenant(queryTenant!);
            }
        }

        await _next(context);
    }
}

public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TenantMiddleware>();
    }
}
