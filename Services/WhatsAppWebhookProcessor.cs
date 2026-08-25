using System.Text.Json;
using JPPL.OrchidPrism.WhatsAppWebhook.Data;
using JPPL.OrchidPrism.WhatsAppWebhook.Models;
using Microsoft.EntityFrameworkCore;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Services;

public sealed class WhatsAppWebhookProcessor(AppDbContext db)
{
    public async Task<WebhookMetadata> ProcessAsync(
        string rawPayload,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(rawPayload);
        var root = document.RootElement;

        var eventType = "unknown";
        string? firstMetaMessageId = null;
        string? firstWhatsAppNumber = null;

        foreach (var value in EnumerateChangeValues(root))
        {
            if (value.TryGetProperty("statuses", out var statuses) &&
                statuses.ValueKind == JsonValueKind.Array)
            {
                eventType = "statuses";

                foreach (var statusEvent in statuses.EnumerateArray())
                {
                    var metaMessageId = GetString(statusEvent, "id");
                    var whatsAppNumber = GetString(statusEvent, "recipient_id");
                    var status = MapStatus(GetString(statusEvent, "status"));

                    firstMetaMessageId ??= metaMessageId;
                    firstWhatsAppNumber ??= whatsAppNumber;

                    if (string.IsNullOrWhiteSpace(metaMessageId) || status is null)
                    {
                        continue;
                    }

                    var messageLogs = await db.WhatsAppMessageLogs
                        .Where(log => log.MetaMessageId == metaMessageId)
                        .ToListAsync(cancellationToken);

                    var (errorCode, errorMessage) = ReadError(statusEvent);
                    foreach (var messageLog in messageLogs)
                    {
                        messageLog.Status = status;
                        messageLog.UpdatedAt = DateTime.UtcNow;

                        if (status == "Failed")
                        {
                            messageLog.ErrorCode = errorCode;
                            messageLog.ErrorMessage = errorMessage;
                        }
                    }
                }
            }

            if (value.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array)
            {
                if (eventType == "unknown")
                {
                    eventType = "messages";
                }

                foreach (var message in messages.EnumerateArray())
                {
                    var metaMessageId = GetString(message, "id");
                    var whatsAppNumber = GetString(message, "from");
                    firstMetaMessageId ??= metaMessageId;
                    firstWhatsAppNumber ??= whatsAppNumber;

                    if (string.IsNullOrWhiteSpace(metaMessageId))
                    {
                        continue;
                    }

                    var alreadyLogged = await db.WhatsAppMessageLogs
                        .AnyAsync(log => log.MetaMessageId == metaMessageId &&
                                         log.Direction == "Incoming", cancellationToken);

                    if (!alreadyLogged)
                    {
                        db.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
                        {
                            WhatsAppNumber = whatsAppNumber,
                            MessageType = "IncomingMessage",
                            MetaMessageId = metaMessageId,
                            Direction = "Incoming",
                            Status = "Received",
                            Payload = rawPayload,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            if (eventType == "unknown" && HasNonEmptyArray(value, "errors"))
            {
                eventType = "errors";
            }
        }

        if (eventType == "unknown" && HasNonEmptyArray(root, "errors"))
        {
            eventType = "errors";
        }

        return new WebhookMetadata(eventType, firstMetaMessageId, firstWhatsAppNumber);
    }

    private static IEnumerable<JsonElement> EnumerateChangeValues(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) ||
                changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("value", out var value) &&
                    value.ValueKind == JsonValueKind.Object)
                {
                    yield return value;
                }
            }
        }
    }

    private static string? MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "sent" => "Sent",
        "delivered" => "Delivered",
        "read" => "Read",
        "failed" => "Failed",
        _ => null
    };

    private static (string? Code, string? Message) ReadError(JsonElement statusEvent)
    {
        if (!statusEvent.TryGetProperty("errors", out var errors) ||
            errors.ValueKind != JsonValueKind.Array ||
            errors.GetArrayLength() == 0)
        {
            return (null, null);
        }

        var error = errors[0];
        var code = GetValueAsString(error, "code");
        var message = GetString(error, "message") ?? GetString(error, "title");

        if (error.TryGetProperty("error_data", out var errorData) &&
            errorData.ValueKind == JsonValueKind.Object)
        {
            message ??= GetString(errorData, "details");
        }

        return (code, message);
    }

    private static bool HasNonEmptyArray(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Array &&
        property.GetArrayLength() > 0;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? GetValueAsString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }
}

public sealed record WebhookMetadata(
    string EventType,
    string? MetaMessageId,
    string? WhatsAppNumber);
