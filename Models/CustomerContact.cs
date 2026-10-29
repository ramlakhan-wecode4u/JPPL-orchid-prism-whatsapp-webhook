namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

public class CustomerContact
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? WhatsAppNumber { get; set; }
    public bool IsWhatsAppVerified { get; set; }
    public DateTime? WhatsAppVerifiedAt { get; set; }
    public bool CanSeeRates { get; set; }
    public bool IsSendNotifications { get; set; } = true;
}
