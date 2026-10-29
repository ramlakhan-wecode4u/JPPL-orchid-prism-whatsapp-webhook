using System.Globalization;
using System.Text;
using System.Text.Json;
using JPPL.OrchidPrism.WhatsAppWebhook.Data;
using JPPL.OrchidPrism.WhatsAppWebhook.Helpers;
using JPPL.OrchidPrism.WhatsAppWebhook.Models;
using JPPL.OrchidPrism.WhatsAppWebhook.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Services;

public sealed class WhatsAppMenuService(
    AppDbContext db,
    WhatsAppMessageSender sender,
    WhatsAppAccountResolver accountResolver,
    CustomerAccountDataService accountData,
    IOptions<SupportContactOptions> supportOptions,
    ILogger<WhatsAppMenuService> logger)
{
    private const string OrderActionPrefix = "ORDER_";
    private const string AccountActionPrefix = "ACCOUNT_";
    private const string BackToMenuHint = "Type Menu to go back to main menu.";
    private const string ReplyMessageType = "CustomerReply";

    // Meta message types that are not a request from the customer and must not trigger a reply.
    private static readonly HashSet<string> SilentMetaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "reaction",
        "system",
        "unsupported",
        "ephemeral",
        "request_welcome"
    };

    private static readonly InteractiveOption[] MainMenuButtons =
    [
        new("MAIN_ORDERS", "Orders"),
        new("MAIN_BALANCE", "Balance"),
        new("MAIN_CREDIT", "Credit")
    ];

    public async Task ProcessIncomingMessageAsync(
        ParsedIncomingMessage message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.From))
        {
            logger.LogInformation("Incoming WhatsApp message had no sender number.");
            return;
        }

        // Meta retries deliveries, so a message that was already handled must not be answered twice.
        if (!string.IsNullOrWhiteSpace(message.MetaMessageId))
        {
            var alreadySaved = await db.WhatsAppConversationMessages
                .AnyAsync(item => item.MetaMessageId == message.MetaMessageId &&
                                  item.Direction == "Incoming", cancellationToken);
            if (alreadySaved)
            {
                return;
            }
        }

        var now = DateTime.UtcNow;
        var conversation = await GetOrCreateConversationAsync(message.From, now, cancellationToken);
        conversation.LastInteractionAt = now;
        conversation.LastMessageId = message.MetaMessageId;
        conversation.UpdatedAt = now;

        var incomingMessage = new WhatsAppConversationMessage
        {
            ConversationId = conversation.Id,
            CustomerId = conversation.CustomerId,
            WhatsAppNumber = message.From,
            MetaMessageId = message.MetaMessageId,
            Direction = "Incoming",
            MessageType = message.MessageType,
            UserText = message.UserText,
            InteractiveId = message.InteractiveId,
            InteractiveTitle = message.InteractiveTitle,
            PayloadJson = message.PayloadJson,
            CreatedAt = now
        };
        db.WhatsAppConversationMessages.Add(incomingMessage);

        if (SilentMetaTypes.Contains(message.MetaType))
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Accounts are looked up on every message, so removing a verification takes effect immediately.
        var accounts = await accountResolver.FindAccountsAsync(message.From, cancellationToken);

        if (accounts.Count == 0)
        {
            conversation.CustomerId = null;
            await db.SaveChangesAsync(cancellationToken);
            await sender.SendTextAsync(conversation, null,
                "Your WhatsApp number is not linked with any verified account.\n\nPlease verify your WhatsApp number on the website first.",
                ReplyMessageType,
                cancellationToken);
            return;
        }

        var actionId = ResolveActionId(message);

        // One number can own several studios: the chosen account is kept on the conversation.
        if (accounts.Count > 1)
        {
            if (actionId is not null &&
                actionId.StartsWith(AccountActionPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(actionId.AsSpan(AccountActionPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var chosenCustomerId) &&
                accounts.Any(item => item.Customer.Id == chosenCustomerId))
            {
                conversation.CustomerId = chosenCustomerId;
                actionId = "BACK_MAIN";
            }
            else if (actionId == "SWITCH_ACCOUNT" ||
                     actionId?.StartsWith(AccountActionPrefix, StringComparison.OrdinalIgnoreCase) == true ||
                     !accounts.Any(item => item.Customer.Id == conversation.CustomerId))
            {
                await SendAccountSelectionAsync(conversation, accounts, cancellationToken);
                return;
            }
        }
        else
        {
            conversation.CustomerId = accounts[0].Customer.Id;

            // An old account list or "Switch" from a number that now has one account just opens the menu.
            if (actionId == "SWITCH_ACCOUNT" ||
                actionId?.StartsWith(AccountActionPrefix, StringComparison.OrdinalIgnoreCase) == true)
            {
                actionId = "BACK_MAIN";
            }
        }

        var account = accounts.First(item => item.Customer.Id == conversation.CustomerId);
        incomingMessage.CustomerId = account.Customer.Id;

        if (actionId is null)
        {
            conversation.CurrentState = "MAIN_MENU";
            await db.SaveChangesAsync(cancellationToken);
            await SendNotUnderstoodAsync(conversation, account, cancellationToken);
            return;
        }

        await HandleActionAsync(conversation, account, accounts.Count > 1, actionId, cancellationToken);
    }

    private async Task SendAccountSelectionAsync(
        WhatsAppConversation conversation,
        IReadOnlyList<LinkedAccount> accounts,
        CancellationToken cancellationToken)
    {
        conversation.CurrentState = "ACCOUNT_SELECT";
        conversation.ContextJson = null;
        await db.SaveChangesAsync(cancellationToken);

        if (accounts.Count > WhatsAppMessageSender.MaxListRows)
        {
            logger.LogWarning(
                "Conversation {ConversationId} is linked to {AccountCount} accounts; only the first {MaxRows} can be listed.",
                conversation.Id, accounts.Count, WhatsAppMessageSender.MaxListRows);
        }

        var rows = accounts
            .Select(item => new InteractiveOption(
                AccountActionPrefix + item.Customer.Id.ToString(CultureInfo.InvariantCulture),
                item.AccountName,
                item.Customer.Name))
            .ToArray();

        await sender.SendListAsync(conversation, null,
            "Your WhatsApp number is linked with more than one account. Please select the account you want to use.",
            "Select Account",
            "Your Accounts",
            rows,
            cancellationToken);
    }

    private async Task HandleActionAsync(
        WhatsAppConversation conversation,
        LinkedAccount account,
        bool hasMultipleAccounts,
        string actionId,
        CancellationToken cancellationToken)
    {
        var customer = account.Customer;

        switch (actionId)
        {
            case "BACK_MAIN" or "MAIN_MENU":
                conversation.CurrentState = "MAIN_MENU";
                conversation.ContextJson = null;
                await db.SaveChangesAsync(cancellationToken);

                var menuText = new StringBuilder()
                    .Append($"Hello {account.DisplayName},\n\n");
                if (hasMultipleAccounts)
                {
                    menuText.Append($"Account: {account.AccountName}\n\n");
                }

                menuText.Append("Welcome to Orchid Prism. Please choose an option.\n\nFor help, type Support.");
                if (hasMultipleAccounts)
                {
                    menuText.Append("\nTo change account, type Switch.");
                }

                await sender.SendButtonsAsync(conversation, customer, menuText.ToString(), MainMenuButtons, cancellationToken);
                return;

            case "MAIN_ORDERS":
                await SendOrderListAsync(conversation, account, cancellationToken);
                return;

            case "MAIN_BALANCE":
                await SendBalanceAsync(conversation, account, cancellationToken);
                return;

            case "MAIN_CREDIT":
                await SendCreditAsync(conversation, account, cancellationToken);
                return;

            case "MAIN_SUPPORT":
                conversation.CurrentState = "SUPPORT";
                await db.SaveChangesAsync(cancellationToken);
                await sender.SendTextAsync(conversation, customer,
                    BuildSupportContactText() + "\n\n" + BackToMenuHint,
                    ReplyMessageType,
                    cancellationToken);
                return;
        }

        if (actionId.StartsWith(OrderActionPrefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(actionId.AsSpan(OrderActionPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var orderId))
        {
            await SendOrderDetailsAsync(conversation, account, orderId, cancellationToken);
            return;
        }

        conversation.CurrentState = "MAIN_MENU";
        await db.SaveChangesAsync(cancellationToken);
        await SendNotUnderstoodAsync(conversation, account, cancellationToken);
    }

    private async Task SendOrderListAsync(
        WhatsAppConversation conversation,
        LinkedAccount account,
        CancellationToken cancellationToken)
    {
        var orders = await accountData.GetRecentOrdersAsync(account.Customer.Id, cancellationToken);
        conversation.CurrentState = "ORDER_LIST";
        conversation.ContextJson = null;
        await db.SaveChangesAsync(cancellationToken);

        if (orders.Count == 0)
        {
            await sender.SendTextAsync(conversation, account.Customer,
                "No recent orders found for your account.\n\n" + BackToMenuHint,
                ReplyMessageType,
                cancellationToken);
            return;
        }

        var rows = orders
            .Select(order => new InteractiveOption(
                OrderActionPrefix + order.Id.ToString(CultureInfo.InvariantCulture),
                $"Order #{order.Id}",
                BuildOrderRowDescription(order, account.CanSeeRates)))
            .ToArray();

        await sender.SendListAsync(conversation, account.Customer,
            $"Here are your latest {orders.Count} orders. Select an order to see its details.",
            "View Orders",
            "Recent Orders",
            rows,
            cancellationToken);
    }

    private async Task SendOrderDetailsAsync(
        WhatsAppConversation conversation,
        LinkedAccount account,
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await accountData.GetOrderDetailsAsync(account.Customer.Id, orderId, cancellationToken);
        conversation.CurrentState = "ORDER_DETAIL";
        conversation.ContextJson = JsonSerializer.Serialize(new { orderId });
        await db.SaveChangesAsync(cancellationToken);

        if (order is null)
        {
            await sender.SendTextAsync(conversation, account.Customer,
                "We could not find this order on your account.\n\n" + BackToMenuHint,
                ReplyMessageType,
                cancellationToken);
            return;
        }

        var text = new StringBuilder()
            .AppendLine($"Order #{order.Id}")
            .AppendLine()
            .AppendLine($"Status: {order.StatusName}")
            .AppendLine($"Order date: {MessageFormat.Date(order.CreatedAt)}");

        if (account.CanSeeRates)
        {
            if (order.Amount is not null)
            {
                text.AppendLine($"Amount: {MessageFormat.Money(order.Amount.Value)}");
            }

            foreach (var bill in order.Bills)
            {
                text.AppendLine()
                    .AppendLine($"Bill {bill.BillNumber} ({MessageFormat.Date(bill.BillDate)})")
                    .AppendLine($"Bill amount: {MessageFormat.Money(bill.TotalAmount)}")
                    .AppendLine($"Paid: {MessageFormat.Money(bill.PaidAmount)}")
                    .AppendLine($"Outstanding: {MessageFormat.Money(bill.OutstandingAmount)}");

                if (bill.CreditDueDate is not null && bill.OutstandingAmount > 0)
                {
                    text.AppendLine($"Due date: {MessageFormat.Date(bill.CreditDueDate.Value)}");
                }

                text.AppendLine($"Bill status: {bill.Status}");
            }
        }

        text.AppendLine().Append(BackToMenuHint);

        await sender.SendTextAsync(conversation, account.Customer, text.ToString(), ReplyMessageType, cancellationToken);
    }

    private async Task SendBalanceAsync(
        WhatsAppConversation conversation,
        LinkedAccount account,
        CancellationToken cancellationToken)
    {
        conversation.CurrentState = "BALANCE";
        conversation.ContextJson = null;

        if (!account.CanSeeRates)
        {
            await db.SaveChangesAsync(cancellationToken);
            await SendRestrictedAsync(conversation, account, "balance", cancellationToken);
            return;
        }

        var balance = await accountData.GetBalanceAsync(account.Customer.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var text = new StringBuilder()
            .AppendLine("Your account balance:")
            .AppendLine()
            .AppendLine($"Advance balance: {MessageFormat.Money(balance.AdvanceBalance)}")
            .AppendLine($"Outstanding amount: {MessageFormat.Money(balance.Outstanding)}");

        if (balance.Overdue > 0)
        {
            text.AppendLine($"Overdue amount: {MessageFormat.Money(balance.Overdue)}");
        }

        text.AppendLine().Append(BackToMenuHint);

        await sender.SendTextAsync(conversation, account.Customer, text.ToString(), ReplyMessageType, cancellationToken);
    }

    private async Task SendCreditAsync(
        WhatsAppConversation conversation,
        LinkedAccount account,
        CancellationToken cancellationToken)
    {
        conversation.CurrentState = "CREDIT";
        conversation.ContextJson = null;

        if (!account.CanSeeRates)
        {
            await db.SaveChangesAsync(cancellationToken);
            await SendRestrictedAsync(conversation, account, "credit", cancellationToken);
            return;
        }

        var credit = await accountData.GetCreditAsync(account.Customer.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (credit is null)
        {
            await sender.SendTextAsync(conversation, account.Customer,
                "No credit terms are set up for your account.\n\n" + BackToMenuHint,
                ReplyMessageType,
                cancellationToken);
            return;
        }

        var text = new StringBuilder()
            .AppendLine("Your credit details:")
            .AppendLine()
            .AppendLine($"Credit limit: {MessageFormat.Money(credit.CreditLimit)}")
            .AppendLine($"Credit days: {credit.CreditDays}")
            .AppendLine($"Used credit: {MessageFormat.Money(credit.UsedCredit)}")
            .AppendLine($"Available credit: {MessageFormat.Money(credit.AvailableCredit)}");

        if (credit.Overdue > 0)
        {
            text.AppendLine($"Overdue amount: {MessageFormat.Money(credit.Overdue)}");
        }

        text.AppendLine().Append(BackToMenuHint);

        await sender.SendTextAsync(conversation, account.Customer, text.ToString(), ReplyMessageType, cancellationToken);
    }

    private Task SendRestrictedAsync(
        WhatsAppConversation conversation,
        LinkedAccount account,
        string topic,
        CancellationToken cancellationToken) =>
        sender.SendTextAsync(conversation, account.Customer,
            $"Your number does not have access to {topic} details. Please ask the account owner.\n\n" + BackToMenuHint,
            ReplyMessageType,
            cancellationToken);

    private Task SendNotUnderstoodAsync(
        WhatsAppConversation conversation,
        LinkedAccount account,
        CancellationToken cancellationToken) =>
        sender.SendTextAsync(conversation, account.Customer,
            "Sorry, I could not understand that.\n\nPlease type Menu to see available options.",
            ReplyMessageType,
            cancellationToken);

    private string BuildSupportContactText()
    {
        var options = supportOptions.Value;
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.Phone))
        {
            lines.Add($"Phone: {options.Phone.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(options.Email))
        {
            lines.Add($"Email: {options.Email.Trim()}");
        }

        if (lines.Count == 0)
        {
            logger.LogWarning("Support:Phone and Support:Email are not configured in appsettings.json.");
            return "Please contact your Orchid Prism representative for help.";
        }

        return "Please contact our support team:\n\n" + string.Join("\n", lines);
    }

    private static string BuildOrderRowDescription(OrderSummary order, bool canSeeRates)
    {
        var date = MessageFormat.Date(order.CreatedAt);
        return canSeeRates && order.Amount is not null
            ? $"{date} | {MessageFormat.Money(order.Amount.Value)} | {order.StatusName}"
            : $"{date} | {order.StatusName}";
    }

    private async Task<WhatsAppConversation> GetOrCreateConversationAsync(
        string whatsAppNumber,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var conversation = await db.WhatsAppConversations
            .Where(item => item.WhatsAppNumber == whatsAppNumber && item.IsActive)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation is not null)
        {
            return conversation;
        }

        conversation = new WhatsAppConversation
        {
            WhatsAppNumber = whatsAppNumber,
            CurrentState = "MAIN_MENU",
            LastInteractionAt = now,
            IsActive = true,
            CreatedAt = now
        };

        db.WhatsAppConversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    private static string? ResolveActionId(ParsedIncomingMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.InteractiveId))
        {
            return message.InteractiveId;
        }

        var text = message.UserText?.Trim().ToLowerInvariant();
        return text switch
        {
            "hi" or "hello" or "start" or "menu" or "0" => "BACK_MAIN",
            "1" or "orders" or "order" => "MAIN_ORDERS",
            "2" or "balance" => "MAIN_BALANCE",
            "3" or "credit" => "MAIN_CREDIT",
            "4" or "support" or "help" => "MAIN_SUPPORT",
            "switch" or "account" or "accounts" => "SWITCH_ACCOUNT",
            _ => null
        };
    }
}
