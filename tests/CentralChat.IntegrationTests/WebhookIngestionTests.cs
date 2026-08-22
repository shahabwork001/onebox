using System.Text.Json;
using CentralChat.Application;
using CentralChat.Domain;
using CentralChat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CentralChat.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class WebhookIngestionTests(PostgresFixture postgres)
{
    /// <summary>
    /// The regression this file exists for. A Meta payload routinely carries several messages from the
    /// same sender, and every one of them needs the same contact, conversation and ticket. Those are
    /// created once at the end by a single SaveChanges, so a DbSet query mid-loop cannot see the row
    /// added an iteration earlier. Before the fix the second message added a duplicate contact, the
    /// unique index rejected the save, and the whole event was dead-lettered with the customer's
    /// messages lost. It only reproduces against a database that actually enforces the index.
    /// </summary>
    [SkippableFact]
    public async Task Several_messages_from_one_new_contact_are_all_kept()
    {
        Skip.IfNot(postgres.Available, PostgresFixture.SkipReason);
        await using var db = await postgres.CreateDatabaseAsync();
        var service = Build(db);

        var eventId = await IngestAsync(db, service, Payload("923150236808", "Ayesha Khan",
            ("Hi, my order has not arrived.", 600),
            ("The order number is UD-99231.", 540),
            ("Any update please?", 60)));

        await service.ProcessAsync(eventId, default);

        Assert.Equal(3, await db.ChatMessages.CountAsync());
        Assert.Equal(1, await db.Contacts.CountAsync());
        Assert.Equal(1, await db.Conversations.CountAsync());
        Assert.Equal(1, await db.Tickets.CountAsync());
        Assert.Equal(WebhookProcessingStatus.Processed, (await db.WebhookEvents.SingleAsync()).ProcessingStatus);
    }

    [SkippableFact]
    public async Task Messages_from_several_new_contacts_each_get_their_own_ticket()
    {
        Skip.IfNot(postgres.Available, PostgresFixture.SkipReason);
        await using var db = await postgres.CreateDatabaseAsync();
        var service = Build(db);

        var eventId = await IngestAsync(db, service, Payload("923150236808", "Ayesha", ("One", 120), ("Two", 60)));
        await service.ProcessAsync(eventId, default);

        var second = await IngestAsync(db, service, Payload("923004455661", "Bilal", ("Hello", 30)));
        await service.ProcessAsync(second, default);

        Assert.Equal(3, await db.ChatMessages.CountAsync());
        Assert.Equal(2, await db.Contacts.CountAsync());
        Assert.Equal(2, await db.Tickets.CountAsync());
        // One channel, however many senders arrive through it.
        Assert.Equal(1, await db.WhatsAppChannels.CountAsync());
    }

    /// <summary>Meta redelivers, so the same message id must never produce a second row.</summary>
    [SkippableFact]
    public async Task Reprocessing_the_same_event_adds_nothing()
    {
        Skip.IfNot(postgres.Available, PostgresFixture.SkipReason);
        await using var db = await postgres.CreateDatabaseAsync();
        var service = Build(db);

        var eventId = await IngestAsync(db, service, Payload("923150236808", "Ayesha", ("Only once", 60)));
        await service.ProcessAsync(eventId, default);
        await service.ProcessAsync(eventId, default);

        Assert.Equal(1, await db.ChatMessages.CountAsync());
        Assert.Equal(1, await db.Contacts.CountAsync());
    }

    [SkippableFact]
    public async Task Media_message_records_its_provider_id_and_keeps_the_caption_as_text()
    {
        Skip.IfNot(postgres.Available, PostgresFixture.SkipReason);
        await using var db = await postgres.CreateDatabaseAsync();
        var service = Build(db);

        var payload = JsonSerializer.Serialize(new
        {
            entry = new[]
            {
                new
                {
                    changes = new[]
                    {
                        new
                        {
                            value = new
                            {
                                metadata = new { phone_number_id = "708123456789012" },
                                contacts = new[] { new { profile = new { name = "Ayesha" }, wa_id = "923150236808" } },
                                messages = new object[]
                                {
                                    new
                                    {
                                        id = "wamid.media.1",
                                        from = "923150236808",
                                        timestamp = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds().ToString(),
                                        type = "image",
                                        image = new { id = "META-MEDIA-1", mime_type = "image/jpeg", caption = "The damaged part" },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        });

        var eventId = await IngestAsync(db, service, payload);
        await service.ProcessAsync(eventId, default);

        var message = await db.ChatMessages.SingleAsync();
        Assert.Equal(MessageType.Image, message.Type);
        Assert.Equal("The damaged part", message.TextBody);
        Assert.Equal("META-MEDIA-1", message.MediaId);
        Assert.Equal("image/jpeg", message.MimeType);
        // The binary is fetched by a queued job, so nothing is stored yet.
        Assert.False(message.HasStoredMedia);
        Assert.Contains(await db.OutboxMessages.ToListAsync(), x => x.Type == "WhatsAppMediaDownloadRequested");
    }

    private static WebhookIngestionService Build(CentralChatDbContext db) =>
        new(db,
            Options.Create(new MetaWhatsAppOptions { VerifyToken = "t", AppSecret = "s", AccessToken = "a", ValidateSignature = false }),
            new NoopRealtime(),
            new NoopBroadcaster(),
            NullLogger<WebhookIngestionService>.Instance);

    private static async Task<Guid> IngestAsync(CentralChatDbContext db, WebhookIngestionService service, string payload)
    {
        var result = await service.IngestAsync(payload, default);
        db.ChangeTracker.Clear();
        return result.EventId;
    }

    private static string Payload(string waId, string name, params (string Text, int SecondsAgo)[] messages) =>
        JsonSerializer.Serialize(new
        {
            entry = new[]
            {
                new
                {
                    changes = new[]
                    {
                        new
                        {
                            value = new
                            {
                                metadata = new { phone_number_id = "708123456789012" },
                                contacts = new[] { new { profile = new { name }, wa_id = waId } },
                                messages = messages.Select((m, index) => new
                                {
                                    id = $"wamid.{waId}.{index}",
                                    from = waId,
                                    timestamp = DateTimeOffset.UtcNow.AddSeconds(-m.SecondsAgo).ToUnixTimeSeconds().ToString(),
                                    type = "text",
                                    text = new { body = m.Text },
                                }).ToArray(),
                            },
                        },
                    },
                },
            },
        });

    private sealed class NoopRealtime : IRealtimeNotifier
    {
        public Task UserAsync(Guid userId, string name, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task UnassignedAsync(string name, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task WorkspaceAsync(string name, object payload, CancellationToken ct) => Task.CompletedTask;
        public Task ConversationAsync(Guid conversationId, string name, object payload, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoopBroadcaster : ITicketBroadcaster
    {
        public Task UpsertedAsync(Guid ticketId, CancellationToken ct) => Task.CompletedTask;
        public Task RemovedAsync(Guid ticketId, CancellationToken ct) => Task.CompletedTask;
    }
}
