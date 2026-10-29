using System.Globalization;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Helpers;

public static class MessageFormat
{
    private static readonly CultureInfo IndiaCulture = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>Formats an amount with Indian digit grouping, for example "Rs. 1,23,456.00".</summary>
    public static string Money(decimal amount) =>
        amount < 0
            ? "-Rs. " + (-amount).ToString("N2", IndiaCulture)
            : "Rs. " + amount.ToString("N2", IndiaCulture);

    public static string Date(DateTime value) =>
        value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) =>
        value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
}
