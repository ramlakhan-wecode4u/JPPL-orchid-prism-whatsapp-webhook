namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

// Read-only mapping of the main project's EstimateVersions table.
public class EstimateVersion
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public decimal TotalPrice { get; set; }
}
