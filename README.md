# JPPL Orchid Prism WhatsApp Webhook

A small, independently deployable ASP.NET Core service that receives Meta WhatsApp Cloud API webhooks. It verifies Meta's subscription challenge, stores every webhook payload, updates outgoing-message delivery statuses, records incoming customer messages, and sends simple menu replies for verified customers. It contains no OTP verification logic; OTP verification remains in the main Orchid Prism project.

The project targets .NET 10, the latest stable SDK installed in the project environment. It uses ASP.NET Core Minimal API, Entity Framework Core, SQL Server, and built-in structured logging.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/` | Default service status page |
| `GET` | `/health` | Service health check |
| `GET` | `/api/whatsapp/webhook` | Meta subscription verification |
| `POST` | `/api/whatsapp/webhook` | Receive and process Meta events (requires a valid `X-Hub-Signature-256`) |
| `GET` | `/api/whatsapp/debug/database` | Development only: recent webhook events |
| `GET` | `/api/whatsapp/message-status/recent` | Development only: recent outgoing message statuses |
| `GET` | `/api/whatsapp/message-status/{metaMessageId}` | Development only: status history for one message |

Swagger is available only in Development at `/swagger`. The diagnostic endpoints return 404 in every other environment because they expose phone numbers and payloads.

## Database requirements

The service uses the existing SQL Server database. On startup it checks the required WhatsApp/customer tables, creates the three menu tables only when they are missing, and seeds `WhatsAppMenuActions` only when that table is empty.

- `WhatsAppWebhookEvents`: `Id`, `EventType`, `MetaMessageId`, `WhatsAppNumber`, `RawPayload`, `Processed`, `ProcessingError`, `CreatedAt`, `ProcessedAt`
- `WhatsAppMessageLogs`: `Id`, `CustomerId`, `WhatsAppNumber`, `MessageType`, `TemplateName`, `MetaMessageId`, `Direction`, `Status`, `ErrorCode`, `ErrorMessage`, `Payload`, `ApiResponse`, `CreatedAt`, `UpdatedAt`
- `WhatsAppApiSettings`: this service reads `PhoneNumberId`, `ApiVersion`, `VerifyToken`, `AccessToken`, `AppSecret`, and `IsActive`. `AppSecret` is not created by the main project; add it with `Scripts/whatsapp-app-secret-column.sql`.
- `Customers`: must contain `WhatsAppNumber`, `IsWhatsAppVerified`, `WhatsAppVerifiedAt`, and `IsActive`
- `CustomerContacts`: must contain `CustomerId`, `WhatsAppNumber`, `IsWhatsAppVerified`, and `CanSeeRates`
- Read-only order and billing tables: `Orders`, `EstimateVersions`, `LuStatus`, `CustomerBills`, `CustomerBalances`, `CustomerPaymentTerms`
- `WhatsAppConversations`: current menu/chat state per WhatsApp number
- `WhatsAppConversationMessages`: incoming and outgoing menu/chat message history
- `WhatsAppMenuActions`: seed menu options for future database configurability

An active `WhatsAppApiSettings` row must contain the Meta verify token. The expected development token is `orchid_whatsapp_verify_2026`; it is documented here for Meta setup but is not hardcoded into the application.

The database login needs `SELECT` on `Customers`, `CustomerContacts`, `WhatsAppApiSettings`, and the read-only order and billing tables above, `INSERT` and `UPDATE` on `WhatsAppWebhookEvents`, and `SELECT`, `INSERT`, and `UPDATE` on `WhatsAppMessageLogs`, `WhatsAppConversations`, and `WhatsAppConversationMessages`. The startup initializer also needs permission to create the three menu tables if they do not already exist.

If you prefer to create the menu tables manually, run `Scripts/whatsapp-menu-tables.sql` in SSMS against `OrchidPrismDb`.

The service never alters `WhatsAppApiSettings`. On startup it checks that the `AppSecret` column exists and stops with a clear error if it does not. Run `Scripts/whatsapp-app-secret-column.sql` once, then set the App Secret value (Meta App Dashboard > App settings > Basic > App secret) with the `UPDATE` statement in that script.

## Configuration

`appsettings.json` holds only the connection string and the support contact shown by the Support option:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\SQLEXPRESS;Database=OrchidPrismDb;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;"
  },
  "Support": {
    "Phone": "+91 ...",
    "Email": "support@..."
  }
}
```

WhatsApp credentials are not stored in configuration. The service reads them from the active `WhatsAppApiSettings` row, which the main Orchid Prism project maintains:

| Column | Used for |
|---|---|
| `AccessToken` | Bearer token for sending replies through the Cloud API |
| `PhoneNumberId`, `ApiVersion` | Cloud API send URL |
| `VerifyToken` | Meta webhook subscription check |
| `AppSecret` | Validating `X-Hub-Signature-256` on every POST |

If `AppSecret` is empty, signed requests cannot be checked: the webhook rejects all POSTs with 401, except in Development where the check is skipped with a warning.

## Run locally

Requirements: .NET 10 SDK and network access to the existing SQL Server.

```powershell
cd JPPL-orchid-prism-whatsapp-webhook
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

The POST endpoint accepts payloads up to 1 MiB. It first checks the `X-Hub-Signature-256` header against `WhatsAppApiSettings.AppSecret` and returns 401 for a missing or invalid signature. It then inserts the untouched request body into `WhatsAppWebhookEvents` and parses it. Status values are mapped as follows:

| Meta status | Stored status |
|---|---|
| `sent` | `Sent` |
| `delivered` | `Delivered` |
| `read` | `Read` |
| `failed` | `Failed` |

Meta can deliver statuses out of order, so a status only moves forward (`Sent` < `Delivered` < `Read` < `Failed`); a late `delivered` never overwrites `read`. Failed statuses also copy Meta's error code and message when provided. Incoming messages are idempotently logged as `IncomingMessage` / `Incoming` / `Received`, keyed by Meta message ID and direction. All raw deliveries, including retries, remain in `WhatsAppWebhookEvents`.

### Chatbot menu

Incoming text, button replies, list replies, and template quick-reply buttons are parsed for menu handling. Reactions and system messages are stored but not answered.

| Customer sends | Result |
|---|---|
| `Hi`, `Hello`, `Menu`, `Start`, `0` | Main menu with Orders, Balance, Credit buttons |
| `1`, `Orders` | List of the latest 10 orders (declined orders hidden); selecting one shows status, amount and bills |
| `2`, `Balance` | Advance balance (`CustomerBalances`), outstanding and overdue amounts (`CustomerBills`) |
| `3`, `Credit` | Credit limit and days (`CustomerPaymentTerms`), used credit, available credit, overdue amount |
| `4`, `Support`, `Help` | Support phone and email from the `Support` configuration section |
| `Switch`, `Account` | Account picker, for numbers linked to more than one account |

**Who can use the menu.** The sender's number is normalized (spaces, dashes, brackets and `+` removed; 10-digit numbers treated as Indian) and matched against:

1. `Customers.WhatsAppNumber` where `IsWhatsAppVerified = 1` and `IsActive = 1` (full access), and
2. `CustomerContacts.WhatsAppNumber` where the contact's `IsWhatsAppVerified = 1` and the customer is active. Contacts with `CanSeeRates = 0` see orders and statuses without amounts, and cannot open Balance or Credit.

Accounts are looked up on every message, so removing a verification takes effect immediately. When one number is linked to several accounts (for example, one owner with several studios), the bot asks the user to pick an account and remembers the choice on the conversation. Unknown numbers receive a message asking them to verify WhatsApp on the website first.

Order details are always filtered by the selected customer, so one customer cannot open another customer's order by ID.

If parsing or internal processing fails, the raw event remains stored with `Processed = 0` and `ProcessingError` populated. After the initial raw insert succeeds, the endpoint returns HTTP 200 even when later processing fails, preventing unnecessary Meta retries. Empty bodies receive HTTP 400, oversized requests receive HTTP 413, and a database failure during the initial insert is surfaced as a server error because the payload was not stored.

## Production deployment

- Host on a public HTTPS URL such as `https://whatsapp.yourdomain.com`; do not configure Meta with localhost, private IP addresses, or internal-only DNS.
- Terminate TLS at the hosting platform or a trusted reverse proxy. Keep HTTP-to-HTTPS redirection enabled.
- When TLS terminates at a reverse proxy, configure that host as a trusted forwarded-header proxy (or use the platform's supported forwarded-header setting) so ASP.NET Core receives the original HTTPS scheme.
- Set the production SQL Server connection string and support contact in `appsettings.json`. The access token and App Secret come from `WhatsAppApiSettings`.
- Ensure the public service can securely reach SQL Server, for example through an allow-listed encrypted connection, VPN, or private network integration.
- Restrict database permissions to the required tables and monitor application logs without logging credentials or tokens.
- Run multiple instances only after confirming the database can accept concurrent webhook writes.

Build a container from the included `Dockerfile`, or publish directly with:

```powershell
dotnet publish -c Release -o publish
```
