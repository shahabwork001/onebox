using CentralChat.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CentralChat.Infrastructure;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class PermissionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public sealed class CentralChatDbContext(DbContextOptions<CentralChatDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<WhatsAppChannel> WhatsAppChannels => Set<WhatsAppChannel>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ContactAssignmentHistory> AssignmentHistory => Set<ContactAssignmentHistory>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("centralchat");
        b.Entity<WhatsAppChannel>().HasIndex(x => x.PhoneNumberId).IsUnique();
        b.Entity<Campaign>().HasIndex(x => x.CreatedAt);
        // A campaign reaches a contact once; the unique index is what makes a redelivered send a no-op.
        b.Entity<CampaignRecipient>().HasIndex(x => new { x.CampaignId, x.ContactId }).IsUnique();
        b.Entity<CampaignRecipient>().HasIndex(x => x.ExternalMessageId);
        b.Entity<Contact>().HasIndex(x => x.MarketingOptOut);
        b.Entity<Contact>().HasIndex(x => new { x.ChannelId, x.WhatsAppUserId }).IsUnique();
        b.Entity<Contact>().HasIndex(x => x.PhoneNumber);
        b.Entity<Contact>().HasIndex(x => x.CurrentAssignedAgentId);
        b.Entity<Conversation>().HasIndex(x => new { x.ContactId, x.ChannelId, x.Status });
        b.Entity<Conversation>().HasIndex(x => x.LastMessageAt);
        b.Entity<Ticket>().HasIndex(x => x.TicketNumber).IsUnique();
        b.Entity<Ticket>().HasIndex(x => new { x.ContactId, x.Status });
        b.Entity<Ticket>().HasIndex(x => x.AssignedAgentId);
        b.Entity<Ticket>().HasIndex(x => x.AssignedTeamId);
        b.Entity<Ticket>().HasIndex(x => x.LastActivityAt);
        b.Entity<ChatMessage>().HasIndex(x => x.ExternalMessageId).IsUnique().HasFilter("\"ExternalMessageId\" IS NOT NULL");
        b.Entity<ChatMessage>().HasIndex(x => new { x.ConversationId, x.ProviderTimestamp, x.Id });
        b.Entity<ChatMessage>().HasIndex(x => x.ContactId);
        b.Entity<WebhookEvent>().HasIndex(x => x.PayloadHash).IsUnique();
        b.Entity<WebhookEvent>().HasIndex(x => x.ExternalEventId);
        b.Entity<InboxMessage>().HasIndex(x => new { x.Consumer, x.MessageId }).IsUnique();
        // Shape for the dashboard's aggregate query; it maps to no table of its own.
        b.Entity<FirstResponseRow>().HasNoKey().ToView(null);
        // The publisher polls for unpublished work every couple of seconds. A partial index keeps that
        // scan proportional to the backlog rather than to the full history of everything ever sent.
        b.Entity<OutboxMessage>().HasIndex(x => x.OccurredAt).HasFilter("\"ProcessedAt\" IS NULL");
        b.Entity<OutboxMessage>().HasIndex(x => x.ProcessedAt);
        b.Entity<InboxMessage>().HasIndex(x => x.ProcessedAt);
        b.Entity<WebhookEvent>().HasIndex(x => x.ReceivedAt);
        b.Entity<TeamMember>().HasKey(x => new { x.TeamId, x.UserId });
        b.Entity<RefreshToken>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<PermissionRecord>().HasIndex(x => x.Name).IsUnique();
        b.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.PermissionId });
        b.Entity<RolePermission>().HasOne<IdentityRole<Guid>>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RolePermission>().HasOne<PermissionRecord>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OutboxMessage>().HasKey(x => x.Id);
        b.Entity<InboxMessage>().HasKey(x => x.Id);
        b.Entity<AuditLog>().HasKey(x => x.Id);
        b.Entity<TeamMember>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId);
        b.Entity<TeamMember>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
    }
}
