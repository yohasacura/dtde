using Dtde.Samples.MultiTenant.Data;
using Dtde.Samples.MultiTenant.Entities;
using Dtde.Samples.MultiTenant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.MultiTenant.Controllers;

/// <summary>
/// API controller for tenant-scoped project operations.
/// All operations are automatically scoped to the current tenant.
/// </summary>
[ApiController]
[Route("api/tenant/{tenantId}/projects")]
public class ProjectsController : ControllerBase
{
    private readonly MultiTenantDbContext _context;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        MultiTenantDbContext context,
        ITenantContextAccessor tenantAccessor,
        ILogger<ProjectsController> logger)
    {
        _context = context;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Get all projects for a tenant.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectDto>>> GetProjects(
        string tenantId,
        [FromQuery] string? status)
    {
        _tenantAccessor.SetTenant(tenantId);
        _logger.LogInformation("Fetching projects for tenant {TenantId} (tenant-isolated shard)", tenantId);

        var query = _context.Projects.Where(p => p.TenantId == tenantId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(p => p.Status == status);

        var projects = await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectDto
            {
                Id = p.Id,
                TenantId = p.TenantId,
                Name = p.Name,
                Description = p.Description,
                Status = p.Status,
                OwnerId = p.OwnerId,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync();

        return Ok(projects);
    }

    /// <summary>
    /// Get a specific project.
    /// </summary>
    [HttpGet("{projectId}")]
    public async Task<ActionResult<ProjectDetailDto>> GetProject(string tenantId, long projectId)
    {
        _tenantAccessor.SetTenant(tenantId);

        var project = await _context.Projects
            .Where(p => p.TenantId == tenantId && p.Id == projectId)
            .FirstOrDefaultAsync();

        if (project == null)
            return NotFound();

        var taskCounts = await _context.Tasks
            .Where(t => t.TenantId == tenantId && t.ProjectId == projectId)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        return Ok(new ProjectDetailDto
        {
            Id = project.Id,
            TenantId = project.TenantId,
            Name = project.Name,
            Description = project.Description,
            Status = project.Status,
            OwnerId = project.OwnerId,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
            TaskCounts = taskCounts.ToDictionary(t => t.Status, t => t.Count)
        });
    }

    /// <summary>
    /// Create a new project.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ProjectDto>> CreateProject(string tenantId, CreateProjectRequest request)
    {
        _tenantAccessor.SetTenant(tenantId);
        _logger.LogInformation("Creating project for tenant {TenantId}", tenantId);

        var project = new Project
        {
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            Status = "Active",
            OwnerId = request.OwnerId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetProject), new { tenantId, projectId = project.Id }, new ProjectDto
        {
            Id = project.Id,
            TenantId = project.TenantId,
            Name = project.Name,
            Description = project.Description,
            Status = project.Status,
            OwnerId = project.OwnerId,
            CreatedAt = project.CreatedAt
        });
    }

    /// <summary>
    /// Update a project.
    /// </summary>
    [HttpPut("{projectId}")]
    public async Task<ActionResult<ProjectDto>> UpdateProject(
        string tenantId, long projectId, UpdateProjectRequest request)
    {
        _tenantAccessor.SetTenant(tenantId);

        var project = await _context.Projects
            .Where(p => p.TenantId == tenantId && p.Id == projectId)
            .FirstOrDefaultAsync();

        if (project == null)
            return NotFound();

        if (request.Name != null)
            project.Name = request.Name;
        if (request.Description != null)
            project.Description = request.Description;
        if (request.Status != null)
        {
            project.Status = request.Status;
            if (request.Status == "Archived")
                project.ArchivedAt = DateTime.UtcNow;
        }

        project.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new ProjectDto
        {
            Id = project.Id,
            TenantId = project.TenantId,
            Name = project.Name,
            Description = project.Description,
            Status = project.Status,
            OwnerId = project.OwnerId,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        });
    }

    /// <summary>
    /// Delete a project.
    /// </summary>
    [HttpDelete("{projectId}")]
    public async Task<ActionResult> DeleteProject(string tenantId, long projectId)
    {
        _tenantAccessor.SetTenant(tenantId);

        var project = await _context.Projects
            .Where(p => p.TenantId == tenantId && p.Id == projectId)
            .FirstOrDefaultAsync();

        if (project == null)
            return NotFound();

        // Soft delete
        project.Status = "Deleted";
        project.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted project {ProjectId} for tenant {TenantId}", projectId, tenantId);

        return NoContent();
    }
}

// DTOs
public record ProjectDto
{
    public long Id { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? OwnerId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public record ProjectDetailDto : ProjectDto
{
    public Dictionary<string, int> TaskCounts { get; init; } = [];
}

public record CreateProjectRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? OwnerId { get; init; }
}

public record UpdateProjectRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Status { get; init; }
}
