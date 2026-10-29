using JPPL.OrchidPrism.WhatsAppWebhook.Data;
using JPPL.OrchidPrism.WhatsAppWebhook.Helpers;
using JPPL.OrchidPrism.WhatsAppWebhook.Models;
using Microsoft.EntityFrameworkCore;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Services;

/// <summary>
/// Finds which customer accounts an incoming WhatsApp number belongs to.
/// Only verified numbers on active customers are accepted, either the customer's own number
/// or a verified customer contact number.
/// </summary>
public sealed class WhatsAppAccountResolver(AppDbContext db)
{
    /// <summary>
    /// Returns every account the number may use, sorted by account name. One person can own several
    /// studios, so more than one account is valid; the menu asks the user to pick one.
    /// </summary>
    public async Task<IReadOnlyList<LinkedAccount>> FindAccountsAsync(
        string whatsAppNumber,
        CancellationToken cancellationToken)
    {
        var candidates = PhoneNumberHelper.GetStoredFormCandidates(whatsAppNumber);
        if (candidates.Length == 0)
        {
            return [];
        }

        // Stored numbers may contain spaces, dashes, brackets or "+", so strip them in SQL before comparing.
        var customers = await db.Customers
            .AsNoTracking()
            .Where(customer =>
                customer.IsActive &&
                customer.IsWhatsAppVerified &&
                customer.WhatsAppNumber != null &&
                candidates.Contains(customer.WhatsAppNumber
                    .Replace(" ", "").Replace("-", "").Replace("+", "")
                    .Replace("(", "").Replace(")", "").Replace(".", "")))
            .ToListAsync(cancellationToken);

        var contacts = await (
                from contact in db.CustomerContacts.AsNoTracking()
                join customer in db.Customers.AsNoTracking() on contact.CustomerId equals customer.Id
                where customer.IsActive &&
                      contact.IsWhatsAppVerified &&
                      contact.WhatsAppNumber != null &&
                      candidates.Contains(contact.WhatsAppNumber
                          .Replace(" ", "").Replace("-", "").Replace("+", "")
                          .Replace("(", "").Replace(")", "").Replace(".", ""))
                select new { Contact = contact, Customer = customer })
            .ToListAsync(cancellationToken);

        var accounts = new List<LinkedAccount>();

        // The customer's own verified number gives full access to that account.
        foreach (var customer in customers)
        {
            accounts.Add(new LinkedAccount(customer, null, customer.Name, CanSeeRates: true));
        }

        // A verified contact number gives access to the contact's customer, unless the number is also
        // the customer's own number. Several contacts on one customer get the most restrictive access.
        foreach (var group in contacts.GroupBy(item => item.Customer.Id))
        {
            if (accounts.Any(account => account.Customer.Id == group.Key))
            {
                continue;
            }

            var contactMatch = group
                .OrderBy(item => item.Contact.CanSeeRates)
                .ThenBy(item => item.Contact.Id)
                .First();

            accounts.Add(new LinkedAccount(
                contactMatch.Customer,
                contactMatch.Contact.Id,
                contactMatch.Contact.Name,
                contactMatch.Contact.CanSeeRates));
        }

        return accounts
            .OrderBy(account => account.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.Customer.Id)
            .ToList();
    }
}

public sealed record LinkedAccount(
    Customer Customer,
    int? ContactId,
    string DisplayName,
    bool CanSeeRates)
{
    /// <summary>Studio name when available, otherwise the customer name.</summary>
    public string AccountName =>
        string.IsNullOrWhiteSpace(Customer.StudioName) ? Customer.Name : Customer.StudioName;
}
