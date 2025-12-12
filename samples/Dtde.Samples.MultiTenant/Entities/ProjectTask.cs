namespace Dtde.Samples.MultiTenant.Entities;

/// <summary>
/// Represents a task within a project.
/// Uses TenantId as shard key for tenant isolation.
/// Sharding configured via fluent API in DbContext.
/// </summary>
public class ProjectTask
{
    public long Id { get; set; }

    /// <summary>
    /// Tenant identifier - co-located with Project for efficient joins.
    /// </summary>
    public required string TenantId { get; set; }

    public long ProjectId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Status { get; set; } // Todo, InProgress, Review, Done
    public required string Priority { get; set; } // Low, Medium, High, Critical
    public string? AssigneeId { get; set; }
    public string? ReporterId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? EstimatedHours { get; set; }
    public string? TagsJson { get; set; }
}
