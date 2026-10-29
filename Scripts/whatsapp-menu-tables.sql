SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME IN (
    'WhatsAppConversations',
    'WhatsAppConversationMessages',
    'WhatsAppMenuActions',
    'WhatsAppWebhookEvents',
    'WhatsAppMessageLogs',
    'WhatsAppApiSettings',
    'Customers',
    'CustomerContacts'
);

SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Customers'
AND COLUMN_NAME IN (
    'WhatsAppNumber',
    'IsWhatsAppVerified',
    'WhatsAppVerifiedAt'
);

SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'CustomerContacts'
AND COLUMN_NAME IN (
    'CustomerId',
    'WhatsAppNumber'
);

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'WhatsAppConversations'
)
BEGIN
    CREATE TABLE WhatsAppConversations (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        WhatsAppNumber NVARCHAR(20) NOT NULL,
        CustomerId BIGINT NULL,
        CurrentState NVARCHAR(100) NOT NULL
            CONSTRAINT DF_WhatsAppConversations_CurrentState DEFAULT 'MAIN_MENU',
        LastMessageId NVARCHAR(255) NULL,
        ContextJson NVARCHAR(MAX) NULL,
        LastInteractionAt DATETIME2 NOT NULL
            CONSTRAINT DF_WhatsAppConversations_LastInteractionAt DEFAULT SYSUTCDATETIME(),
        IsActive BIT NOT NULL
            CONSTRAINT DF_WhatsAppConversations_IsActive DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL
            CONSTRAINT DF_WhatsAppConversations_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NULL
    );

    CREATE INDEX IX_WhatsAppConversations_Number
    ON WhatsAppConversations (WhatsAppNumber);

    CREATE INDEX IX_WhatsAppConversations_CustomerId
    ON WhatsAppConversations (CustomerId);

    CREATE INDEX IX_WhatsAppConversations_CurrentState
    ON WhatsAppConversations (CurrentState);
END;

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'WhatsAppConversationMessages'
)
BEGIN
    CREATE TABLE WhatsAppConversationMessages (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        ConversationId BIGINT NULL,
        CustomerId BIGINT NULL,
        WhatsAppNumber NVARCHAR(20) NOT NULL,
        MetaMessageId NVARCHAR(255) NULL,
        Direction NVARCHAR(20) NOT NULL,
        MessageType NVARCHAR(50) NOT NULL,
        UserText NVARCHAR(MAX) NULL,
        InteractiveId NVARCHAR(255) NULL,
        InteractiveTitle NVARCHAR(255) NULL,
        PayloadJson NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2 NOT NULL
            CONSTRAINT DF_WhatsAppConversationMessages_CreatedAt DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_WhatsAppConversationMessages_Number
    ON WhatsAppConversationMessages (WhatsAppNumber);

    CREATE INDEX IX_WhatsAppConversationMessages_MetaMessageId
    ON WhatsAppConversationMessages (MetaMessageId);

    CREATE INDEX IX_WhatsAppConversationMessages_ConversationId
    ON WhatsAppConversationMessages (ConversationId);

    CREATE INDEX IX_WhatsAppConversationMessages_CustomerId
    ON WhatsAppConversationMessages (CustomerId);
END;

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'WhatsAppMenuActions'
)
BEGIN
    CREATE TABLE WhatsAppMenuActions (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        MenuKey NVARCHAR(100) NOT NULL,
        ActionId NVARCHAR(100) NOT NULL,
        Title NVARCHAR(100) NOT NULL,
        Description NVARCHAR(255) NULL,
        NextState NVARCHAR(100) NULL,
        SortOrder INT NOT NULL
            CONSTRAINT DF_WhatsAppMenuActions_SortOrder DEFAULT 0,
        IsActive BIT NOT NULL
            CONSTRAINT DF_WhatsAppMenuActions_IsActive DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL
            CONSTRAINT DF_WhatsAppMenuActions_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NULL
    );

    CREATE INDEX IX_WhatsAppMenuActions_MenuKey
    ON WhatsAppMenuActions (MenuKey);

    CREATE INDEX IX_WhatsAppMenuActions_ActionId
    ON WhatsAppMenuActions (ActionId);
END;

IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'WhatsAppMenuActions'
)
AND NOT EXISTS (
    SELECT 1 FROM WhatsAppMenuActions
)
BEGIN
    INSERT INTO WhatsAppMenuActions (
        MenuKey,
        ActionId,
        Title,
        Description,
        NextState,
        SortOrder,
        IsActive
    )
    VALUES
    ('MAIN_MENU', 'MAIN_ORDERS', 'Orders', 'View recent orders', 'ORDER_LIST', 1, 1),
    ('MAIN_MENU', 'MAIN_BALANCE', 'Balance', 'Check account balance', 'BALANCE', 2, 1),
    ('MAIN_MENU', 'MAIN_CREDIT', 'Credit', 'Check available credit', 'CREDIT', 3, 1),
    ('MAIN_MENU', 'MAIN_SUPPORT', 'Support', 'Contact support team', 'SUPPORT', 4, 1),
    ('NAVIGATION', 'BACK_MAIN', 'Main Menu', 'Go back to main menu', 'MAIN_MENU', 1, 1);
END;
