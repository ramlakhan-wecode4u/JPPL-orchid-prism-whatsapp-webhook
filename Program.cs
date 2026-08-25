using JPPL.OrchidPrism.WhatsAppWebhook.Data;
using JPPL.OrchidPrism.WhatsAppWebhook.Models;
using JPPL.OrchidPrism.WhatsAppWebhook.Services;
using Microsoft.EntityFrameworkCore;

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
        "ConnectionStrings:DefaultConnection is required. Set it with the " +
        "ConnectionStrings__DefaultConnection environment variable in production.");
}

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maxRequestBodySize);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.EnableRetryOnFailure(3)));
builder.Services.AddScoped<WhatsAppWebhookProcessor>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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

app.MapGet("/api/whatsapp/webhook", async Task<IResult> (
    HttpRequest request,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    var mode = request.Query["hub.mode"].ToString();
    var token = request.Query["hub.verify_token"].ToString();
    var challenge = request.Query["hub.challenge"].ToString();

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

    var verifyToken = await db.WhatsAppApiSettings
        .AsNoTracking()
        .Where(setting => setting.IsActive)
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

    string rawPayload;
    using (var reader = new StreamReader(request.Body))
    {
        rawPayload = await reader.ReadToEndAsync(cancellationToken);
    }

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

    // This save deliberately happens before JSON parsing or message-log updates.
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

    return Results.Ok();
})
.WithName("ReceiveWhatsAppWebhook");

app.Run();

static string Truncate(string value, int maximumLength) =>
    value.Length <= maximumLength ? value : value[..maximumLength];

public partial class Program;
