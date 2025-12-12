namespace Dtde.Sample.WebApi.Entities;

/// <summary>
/// Regular EF Core entity (NOT temporal).
/// Demonstrates that DTDE works alongside standard EF Core entities.
/// </summary>
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
