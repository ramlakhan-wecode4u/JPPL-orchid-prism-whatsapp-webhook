using System.Text.Json;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Helpers;

public sealed class WhatsAppPayloadParser
{
    public ParsedWebhookPayload Parse(string rawPayload)
    {
        using var document = JsonDocument.Parse(rawPayload);
        var root = document.RootElement;
        var result = new ParsedWebhookPayload();

        foreach (var value in EnumerateChangeValues(root))
        {
            if (value.TryGetProperty("statuses", out var statuses) &&
                statuses.ValueKind == JsonValueKind.Array)
            {
                foreach (var status in statuses.EnumerateArray())
                {
                    result.Statuses.Add(new ParsedStatusEvent(
                        GetString(status, "id"),
                        GetString(status, "status"),
                        GetString(status, "recipient_id"),
                        ReadErrorCode(status),
                        ReadErrorMessage(status)));
                }
            }

            if (value.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    result.Messages.Add(ParseMessage(message));
                }
            }
        }

        return result;
    }

    private static ParsedIncomingMessage ParseMessage(JsonElement message)
    {
        var type = GetString(message, "type") ?? "unknown";
        var messageType = "Unknown";
        string? userText = null;
        string? interactiveId = null;
        string? interactiveTitle = null;

        if (type == "text" &&
            message.TryGetProperty("text", out var text) &&
            text.ValueKind == JsonValueKind.Object)
        {
            messageType = "Text";
            userText = GetString(text, "body");
        }
        else if (type == "interactive" &&
                 message.TryGetProperty("interactive", out var interactive) &&
                 interactive.ValueKind == JsonValueKind.Object)
        {
            // Meta sends button and list replies under different nested objects.
            var interactiveType = GetString(interactive, "type");
            if (interactiveType == "button_reply" &&
                interactive.TryGetProperty("button_reply", out var buttonReply))
            {
                messageType = "InteractiveButton";
                interactiveId = GetString(buttonReply, "id");
                interactiveTitle = GetString(buttonReply, "title");
            }
            else if (interactiveType == "list_reply" &&
                     interactive.TryGetProperty("list_reply", out var listReply))
            {
                messageType = "InteractiveList";
                interactiveId = GetString(listReply, "id");
                interactiveTitle = GetString(listReply, "title");
            }
        }

        return new ParsedIncomingMessage(
            GetString(message, "from"),
            GetString(message, "id"),
            GetString(message, "timestamp"),
            messageType,
            userText,
            interactiveId,
            interactiveTitle,
            message.GetRawText());
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

    private static string? ReadErrorCode(JsonElement status)
    {
        if (!TryGetFirstError(status, out var error))
        {
            return null;
        }

        return GetValueAsString(error, "code");
    }

    private static string? ReadErrorMessage(JsonElement status)
    {
        if (!TryGetFirstError(status, out var error))
        {
            return null;
        }

        var message = GetString(error, "message") ?? GetString(error, "title");
        if (message is null &&
            error.TryGetProperty("error_data", out var errorData) &&
            errorData.ValueKind == JsonValueKind.Object)
        {
            message = GetString(errorData, "details");
        }

        return message;
    }

    private static bool TryGetFirstError(JsonElement status, out JsonElement error)
    {
        if (status.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            error = errors[0];
            return true;
        }

        error = default;
        return false;
    }

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

public sealed class ParsedWebhookPayload
{
    public List<ParsedIncomingMessage> Messages { get; } = [];
    public List<ParsedStatusEvent> Statuses { get; } = [];

    public string EventType =>
        Statuses.Count > 0 ? "statuses" :
        Messages.Count > 0 ? "messages" :
        "unknown";
}

public sealed record ParsedIncomingMessage(
    string? From,
    string? MetaMessageId,
    string? Timestamp,
    string MessageType,
    string? UserText,
    string? InteractiveId,
    string? InteractiveTitle,
    string PayloadJson);

public sealed record ParsedStatusEvent(
    string? MetaMessageId,
    string? Status,
    string? RecipientId,
    string? ErrorCode,
    string? ErrorMessage);
