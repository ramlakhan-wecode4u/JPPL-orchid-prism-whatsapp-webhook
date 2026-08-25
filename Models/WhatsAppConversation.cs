namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

public class WhatsAppConversation
{
    public long Id { get; set; }
    public string WhatsAppNumber { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public string CurrentState { get; set; } = "MAIN_MENU";
    public string? LastMessageId { get; set; }
    public string? ContextJson { get; set; }
    public DateTime LastInteractionAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
