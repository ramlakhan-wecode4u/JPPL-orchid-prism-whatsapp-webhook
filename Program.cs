using JPPL.OrchidPrism.WhatsAppWebhook.Data;
using JPPL.OrchidPrism.WhatsAppWebhook.Helpers;
using JPPL.OrchidPrism.WhatsAppWebhook.Models;
using JPPL.OrchidPrism.WhatsAppWebhook.Options;
using JPPL.OrchidPrism.WhatsAppWebhook.Services;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

const long maxRequestBodySize = 1_048_576; // 1 MiB

var builder = WebApplication.CreateBuilder(args);

// Console logging works consistently for Windows services, Linux hosts, and containers.
// In particular, it does not require permission to write to the Windows Event Log.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is required in appsettings.json.");
}

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maxRequestBodySize);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.EnableRetryOnFailure(3)));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<WhatsAppPayloadParser>();
builder.Services.Configure<SupportContactOptions>(
    builder.Configuration.GetSection(SupportContactOptions.SectionName));
builder.Services.AddScoped<WhatsAppSignatureValidator>();
builder.Services.AddScoped<WhatsAppWebhookProcessor>();
builder.Services.AddScoped<WhatsAppAccountResolver>();
builder.Services.AddScoped<CustomerAccountDataService>();
builder.Services.AddScoped<WhatsAppMenuService>();
builder.Services.AddScoped<WhatsAppMessageSender>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseInitializer.EnsureWhatsAppMenuTablesAsync(db, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.MapGet("/", (IWebHostEnvironment environment) => Results.Content($$"""
    <!doctype html>
    <html lang="en">
    <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>JPPL Orchid Prism WhatsApp Webhook</title>
        <style>
            body {
                margin: 0;
                font-family: Segoe UI, Arial, sans-serif;
                background: #f6f7f9;
                color: #1f2933;
            }

            main {
                max-width: 760px;
                margin: 56px auto;
                padding: 32px;
                background: #ffffff;
                border: 1px solid #d9dee7;
                border-radius: 8px;
                box-shadow: 0 12px 30px rgba(15, 23, 42, 0.08);
            }

            h1 {
                margin: 0 0 12px;
                font-size: 28px;
                line-height: 1.25;
            }

            p {
                font-size: 16px;
                line-height: 1.6;
            }

            code {
                padding: 2px 6px;
                background: #eef2f7;
                border-radius: 4px;
            }

            .links {
                display: flex;
                flex-wrap: wrap;
                gap: 12px;
                margin-top: 24px;
            }

            a {
                color: #0f766e;
                font-weight: 600;
                text-decoration: none;
            }

            .button {
                display: inline-flex;
                align-items: center;
                min-height: 40px;
                padding: 0 14px;
                border: 1px solid #0f766e;
                border-radius: 6px;
                background: #ecfdf5;
            }
        </style>
    </head>
    <body>
        <main>
            <h1>JPPL Orchid Prism WhatsApp Webhook</h1>
            <p>This service is running. Use <code>/api/whatsapp/webhook</code> as the Meta WhatsApp callback URL and <code>/health</code> for uptime checks.</p>
            <p>Environment: <code>{{environment.EnvironmentName}}</code></p>
            <div class="links">
                <a class="button" href="/health">Health check</a>
                <a class="button" href="/api/whatsapp/webhook">Webhook verify endpoint</a>
                {{(environment.IsDevelopment() ? "<a class=\"button\" href=\"/swagger\">Swagger</a>" : "")}}
            </div>
        </main>
    </body>
    </html>
    """, "text/html"))
.WithName("Home");

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "JPPL-orchid-prism-whatsapp-webhook"
}))
.WithName("HealthCheck");

app.MapGet("/api/whatsapp/debug/database", async Task<IResult> (
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var connection = db.Database.GetDbConnection();
    var recentWebhookEvents = await db.WhatsAppWebhookEvents
        .AsNoTracking()
        .OrderByDescending(item => item.Id)
        .Take(10)
        .Select(item => new
        {
            item.Id,
            item.EventType,
            item.MetaMessageId,
            item.WhatsAppNumber,
            item.Processed,
            item.ProcessingError,
            item.CreatedAt,
            item.ProcessedAt
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(new
    {
        database = connection.Database,
        server = connection.DataSource,
        webhookEventCount = await db.WhatsAppWebhookEvents.CountAsync(cancellationToken),
        latestWebhookEvents = recentWebhookEvents
    });
})
.WithName("WhatsAppDebugDatabase")
.AddEndpointFilter(DevelopmentOnly);

app.MapGet("/api/whatsapp/webhook", async Task<IResult> (
    HttpRequest request,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var mode = request.Query["hub.mode"].ToString();
    var token = request.Query["hub.verify_token"].ToString();
    var challenge = request.Query["hub.challenge"].ToString();

    // A browser hit has no Meta challenge values, so show a friendly page.
    if (string.IsNullOrWhiteSpace(mode) &&
        string.IsNullOrWhiteSpace(token) &&
        string.IsNullOrWhiteSpace(challenge))
    {
        return Results.Content("""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>WhatsApp Webhook Endpoint</title>
                <style>
                    body {
                        margin: 0;
                        font-family: Segoe UI, Arial, sans-serif;
                        background: #f6f7f9;
                        color: #1f2933;
                    }

                    main {
                        max-width: 760px;
                        margin: 56px auto;
                        padding: 32px;
                        background: #ffffff;
                        border: 1px solid #d9dee7;
                        border-radius: 8px;
                        box-shadow: 0 12px 30px rgba(15, 23, 42, 0.08);
                    }

                    h1 {
                        margin: 0 0 12px;
                        font-size: 28px;
                        line-height: 1.25;
                    }

                    p {
                        font-size: 16px;
                        line-height: 1.6;
                    }

                    code {
                        padding: 2px 6px;
                        background: #eef2f7;
                        border-radius: 4px;
                    }

                    a {
                        color: #0f766e;
                        font-weight: 600;
                        text-decoration: none;
                    }
                </style>
            </head>
            <body>
                <main>
                    <h1>WhatsApp Webhook Endpoint</h1>
                    <p>This endpoint is ready. Meta will call it with <code>hub.mode</code>, <code>hub.verify_token</code>, and <code>hub.challenge</code> during webhook verification.</p>
                    <p>For a normal browser check, use <a href="/health">/health</a> or <a href="/">the service home page</a>.</p>
                </main>
            </body>
            </html>
            """, "text/html");
    }

    // Meta verifies the webhook by sending back a challenge only when the configured token matches.
    var verifyToken = await db.WhatsAppApiSettings
        .AsNoTracking()
        .Where(setting => setting.IsActive)
        .OrderByDescending(setting => setting.Id)
        .Select(setting => setting.VerifyToken)
        .FirstOrDefaultAsync(cancellationToken);

    if (mode == "subscribe" &&
        !string.IsNullOrEmpty(verifyToken) &&
        string.Equals(token, verifyToken, StringComparison.Ordinal))
    {
        return Results.Text(challenge, "text/plain");
    }

    return Results.Unauthorized();
})
.WithName("VerifyWhatsAppWebhook");

app.MapPost("/api/whatsapp/webhook", async Task<IResult> (
    HttpRequest request,
    AppDbContext db,
    WhatsAppSignatureValidator signatureValidator,
    WhatsAppWebhookProcessor processor,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (request.ContentLength is 0)
    {
        return Results.BadRequest(new { error = "Request body is required." });
    }

    if (request.ContentLength > maxRequestBodySize)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    // The signature covers the exact bytes Meta sent, so read the body as bytes before decoding it.
    byte[] body;
    using (var buffer = new MemoryStream())
    {
        await request.Body.CopyToAsync(buffer, cancellationToken);
        body = buffer.ToArray();
    }

    if (!await signatureValidator.IsValidAsync(
            body,
            request.Headers[WhatsAppSignatureValidator.SignatureHeaderName].ToString(),
            cancellationToken))
    {
        logger.LogWarning("Rejected WhatsApp webhook request with a missing or invalid signature.");
        return Results.Unauthorized();
    }

    var rawPayload = Encoding.UTF8.GetString(body);
    if (string.IsNullOrWhiteSpace(rawPayload))
    {
        return Results.BadRequest(new { error = "Request body is required." });
    }

    var webhookEvent = new WhatsAppWebhookEvent
    {
        EventType = "unknown",
        RawPayload = rawPayload,
        Processed = false,
        CreatedAt = DateTime.UtcNow
    };

    // Save RawPayload first so failed parsing/menu logic still leaves an audit trail for debugging.
    db.WhatsAppWebhookEvents.Add(webhookEvent);
    await db.SaveChangesAsync(cancellationToken);

    try
    {
        var metadata = await processor.ProcessAsync(rawPayload, cancellationToken);
        webhookEvent.EventType = metadata.EventType;
        webhookEvent.MetaMessageId = metadata.MetaMessageId;
        webhookEvent.WhatsAppNumber = metadata.WhatsAppNumber;
        webhookEvent.Processed = true;
        webhookEvent.ProcessingError = null;
        webhookEvent.ProcessedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogError(exception,
            "WhatsApp webhook event {WebhookEventId} was stored but could not be processed.",
            webhookEvent.Id);

        // Discard any failed message-log changes, then record the processing failure alone.
        db.ChangeTracker.Clear();
        try
        {
            var storedEvent = await db.WhatsAppWebhookEvents
                .SingleAsync(item => item.Id == webhookEvent.Id, cancellationToken);
            storedEvent.Processed = false;
            storedEvent.ProcessingError = Truncate(exception.Message, 4000);
            storedEvent.ProcessedAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception updateException)
        {
            logger.LogError(updateException,
                "Could not record the processing error for WhatsApp webhook event {WebhookEventId}.",
                webhookEvent.Id);
        }
    }

    // Meta should receive 200 after the raw payload is stored, even if internal processing failed.
    return Results.Ok();
})
.WithName("ReceiveWhatsAppWebhook");

app.MapGet("/api/whatsapp/message-status/recent", async Task<IResult> (
    AppDbContext db,
    WhatsAppPayloadParser parser,
    string? messageType,
    int? take,
    CancellationToken cancellationToken) =>
{
    var max = Math.Clamp(take ?? 10, 1, 50);
    var query = db.WhatsAppMessageLogs.AsNoTracking()
        .Where(log => log.Direction == "Outgoing");

    if (!string.IsNullOrWhiteSpace(messageType))
    {
        query = query.Where(log => log.MessageType == messageType);
    }

    var logs = await query
        .OrderByDescending(log => log.Id)
        .Take(max)
        .Select(log => new
        {
            log.Id,
            log.CustomerId,
            log.WhatsAppNumber,
            log.MessageType,
            log.TemplateName,
            log.MetaMessageId,
            log.Status,
            log.ErrorCode,
            log.ErrorMessage,
            log.Payload,
            log.ApiResponse,
            log.CreatedAt,
            log.UpdatedAt
        })
        .ToListAsync(cancellationToken);

    var metaMessageIds = logs
        .Select(log => log.MetaMessageId)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct()
        .ToArray();

    var webhookRows = await db.WhatsAppWebhookEvents.AsNoTracking()
        .Where(item => item.MetaMessageId != null && metaMessageIds.Contains(item.MetaMessageId))
        .OrderByDescending(item => item.Id)
        .ToListAsync(cancellationToken);
    var webhookEvents = webhookRows.Select(item => new
    {
        item.Id,
        item.EventType,
        item.MetaMessageId,
        item.WhatsAppNumber,
        item.Processed,
        item.ProcessingError,
        item.CreatedAt,
        item.ProcessedAt,
        Statuses = ExtractMatchingStatuses(parser, item.RawPayload, item.MetaMessageId)
    });

    return Results.Ok(new { logs, webhookEvents });
})
.WithName("RecentWhatsAppMessageStatus")
.AddEndpointFilter(DevelopmentOnly);

app.MapGet("/api/whatsapp/message-status/{metaMessageId}", async Task<IResult> (
    string metaMessageId,
    AppDbContext db,
    WhatsAppPayloadParser parser,
    CancellationToken cancellationToken) =>
{
    var logs = await db.WhatsAppMessageLogs.AsNoTracking()
        .Where(log => log.MetaMessageId == metaMessageId)
        .OrderByDescending(log => log.Id)
        .Select(log => new
        {
            log.Id,
            log.CustomerId,
            log.WhatsAppNumber,
            log.MessageType,
            log.TemplateName,
            log.MetaMessageId,
            log.Status,
            log.ErrorCode,
            log.ErrorMessage,
            log.Payload,
            log.ApiResponse,
            log.CreatedAt,
            log.UpdatedAt
        })
        .ToListAsync(cancellationToken);

    var webhookRows = await db.WhatsAppWebhookEvents.AsNoTracking()
        .Where(item => item.MetaMessageId == metaMessageId ||
                       item.RawPayload.Contains(metaMessageId))
        .OrderByDescending(item => item.Id)
        .ToListAsync(cancellationToken);
    var webhookEvents = webhookRows.Select(item => new
    {
        item.Id,
        item.EventType,
        item.MetaMessageId,
        item.WhatsAppNumber,
        item.Processed,
        item.ProcessingError,
        item.CreatedAt,
        item.ProcessedAt,
        Statuses = ExtractMatchingStatuses(parser, item.RawPayload, metaMessageId)
    });

    return Results.Ok(new { metaMessageId, logs, webhookEvents });
})
.WithName("WhatsAppMessageStatusByMetaId")
.AddEndpointFilter(DevelopmentOnly);

app.Run();

// Diagnostic endpoints expose phone numbers, payloads and database details, so they answer only in Development.
static async ValueTask<object?> DevelopmentOnly(
    EndpointFilterInvocationContext context,
    EndpointFilterDelegate next) =>
    context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
        ? await next(context)
        : Results.NotFound();

static string Truncate(string value, int maximumLength) =>
    value.Length <= maximumLength ? value : value[..maximumLength];

static IReadOnlyList<object> ExtractMatchingStatuses(
    WhatsAppPayloadParser parser,
    string rawPayload,
    string? metaMessageId)
{
    if (string.IsNullOrWhiteSpace(metaMessageId))
    {
        return [];
    }

    try
    {
        return parser.Parse(rawPayload).Statuses
            .Where(status => status.MetaMessageId == metaMessageId)
            .Select(status => new
            {
                status.MetaMessageId,
                status.Status,
                status.RecipientId,
                status.Timestamp,
                status.ErrorCode,
                status.ErrorMessage,
                status.PayloadJson
            })
            .Cast<object>()
            .ToList();
    }
    catch (JsonException)
    {
        return [];
    }
}

public partial class Program;
