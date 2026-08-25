using JPPL.OrchidPrism.WhatsAppWebhook.Models;
using Microsoft.EntityFrameworkCore;

namespace JPPL.OrchidPrism.WhatsAppWebhook.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents => Set<WhatsAppWebhookEvent>();
    public DbSet<WhatsAppMessageLog> WhatsAppMessageLogs => Set<WhatsAppMessageLog>();
    public DbSet<WhatsAppApiSetting> WhatsAppApiSettings => Set<WhatsAppApiSetting>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<WhatsAppConversation> WhatsAppConversations => Set<WhatsAppConversation>();
    public DbSet<WhatsAppConversationMessage> WhatsAppConversationMessages => Set<WhatsAppConversationMessage>();
    public DbSet<WhatsAppMenuAction> WhatsAppMenuActions => Set<WhatsAppMenuAction>();

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
    }
}
