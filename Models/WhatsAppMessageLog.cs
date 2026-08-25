namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

public class WhatsAppMessageLog
{
    public long Id { get; set; }
    public long? CustomerId { get; set; }
    public string? WhatsAppNumber { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string? TemplateName { get; set; }
    public string? MetaMessageId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Payload { get; set; }
    public string? ApiResponse { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
