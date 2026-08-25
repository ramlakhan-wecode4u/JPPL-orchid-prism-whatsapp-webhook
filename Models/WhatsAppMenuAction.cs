namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

public class WhatsAppMenuAction
{
    public int Id { get; set; }
    public string MenuKey { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? NextState { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
