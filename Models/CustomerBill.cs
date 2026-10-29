namespace JPPL.OrchidPrism.WhatsAppWebhook.Models;

// Read-only mapping of the main project's CustomerBills table.
public class CustomerBill
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int OrderId { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public DateTime BillDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public DateOnly? CreditDueDate { get; set; }
    public string Status { get; set; } = string.Empty;
}
