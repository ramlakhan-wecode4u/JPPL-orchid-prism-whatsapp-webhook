namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

// Read-only mapping of the main project's CustomerPaymentTerms table.
public class CustomerPaymentTerm
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int PaymentTermId { get; set; }
    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
