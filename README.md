# JPPL Orchid Prism WhatsApp Webhook

A small, independently deployable ASP.NET Core service that receives Meta WhatsApp Cloud API webhooks. It verifies Meta's subscription challenge, stores every webhook payload, updates outgoing-message delivery statuses, and records incoming customer messages. It contains no OTP, sending, chatbot, CRM, order, or customer-verification business logic.

The project targets .NET 10, the latest stable SDK installed in the project environment. It uses ASP.NET Core Minimal API, Entity Framework Core, SQL Server, and built-in structured logging.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/` | Default service status page |
| `GET` | `/health` | Service health check |
| `GET` | `/api/whatsapp/webhook` | Meta subscription verification |
| `POST` | `/api/whatsapp/webhook` | Receive and process Meta events |

Swagger is available only in Development at `/swagger`.

## Database requirements

The service uses the existing SQL Server database and does not create or migrate tables. These tables must already exist:

- `WhatsAppWebhookEvents`: `Id`, `EventType`, `MetaMessageId`, `WhatsAppNumber`, `RawPayload`, `Processed`, `ProcessingError`, `CreatedAt`, `ProcessedAt`
- `WhatsAppMessageLogs`: `Id`, `CustomerId`, `WhatsAppNumber`, `MessageType`, `TemplateName`, `MetaMessageId`, `Direction`, `Status`, `ErrorCode`, `ErrorMessage`, `Payload`, `ApiResponse`, `CreatedAt`, `UpdatedAt`
- `WhatsAppApiSettings`: `Id`, `BusinessName`, `PhoneNumberId`, `WhatsAppBusinessAccountId`, `BusinessPhoneNumber`, `ApiVersion`, `VerifyToken`, `OtpTemplateName`, `OtpTemplateLanguageCode`, `OtpExpiryMinutes`, `MaxOtpAttempts`, `ResendCooldownSeconds`, `IsActive`, `CreatedAt`, `UpdatedAt`

An active `WhatsAppApiSettings` row must contain the Meta verify token. The expected development token is `orchid_whatsapp_verify_2026`; it is documented here for Meta setup but is not hardcoded into the application.

The database login needs only `SELECT` on `WhatsAppApiSettings`, `INSERT` and `UPDATE` on `WhatsAppWebhookEvents`, and `SELECT`, `INSERT`, and `UPDATE` on `WhatsAppMessageLogs`.

## Configuration

Set the connection string through an environment variable in production:

```text
ConnectionStrings__DefaultConnection=Server=...;Database=OrchidPrismDb;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=False;
```

Do not commit real database credentials or WhatsApp access tokens. An access token is not required to receive webhooks.

For local development, either set the same environment variable or replace the placeholder in `appsettings.json` in an uncommitted local configuration.

## Run locally

Requirements: .NET 10 SDK and network access to the existing SQL Server.

```powershell
cd JPPL-orchid-prism-whatsapp-webhook
$env:ConnectionStrings__DefaultConnection = "Server=YOUR_SERVER;Database=OrchidPrismDb;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
dotnet restore
dotnet run
```

Use the URL printed by ASP.NET Core. Localhost is useful for testing but cannot be registered directly as Meta's callback URL; use a trusted HTTPS development tunnel if Meta must call your local instance.

## Configure Meta WhatsApp

Deploy the service on a public HTTPS domain, then configure the WhatsApp webhook in the Meta App Dashboard:

```text
Callback URL: https://your-public-domain.com/api/whatsapp/webhook
Verify token: orchid_whatsapp_verify_2026
```

Subscribe to the `messages` webhook field. Meta uses the GET endpoint to verify the callback and sends events to the POST endpoint.

## Processing behavior

The POST endpoint accepts payloads up to 1 MiB. It first inserts the untouched request body into `WhatsAppWebhookEvents`, then parses it. Status values are mapped as follows:

| Meta status | Stored status |
|---|---|
| `sent` | `Sent` |
| `delivered` | `Delivered` |
| `read` | `Read` |
| `failed` | `Failed` |

Failed statuses also copy Meta's error code and message when provided. Incoming messages are idempotently logged as `IncomingMessage` / `Incoming` / `Received`, keyed by Meta message ID and direction. All raw deliveries, including retries, remain in `WhatsAppWebhookEvents`.

If parsing or internal processing fails, the raw event remains stored with `Processed = 0` and `ProcessingError` populated. After the initial raw insert succeeds, the endpoint returns HTTP 200 even when later processing fails, preventing unnecessary Meta retries. Empty bodies receive HTTP 400, oversized requests receive HTTP 413, and a database failure during the initial insert is surfaced as a server error because the payload was not stored.

## Production deployment

- Host on a public HTTPS URL such as `https://whatsapp.yourdomain.com`; do not configure Meta with localhost, private IP addresses, or internal-only DNS.
- Terminate TLS at the hosting platform or a trusted reverse proxy. Keep HTTP-to-HTTPS redirection enabled.
- When TLS terminates at a reverse proxy, configure that host as a trusted forwarded-header proxy (or use the platform's supported forwarded-header setting) so ASP.NET Core receives the original HTTPS scheme.
- Inject `ConnectionStrings__DefaultConnection` from the platform's secret store.
- Ensure the public service can securely reach SQL Server, for example through an allow-listed encrypted connection, VPN, or private network integration.
- Restrict database permissions to the required tables and monitor application logs without logging credentials or tokens.
- Run multiple instances only after confirming the database can accept concurrent webhook writes.

Build a container from the included `Dockerfile`, or publish directly with:

```powershell
dotnet publish -c Release -o publish
```
