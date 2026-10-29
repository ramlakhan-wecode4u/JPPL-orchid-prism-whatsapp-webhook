using System.Security.Cryptography;
using System.Text;
using JPPL.OrchidPrism.WhatsAppWebhook.Data;
using Microsoft.EntityFrameworkCore;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Services;

/// <summary>
/// Checks Meta's X-Hub-Signature-256 header so only Meta can post events to the webhook.
/// The signature is an HMAC-SHA256 of the raw request body, keyed with the Meta App Secret.
/// </summary>
public sealed class WhatsAppSignatureValidator(
    AppDbContext db,
    IWebHostEnvironment environment,
    ILogger<WhatsAppSignatureValidator> logger)
{
    public const string SignatureHeaderName = "X-Hub-Signature-256";
    private const string SignaturePrefix = "sha256=";

    public async Task<bool> IsValidAsync(
        byte[] body,
        string? signatureHeader,
        CancellationToken cancellationToken)
    {
        var appSecret = await db.WhatsAppApiSettings
            .AsNoTracking()
            .Where(setting => setting.IsActive)
            .OrderByDescending(setting => setting.Id)
            .Select(setting => setting.AppSecret)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(appSecret))
        {
            // Local testing without a secret is allowed; any other environment fails closed.
            if (environment.IsDevelopment())
            {
                logger.LogWarning("WhatsAppApiSettings.AppSecret is not set; skipping webhook signature check in Development.");
                return true;
            }

            logger.LogError("WhatsAppApiSettings.AppSecret is not set; rejecting webhook request.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader) ||
            !signatureHeader.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] receivedSignature;
        try
        {
            receivedSignature = Convert.FromHexString(signatureHeader[SignaturePrefix.Length..].Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret.Trim()), body);
        return CryptographicOperations.FixedTimeEquals(expectedSignature, receivedSignature);
    }
}
