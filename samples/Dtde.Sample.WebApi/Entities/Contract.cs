namespace Dtde.Sample.WebApi.Entities;

/// <summary>
/// Sample entity representing a contract with temporal versioning.
/// </summary>
public class Contract
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Active";

    // Temporal properties - configurable via DTDE
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    // Audit properties
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }

    // Navigation properties
    public ICollection<ContractLineItem> LineItems { get; set; } = [];
}
