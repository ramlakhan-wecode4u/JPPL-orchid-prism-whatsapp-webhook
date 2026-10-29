using JPPL.OrchidPrism.WhatsAppWebhook.Data;
using JPPL.OrchidPrism.WhatsAppWebhook.Helpers;
using JPPL.OrchidPrism.WhatsAppWebhook.Models;
using Microsoft.EntityFrameworkCore;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Services;

public sealed class WhatsAppWebhookProcessor(
    AppDbContext db,
    WhatsAppPayloadParser parser,
    WhatsAppMenuService menuService)
{
    public async Task<WebhookMetadata> ProcessAsync(
        string rawPayload,
        CancellationToken cancellationToken)
    {
        var parsedPayload = parser.Parse(rawPayload);
        string? firstMetaMessageId = null;
        string? firstWhatsAppNumber = null;

        foreach (var statusEvent in parsedPayload.Statuses)
        {
            firstMetaMessageId ??= statusEvent.MetaMessageId;
            firstWhatsAppNumber ??= statusEvent.RecipientId;
            await ProcessStatusEventAsync(statusEvent, cancellationToken);
        }

        foreach (var message in parsedPayload.Messages)
        {
            firstMetaMessageId ??= message.MetaMessageId;
            firstWhatsAppNumber ??= message.From;
            await LogIncomingMessageAsync(message, cancellationToken);
            await menuService.ProcessIncomingMessageAsync(message, cancellationToken);
        }

        return new WebhookMetadata(parsedPayload.EventType, firstMetaMessageId, firstWhatsAppNumber);
    }

    private async Task ProcessStatusEventAsync(
        ParsedStatusEvent statusEvent,
        CancellationToken cancellationToken)
    {
        var status = MapStatus(statusEvent.Status);
        if (string.IsNullOrWhiteSpace(statusEvent.MetaMessageId) || status is null)
        {
            return;
        }

        var messageLogs = await db.WhatsAppMessageLogs
            .Where(log => log.MetaMessageId == statusEvent.MetaMessageId)
            .ToListAsync(cancellationToken);

        foreach (var messageLog in messageLogs)
        {
            // Meta can deliver status webhooks out of order; never move a message back,
            // for example from Read to Delivered.
            if (GetStatusRank(status) <= GetStatusRank(messageLog.Status))
            {
                continue;
            }

            messageLog.Status = status;
            messageLog.UpdatedAt = DateTime.UtcNow;

            if (status == "Failed")
            {
                messageLog.ErrorCode = statusEvent.ErrorCode;
                messageLog.ErrorMessage = statusEvent.ErrorMessage;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static int GetStatusRank(string? status) => status switch
    {
        "Sent" => 1,
        "Delivered" => 2,
        "Read" => 3,
        "Failed" => 4,
        _ => 0
    };

    private async Task LogIncomingMessageAsync(
        ParsedIncomingMessage message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.MetaMessageId) ||
            string.IsNullOrWhiteSpace(message.From))
        {
            return;
        }

        var alreadyLogged = await db.WhatsAppMessageLogs
            .AnyAsync(log => log.MetaMessageId == message.MetaMessageId &&
                             log.Direction == "Incoming", cancellationToken);

        if (alreadyLogged)
        {
            return;
        }

        db.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
        {
            WhatsAppNumber = message.From,
            MessageType = "IncomingMessage",
            MetaMessageId = message.MetaMessageId,
            Direction = "Incoming",
            Status = "Received",
            Payload = message.PayloadJson,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static string? MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "sent" => "Sent",
        "delivered" => "Delivered",
        "read" => "Read",
        "failed" => "Failed",
        _ => null
    };
}

public sealed record WebhookMetadata(
    string EventType,
    string? MetaMessageId,
    string? WhatsAppNumber);
