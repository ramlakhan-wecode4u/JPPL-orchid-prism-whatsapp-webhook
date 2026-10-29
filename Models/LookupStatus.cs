namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

// Read-only mapping of the main project's LuStatus table.
public class LookupStatus
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
