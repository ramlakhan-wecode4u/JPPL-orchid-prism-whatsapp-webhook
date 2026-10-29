namespace JPPL.OrchidPrism.WhatsAppWebhook.Options;

/// <summary>
/// Support contact details shown when a customer picks Support. Bound from the "Support" section of appsettings.json.
/// </summary>
public sealed class SupportContactOptions
{
    public const string SectionName = "Support";

    public string? Phone { get; set; }
    public string? Email { get; set; }
}
