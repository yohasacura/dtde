using Dtde.Samples.MultiTenant.Data;
using Dtde.Samples.MultiTenant.Entities;
using Dtde.Samples.MultiTenant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.MultiTenant.Controllers;

/// <summary>
/// API controller for tenant management (admin operations).
/// </summary>
[ApiController]
[Route("api/admin/tenants")]
public class TenantsController : ControllerBase
{
    private readonly MultiTenantDbContext _context;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(
        MultiTenantDbContext context,
        ILogger<TenantsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// List all tenants (admin only).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TenantDto>>> GetTenants()
    {
        var tenants = await _context.Tenants
            .Select(t => new TenantDto
            {
                TenantId = t.TenantId,
                Name = t.Name,
                Plan = t.Plan,
                Domain = t.Domain,
                CreatedAt = t.CreatedAt,
                IsActive = t.IsActive
            })
            .ToListAsync();

        return Ok(tenants);
    }

    /// <summary>
    /// Get a specific tenant.
    /// </summary>
    [HttpGet("{tenantId}")]
    public async Task<ActionResult<TenantDto>> GetTenant(string tenantId)
    {
        var tenant = await _context.Tenants
            .Where(t => t.TenantId == tenantId)
            .Select(t => new TenantDto
            {
                TenantId = t.TenantId,
                Name = t.Name,
                Plan = t.Plan,
                Domain = t.Domain,
                CreatedAt = t.CreatedAt,
                IsActive = t.IsActive
            })
            .FirstOrDefaultAsync();

        if (tenant == null)
            return NotFound();

        return Ok(tenant);
    }

    /// <summary>
    /// Create a new tenant.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TenantDto>> CreateTenant(CreateTenantRequest request)
    {
        var exists = await _context.Tenants.AnyAsync(t => t.TenantId == request.TenantId);
        if (exists)
            return Conflict($"Tenant {request.TenantId} already exists");

        var tenant = new Tenant
        {
            TenantId = request.TenantId,
            Name = request.Name,
            Plan = request.Plan,
            Domain = request.Domain,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created new tenant: {TenantId}", request.TenantId);

        return CreatedAtAction(nameof(GetTenant), new { tenantId = tenant.TenantId }, new TenantDto
        {
            TenantId = tenant.TenantId,
            Name = tenant.Name,
            Plan = tenant.Plan,
            Domain = tenant.Domain,
            CreatedAt = tenant.CreatedAt,
            IsActive = tenant.IsActive
        });
    }

    /// <summary>
    /// Update tenant plan.
    /// </summary>
    [HttpPatch("{tenantId}/plan")]
    public async Task<ActionResult> UpdateTenantPlan(string tenantId, UpdatePlanRequest request)
    {
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
        if (tenant == null)
            return NotFound();

        var oldPlan = tenant.Plan;
        tenant.Plan = request.Plan;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated tenant {TenantId} plan from {OldPlan} to {NewPlan}",
            tenantId, oldPlan, request.Plan);

        return NoContent();
    }

    /// <summary>
    /// Deactivate a tenant.
    /// </summary>
    [HttpPost("{tenantId}/deactivate")]
    public async Task<ActionResult> DeactivateTenant(string tenantId)
    {
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
        if (tenant == null)
            return NotFound();

        tenant.IsActive = false;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deactivated tenant: {TenantId}", tenantId);

        return NoContent();
    }

    /// <summary>
    /// Get tenant statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<TenantStats>> GetTenantStats()
    {
        var stats = await _context.Tenants
            .GroupBy(t => 1)
            .Select(g => new TenantStats
            {
                TotalTenants = g.Count(),
                ActiveTenants = g.Count(t => t.IsActive),
                TenantsByPlan = g.GroupBy(t => t.Plan)
                    .Select(p => new PlanStats
                    {
                        Plan = p.Key,
                        Count = p.Count()
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync() ?? new TenantStats();

        return Ok(stats);
    }
}

// DTOs
public record TenantDto
{
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Plan { get; init; } = string.Empty;
    public string? Domain { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsActive { get; init; }
}

public record CreateTenantRequest
{
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required string Plan { get; init; }
    public string? Domain { get; init; }
}

public record UpdatePlanRequest
{
    public required string Plan { get; init; }
}

public record TenantStats
{
    public int TotalTenants { get; init; }
    public int ActiveTenants { get; init; }
    public List<PlanStats> TenantsByPlan { get; init; } = [];
}

public record PlanStats
{
    public string Plan { get; init; } = string.Empty;
    public int Count { get; init; }
}
