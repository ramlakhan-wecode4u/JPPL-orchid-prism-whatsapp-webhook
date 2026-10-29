namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

// Read-only mapping of the main project's Orders table.
public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int StatusId { get; set; }
    public int? CurrentEstimateVersionId { get; set; }
    public int? ApprovedEstimateVersionId { get; set; }
}
