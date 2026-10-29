namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

// Maps only the columns this service reads. The main Orchid Prism project owns this table.
public class WhatsAppApiSetting
{
    public int Id { get; set; }
    public string? BusinessName { get; set; }
    public string PhoneNumberId { get; set; } = string.Empty;
    public string WhatsAppBusinessAccountId { get; set; } = string.Empty;
    public string? BusinessPhoneNumber { get; set; }
    public string ApiVersion { get; set; } = "v25.0";
    public string? VerifyToken { get; set; }
    public string? AccessToken { get; set; }

    // Meta App Secret used to validate X-Hub-Signature-256. Added by Scripts/whatsapp-app-secret-column.sql.
    public string? AppSecret { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
