namespace JPPL.OrchidPrism.WhatsAppWebhook.Helpers;

public static class PhoneNumberHelper
{
    private const string IndiaCountryCode = "91";

    /// <summary>
    /// Converts a phone number to international digits only, for example "+91 98765-43210" to "919876543210".
    /// A 10-digit number (or one with a single leading trunk 0) is treated as an Indian mobile number.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.Length == 11 && digits[0] == '0')
        {
            digits = digits[1..];
        }

        if (digits.Length == 10)
        {
            digits = IndiaCountryCode + digits;
        }

        return digits.Length is >= 11 and <= 15 && digits[0] != '0' ? digits : null;
    }

    /// <summary>
    /// Returns every digits-only form the main project may have stored for the same number.
    /// Compare these against stored values after removing separators such as spaces, dashes and "+".
    /// </summary>
    public static string[] GetStoredFormCandidates(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return [];
        }

        if (normalized.Length == 12 && normalized.StartsWith(IndiaCountryCode, StringComparison.Ordinal))
        {
            var localNumber = normalized[2..];
            return [normalized, localNumber, "0" + localNumber, "00" + normalized];
        }

        return [normalized, "00" + normalized];
    }
}
