namespace Dtde.Sample.WebApi.Entities;

/// <summary>
/// Regular EF Core entity (NOT temporal).
/// Demonstrates that DTDE works alongside standard EF Core entities.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? UserId { get; set; }
    public string? Details { get; set; }
}
