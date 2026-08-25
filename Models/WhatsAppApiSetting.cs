namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

public class WhatsAppApiSetting
{
    public int Id { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string WhatsAppBusinessAccountId { get; set; } = string.Empty;
    public string BusinessPhoneNumber { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v25.0";
    public string VerifyToken { get; set; } = string.Empty;
    public string OtpTemplateName { get; set; } = string.Empty;
    public string OtpTemplateLanguageCode { get; set; } = "en";
    public int OtpExpiryMinutes { get; set; }
    public int MaxOtpAttempts { get; set; }
    public int ResendCooldownSeconds { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
