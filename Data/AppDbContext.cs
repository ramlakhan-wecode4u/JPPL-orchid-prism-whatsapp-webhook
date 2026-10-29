using JPPL.OrchidPrism.WhatsAppWebhook.Models;
using Microsoft.EntityFrameworkCore;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents => Set<WhatsAppWebhookEvent>();
    public DbSet<WhatsAppMessageLog> WhatsAppMessageLogs => Set<WhatsAppMessageLog>();
    public DbSet<WhatsAppApiSetting> WhatsAppApiSettings => Set<WhatsAppApiSetting>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<WhatsAppConversation> WhatsAppConversations => Set<WhatsAppConversation>();
    public DbSet<WhatsAppConversationMessage> WhatsAppConversationMessages => Set<WhatsAppConversationMessage>();
    public DbSet<WhatsAppMenuAction> WhatsAppMenuActions => Set<WhatsAppMenuAction>();

    // Read-only tables owned by the main Orchid Prism project.
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<EstimateVersion> EstimateVersions => Set<EstimateVersion>();
    public DbSet<LookupStatus> Statuses => Set<LookupStatus>();
    public DbSet<CustomerBill> CustomerBills => Set<CustomerBill>();
    public DbSet<CustomerBalance> CustomerBalances => Set<CustomerBalance>();
    public DbSet<CustomerPaymentTerm> CustomerPaymentTerms => Set<CustomerPaymentTerm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WhatsAppWebhookEvent>(entity =>
        {
            entity.ToTable("WhatsAppWebhookEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.RawPayload).HasColumnType("nvarchar(max)");
            entity.Property(item => item.ProcessingError).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<WhatsAppMessageLog>(entity =>
        {
            entity.ToTable("WhatsAppMessageLogs");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.MetaMessageId);
            entity.Property(item => item.Id).HasColumnType("bigint");
            entity.Property(item => item.CustomerId).HasColumnType("bigint");
            entity.Property(item => item.WhatsAppNumber).HasMaxLength(20).IsRequired();
            entity.Property(item => item.MessageType).HasMaxLength(50);
            entity.Property(item => item.TemplateName).HasMaxLength(100);
            entity.Property(item => item.MetaMessageId).HasMaxLength(255);
            entity.Property(item => item.Direction).HasMaxLength(20);
            entity.Property(item => item.Status).HasMaxLength(50);
            entity.Property(item => item.ErrorCode).HasMaxLength(100);
            entity.Property(item => item.Payload).HasColumnType("nvarchar(max)");
            entity.Property(item => item.ApiResponse).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<WhatsAppApiSetting>(entity =>
        {
            entity.ToTable("WhatsAppApiSettings");
            entity.HasKey(item => item.Id);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.WhatsAppNumber).HasMaxLength(20);
        });

        modelBuilder.Entity<CustomerContact>(entity =>
        {
            entity.ToTable("CustomerContacts");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.WhatsAppNumber).HasMaxLength(20);
            entity.HasIndex(item => item.CustomerId);
        });

        modelBuilder.Entity<WhatsAppConversation>(entity =>
        {
            entity.ToTable("WhatsAppConversations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnType("bigint");
            entity.Property(item => item.CustomerId).HasColumnType("bigint");
            entity.Property(item => item.WhatsAppNumber).HasMaxLength(20).IsRequired();
            entity.Property(item => item.CurrentState).HasMaxLength(100);
            entity.Property(item => item.LastMessageId).HasMaxLength(255);
            entity.Property(item => item.ContextJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(item => item.WhatsAppNumber);
            entity.HasIndex(item => item.CustomerId);
            entity.HasIndex(item => item.CurrentState);
        });

        modelBuilder.Entity<WhatsAppConversationMessage>(entity =>
        {
            entity.ToTable("WhatsAppConversationMessages");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnType("bigint");
            entity.Property(item => item.ConversationId).HasColumnType("bigint");
            entity.Property(item => item.CustomerId).HasColumnType("bigint");
            entity.Property(item => item.WhatsAppNumber).HasMaxLength(20).IsRequired();
            entity.Property(item => item.MetaMessageId).HasMaxLength(255);
            entity.Property(item => item.Direction).HasMaxLength(20);
            entity.Property(item => item.MessageType).HasMaxLength(50);
            entity.Property(item => item.UserText).HasColumnType("nvarchar(max)");
            entity.Property(item => item.InteractiveId).HasMaxLength(255);
            entity.Property(item => item.InteractiveTitle).HasMaxLength(255);
            entity.Property(item => item.PayloadJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(item => item.WhatsAppNumber);
            entity.HasIndex(item => item.MetaMessageId);
            entity.HasIndex(item => item.ConversationId);
            entity.HasIndex(item => item.CustomerId);
        });

        modelBuilder.Entity<WhatsAppMenuAction>(entity =>
        {
            entity.ToTable("WhatsAppMenuActions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.MenuKey).HasMaxLength(100);
            entity.Property(item => item.ActionId).HasMaxLength(100);
            entity.Property(item => item.Title).HasMaxLength(100);
            entity.Property(item => item.Description).HasMaxLength(255);
            entity.Property(item => item.NextState).HasMaxLength(100);
            entity.HasIndex(item => item.MenuKey);
            entity.HasIndex(item => item.ActionId);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(item => item.Id);
        });

        modelBuilder.Entity<EstimateVersion>(entity =>
        {
            entity.ToTable("EstimateVersions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.TotalPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<LookupStatus>(entity =>
        {
            entity.ToTable("LuStatus");
            entity.HasKey(item => item.Id);
        });

        modelBuilder.Entity<CustomerBill>(entity =>
        {
            entity.ToTable("CustomerBills");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.TotalAmount).HasPrecision(18, 2);
            entity.Property(item => item.PaidAmount).HasPrecision(18, 2);
            entity.Property(item => item.OutstandingAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<CustomerBalance>(entity =>
        {
            entity.ToTable("CustomerBalances");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.BalanceAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<CustomerPaymentTerm>(entity =>
        {
            entity.ToTable("CustomerPaymentTerms");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CreditLimit).HasPrecision(18, 2);
        });
    }
}
