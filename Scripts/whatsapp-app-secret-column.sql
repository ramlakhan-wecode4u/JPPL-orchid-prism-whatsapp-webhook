-- Adds the Meta App Secret used by the webhook to validate the X-Hub-Signature-256 header.
-- Run this once in SSMS against the Orchid Prism database before deploying this webhook version.
-- Find the secret in the Meta App Dashboard: App settings > Basic > App secret.

IF COL_LENGTH('dbo.WhatsAppApiSettings', 'AppSecret') IS NULL
BEGIN
    ALTER TABLE dbo.WhatsAppApiSettings
    ADD AppSecret NVARCHAR(255) NULL;
END;
GO

-- Replace the placeholder with the real App Secret, then run this statement.
-- UPDATE dbo.WhatsAppApiSettings
-- SET AppSecret = N'<META_APP_SECRET>',
--     UpdatedAt = SYSUTCDATETIME()
-- WHERE IsActive = 1;
