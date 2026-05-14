namespace Dtde.Samples.BulkOperations.Entities;

public class AppEvent
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
