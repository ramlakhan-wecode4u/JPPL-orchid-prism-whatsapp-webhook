namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

public class WhatsAppConversationMessage
{
    public long Id { get; set; }
    public long? ConversationId { get; set; }
    public long? CustomerId { get; set; }
    public string WhatsAppNumber { get; set; } = string.Empty;
    public string? MetaMessageId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string? UserText { get; set; }
    public string? InteractiveId { get; set; }
    public string? InteractiveTitle { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
