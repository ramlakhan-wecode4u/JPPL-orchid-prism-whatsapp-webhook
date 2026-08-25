namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

public class WhatsAppWebhookEvent
{
    public long Id { get; set; }
    public string? EventType { get; set; }
    public string? MetaMessageId { get; set; }
    public string? WhatsAppNumber { get; set; }
    public string RawPayload { get; set; } = string.Empty;
    public bool Processed { get; set; }
    public string? ProcessingError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
