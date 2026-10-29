namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StudioName { get; set; } = string.Empty;
    public string? WhatsAppNumber { get; set; }
    public bool IsWhatsAppVerified { get; set; }
    public DateTime? WhatsAppVerifiedAt { get; set; }
    public bool IsActive { get; set; }
}
