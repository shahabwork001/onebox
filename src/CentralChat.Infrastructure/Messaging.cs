using System.Text;
using System.Text.Json;
using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CentralChat.Infrastructure;

public sealed record IntegrationEnvelope(Guid MessageId, string Type, string Payload);

public sealed class RabbitConnection : IDisposable
{
    private readonly ConnectionFactory _factory;
    private IConnection? _connection;
    public RabbitConnection(IOptions<RabbitMqOptions> options)
    {
        var o = options.Value; _factory = new ConnectionFactory { HostName = o.Host, Port = o.Port, UserName = o.UserName, Password = o.Password, VirtualHost = o.VirtualHost, DispatchConsumersAsync = true, AutomaticRecoveryEnabled = true };
    }
    public IModel CreateChannel()
    {
        _connection ??= _factory.CreateConnection("centralchat-api");
        var channel = _connection.CreateModel();
        channel.ExchangeDeclare("centralchat.events", ExchangeType.Topic, durable: true);
        channel.ExchangeDeclare("centralchat.deadletter", ExchangeType.Fanout, durable: true);
        channel.QueueDeclare("centralchat.work", durable: true, exclusive: false, autoDelete: false, new Dictionary<string, object> { ["x-dead-letter-exchange"] = "centralchat.deadletter" });
        channel.QueueDeclare("centralchat.dead", durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind("centralchat.work", "centralchat.events", "#");
        channel.QueueBind("centralchat.dead", "centralchat.deadletter", "");
        return channel;
    }
    public void Dispose() => _connection?.Dispose();
}

public sealed class OutboxPublisher(IServiceScopeFactory scopes, RabbitConnection rabbit, ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralChatDbContext>();
                var items = await db.OutboxMessages.Where(x => x.ProcessedAt == null && x.RetryCount < 20).OrderBy(x => x.OccurredAt).Take(50).ToListAsync(stoppingToken);
                if (items.Count == 0) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); continue; }
                using var channel = rabbit.CreateChannel(); channel.ConfirmSelect();
                foreach (var item in items)
                {
                    try
                    {
                        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new IntegrationEnvelope(item.Id, item.Type, item.Payload)));
                        var props = channel.CreateBasicProperties(); props.Persistent = true; props.MessageId = item.Id.ToString(); props.ContentType = "application/json";
                        channel.BasicPublish("centralchat.events", item.Type, props, bytes); channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5)); item.ProcessedAt = DateTimeOffset.UtcNow;
                    }
                    catch (Exception ex) { item.RetryCount++; item.Error = ex.Message[..Math.Min(ex.Message.Length, 1000)]; logger.LogWarning(ex, "Could not publish outbox message {OutboxMessageId}", item.Id); }
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogWarning(ex, "Outbox publisher iteration failed"); await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        }
    }
}

public sealed class RabbitConsumer(IServiceScopeFactory scopes, RabbitConnection rabbit, ILogger<RabbitConsumer> logger) : BackgroundService
{
    private IModel? _channel;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _channel = rabbit.CreateChannel(); _channel.BasicQos(0, 8, false);
                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += async (_, ea) => await HandleAsync(ea, stoppingToken);
                _channel.BasicConsume("centralchat.work", autoAck: false, consumer);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogWarning(ex, "RabbitMQ consumer disconnected; retrying"); await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        }
    }

    private async Task HandleAsync(BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<IntegrationEnvelope>(delivery.Body.Span) ?? throw new InvalidDataException("Integration envelope was empty.");
            using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralChatDbContext>();
            if (await db.InboxMessages.AnyAsync(x => x.Consumer == "centralchat.work" && x.MessageId == envelope.MessageId.ToString(), ct)) { _channel!.BasicAck(delivery.DeliveryTag, false); return; }
            if (envelope.Type == "WhatsAppWebhookReceived")
            {
                using var payload = JsonDocument.Parse(envelope.Payload); var id = payload.RootElement.GetProperty("WebhookEventId").GetGuid();
                await scope.ServiceProvider.GetRequiredService<CentralChat.Application.IWebhookIngestionService>().ProcessAsync(id, ct);
            }
            else if (envelope.Type == "OutboundWhatsAppMessageRequested") await ProcessOutboundAsync(scope.ServiceProvider, envelope.Payload, ct);
            else if (envelope.Type == "CampaignMessageRequested") await ProcessCampaignMessageAsync(scope.ServiceProvider, envelope.Payload, ct);
            else if (envelope.Type == "WhatsAppMediaDownloadRequested")
            {
                using var payload = JsonDocument.Parse(envelope.Payload); var messageId = payload.RootElement.GetProperty("MessageId").GetGuid();
                await scope.ServiceProvider.GetRequiredService<CentralChat.Application.IMediaService>().DownloadAsync(messageId, ct);
            }
            db.InboxMessages.Add(new InboxMessage { Consumer = "centralchat.work", MessageId = envelope.MessageId.ToString() }); await db.SaveChangesAsync(ct);
            _channel!.BasicAck(delivery.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Integration message {DeliveryTag} failed", delivery.DeliveryTag);
            _channel!.BasicNack(delivery.DeliveryTag, false, requeue: !delivery.Redelivered);
        }
    }

    /// <summary>
    /// One queued message per recipient, which is what makes a broadcast resumable: a pause stops the
    /// remainder without unpicking what already went, and a redelivery cannot send twice because the
    /// recipient row has already left Pending.
    ///
    /// Consent is re-checked here rather than trusted from launch. A large send takes time, and someone
    /// who opts out while it runs must not still receive it.
    /// </summary>
    private static async Task ProcessCampaignMessageAsync(IServiceProvider services, string payload, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(payload);
        var recipientId = json.RootElement.GetProperty("RecipientId").GetGuid();
        var variables = json.RootElement.TryGetProperty("Variables", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : [];

        var db = services.GetRequiredService<CentralChatDbContext>();
        var recipient = await db.CampaignRecipients.SingleOrDefaultAsync(x => x.Id == recipientId, ct);
        if (recipient is null || recipient.Status != CampaignRecipientStatus.Pending) return;

        var campaign = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == recipient.CampaignId, ct);
        if (campaign is null) return;

        // Paused means paused. Leaving the row pending is what lets a resume pick it up again later.
        if (campaign.Status == CampaignStatus.Paused) throw new InvalidOperationException($"Campaign {campaign.Id} is paused.");
        if (campaign.Status is CampaignStatus.Completed or CampaignStatus.Failed) return;

        var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == recipient.ContactId, ct);
        if (contact is null || contact.MarketingOptOut || contact.Status != ContactStatus.Active)
        {
            recipient.Skip(contact is null ? "Contact no longer exists." : "Contact opted out or is inactive.");
            await db.SaveChangesAsync(ct);
            await CompleteCampaignIfDrainedAsync(db, campaign, ct);
            return;
        }

        var channel = await db.WhatsAppChannels.AsNoTracking().FirstOrDefaultAsync(ct);
        if (channel is null) { recipient.MarkFailed("No WhatsApp channel is configured."); await db.SaveChangesAsync(ct); return; }

        var client = services.GetRequiredService<IWhatsAppClient>();
        var result = await client.SendTemplateAsync(channel.PhoneNumberId, recipient.PhoneNumber, campaign.TemplateName, campaign.TemplateLanguage, variables, ct);

        if (result.Success && result.ExternalMessageId is not null) recipient.MarkSent(result.ExternalMessageId);
        else recipient.MarkFailed(result.Error ?? "Unknown provider failure.");

        await db.SaveChangesAsync(ct);
        await CompleteCampaignIfDrainedAsync(db, campaign, ct);
    }

    private static async Task CompleteCampaignIfDrainedAsync(CentralChatDbContext db, Campaign campaign, CancellationToken ct)
    {
        if (campaign.Status != CampaignStatus.Sending) return;
        var pending = await db.CampaignRecipients.CountAsync(x => x.CampaignId == campaign.Id && x.Status == CampaignRecipientStatus.Pending, ct);
        if (pending > 0) return;
        campaign.Complete();
        await db.SaveChangesAsync(ct);
    }

    private static async Task ProcessOutboundAsync(IServiceProvider services, string payload, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(payload); var id = json.RootElement.GetProperty("MessageId").GetGuid();
        var db = services.GetRequiredService<CentralChatDbContext>(); var message = await db.ChatMessages.SingleAsync(x => x.Id == id, ct);
        if (message.Status != MessageStatus.Queued) return;
        var contact = await db.Contacts.SingleAsync(x => x.Id == message.ContactId, ct); var channel = await db.WhatsAppChannels.SingleAsync(x => x.Id == message.ChannelId, ct);
        var client = services.GetRequiredService<IWhatsAppClient>();

        // An attachment travels the same durable path as a reply; only the provider call differs.
        WhatsAppSendResult result;
        if (message.HasStoredMedia)
        {
            var store = services.GetRequiredService<CentralChat.Application.IMediaStore>();
            await using var content = await store.OpenAsync(message.MediaUrl!, ct)
                ?? throw new InvalidOperationException($"Stored media for message {message.Id} is missing.");
            result = await client.SendMediaAsync(channel.PhoneNumberId, contact.PhoneNumber, content,
                message.MimeType ?? "application/octet-stream", $"{message.Id}", message.Type, message.TextBody, ct);
        }
        else
        {
            result = await client.SendTextAsync(channel.PhoneNumberId, contact.PhoneNumber, message.TextBody!, ct);
        }
        if (result.Success && result.ExternalMessageId is not null) message.MarkSent(result.ExternalMessageId); else message.MarkFailed(result.Error ?? "Unknown provider failure");
        await db.SaveChangesAsync(ct);
        var notifier = services.GetRequiredService<IRealtimeNotifier>(); await notifier.ConversationAsync(message.ConversationId, result.Success ? "message.sent" : "message.failed", new { message.Id, message.Status, message.ExternalMessageId, message.FailureReason }, ct);
    }

    public override void Dispose() { _channel?.Dispose(); base.Dispose(); }
}
