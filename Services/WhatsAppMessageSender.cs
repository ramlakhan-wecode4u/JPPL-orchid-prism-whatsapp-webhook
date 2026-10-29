using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JPPL.OrchidPrism.WhatsAppWebhook.Data;
using JPPL.OrchidPrism.WhatsAppWebhook.Models;
using Microsoft.EntityFrameworkCore;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Services;

public sealed class WhatsAppMessageSender(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<WhatsAppMessageSender> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Meta rejects explicit nulls, for example a list row with "description": null.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task SendTextAsync(
        WhatsAppConversation conversation,
        Customer? customer,
        string body,
        string messageType,
        CancellationToken cancellationToken) =>
        SendAsync(conversation, customer, BuildTextPayload(conversation.WhatsAppNumber, body),
            messageType, "Text", cancellationToken);

    // WhatsApp Cloud API limits for interactive messages.
    private const int MaxButtons = 3;
    private const int MaxButtonTitleLength = 20;
    public const int MaxListRows = 10;
    private const int MaxListRowTitleLength = 24;
    private const int MaxListRowDescriptionLength = 72;

    public Task SendButtonsAsync(
        WhatsAppConversation conversation,
        Customer? customer,
        string body,
        IReadOnlyList<InteractiveOption> buttons,
        CancellationToken cancellationToken) =>
        SendAsync(conversation, customer, new
        {
            messaging_product = "whatsapp",
            to = conversation.WhatsAppNumber,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = body },
                action = new
                {
                    buttons = buttons.Take(MaxButtons).Select(button => new
                    {
                        type = "reply",
                        reply = new { id = button.Id, title = Truncate(button.Title, MaxButtonTitleLength) }
                    }).ToArray()
                }
            }
        }, "Menu", "InteractiveButton", cancellationToken);

    public Task SendListAsync(
        WhatsAppConversation conversation,
        Customer? customer,
        string body,
        string buttonText,
        string sectionTitle,
        IReadOnlyList<InteractiveOption> rows,
        CancellationToken cancellationToken) =>
        SendAsync(conversation, customer, new
        {
            messaging_product = "whatsapp",
            to = conversation.WhatsAppNumber,
            type = "interactive",
            interactive = new
            {
                type = "list",
                body = new { text = body },
                action = new
                {
                    button = Truncate(buttonText, MaxButtonTitleLength),
                    sections = new object[]
                    {
                        new
                        {
                            title = Truncate(sectionTitle, MaxListRowTitleLength),
                            rows = rows.Take(MaxListRows).Select(row => new
                            {
                                id = row.Id,
                                title = Truncate(row.Title, MaxListRowTitleLength),
                                description = row.Description is null
                                    ? null
                                    : Truncate(row.Description, MaxListRowDescriptionLength)
                            }).ToArray()
                        }
                    }
                }
            }
        }, "Menu", "InteractiveList", cancellationToken);

    private async Task SendAsync(
        WhatsAppConversation conversation,
        Customer? customer,
        object payload,
        string messageLogType,
        string conversationMessageType,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var messageLog = new WhatsAppMessageLog
        {
            CustomerId = customer?.Id,
            WhatsAppNumber = conversation.WhatsAppNumber,
            MessageType = messageLogType,
            Direction = "Outgoing",
            Status = "Queued",
            Payload = payloadJson,
            CreatedAt = DateTime.UtcNow
        };

        var conversationMessage = new WhatsAppConversationMessage
        {
            ConversationId = conversation.Id,
            CustomerId = customer?.Id,
            WhatsAppNumber = conversation.WhatsAppNumber,
            Direction = "Outgoing",
            MessageType = conversationMessageType,
            PayloadJson = payloadJson,
            CreatedAt = DateTime.UtcNow
        };

        db.WhatsAppMessageLogs.Add(messageLog);
        db.WhatsAppConversationMessages.Add(conversationMessage);

        // The access token is maintained by the main Orchid Prism project in WhatsAppApiSettings.
        var apiSetting = await db.WhatsAppApiSettings
            .AsNoTracking()
            .Where(setting => setting.IsActive)
            .OrderByDescending(setting => setting.Id)
            .Select(setting => new
            {
                setting.ApiVersion,
                setting.PhoneNumberId,
                setting.AccessToken
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (apiSetting is null ||
            string.IsNullOrWhiteSpace(apiSetting.ApiVersion) ||
            string.IsNullOrWhiteSpace(apiSetting.PhoneNumberId))
        {
            messageLog.Status = "Failed";
            messageLog.ErrorMessage = "Active WhatsApp API settings were not found.";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(apiSetting.AccessToken))
        {
            messageLog.Status = "Failed";
            messageLog.ErrorMessage = "WhatsAppApiSettings.AccessToken is not configured.";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var url = $"https://graph.facebook.com/{apiSetting.ApiVersion}/{apiSetting.PhoneNumberId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiSetting.AccessToken.Trim());

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            messageLog.ApiResponse = responseBody;
            messageLog.Status = response.IsSuccessStatusCode ? "Sent" : "Failed";
            messageLog.MetaMessageId = TryGetMetaMessageId(responseBody);
            messageLog.UpdatedAt = DateTime.UtcNow;
            messageLog.ErrorMessage = response.IsSuccessStatusCode ? null : responseBody;

            conversationMessage.MetaMessageId = messageLog.MetaMessageId;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not send WhatsApp message to {WhatsAppNumber}.", conversation.WhatsAppNumber);
            messageLog.Status = "Failed";
            messageLog.ErrorMessage = exception.Message;
            messageLog.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static object BuildTextPayload(string to, string body) => new
    {
        messaging_product = "whatsapp",
        to,
        type = "text",
        text = new { body }
    };

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string? TryGetMetaMessageId(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array &&
                messages.GetArrayLength() > 0)
            {
                return messages[0].TryGetProperty("id", out var id) ? id.GetString() : null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}

public sealed record InteractiveOption(string Id, string Title, string? Description = null);
