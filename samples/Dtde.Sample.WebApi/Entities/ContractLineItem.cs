namespace Dtde.Sample.WebApi.Entities;

/// <summary>
/// Sample entity representing a line item on a contract.
/// </summary>
public class ContractLineItem
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice => Quantity * UnitPrice;

    // Temporal properties
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    // Navigation
    public Contract? Contract { get; set; }
}
