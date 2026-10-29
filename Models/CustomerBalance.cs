namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

// Read-only mapping of the main project's CustomerBalances table (advance money held for the customer).
public class CustomerBalance
{
    public long Id { get; set; }
    public int CustomerId { get; set; }
    public decimal BalanceAmount { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
