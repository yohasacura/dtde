namespace Dtde.Samples.MultiTenant.Entities;

/// <summary>
/// Represents a comment on a task.
/// Uses TenantId as shard key for tenant isolation.
/// Sharding configured via fluent API in DbContext.
/// </summary>
public class TaskComment
{
    public long Id { get; set; }

    /// <summary>
    /// Tenant identifier - co-located with Project and Task.
    /// </summary>
    public required string TenantId { get; set; }

    public long TaskId { get; set; }
    public required string AuthorId { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
}
