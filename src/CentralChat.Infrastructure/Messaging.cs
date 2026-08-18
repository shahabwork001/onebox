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
            db.InboxMessages.Add(new InboxMessage { Consumer = "centralchat.work", MessageId = envelope.MessageId.ToString() }); await db.SaveChangesAsync(ct);
            _channel!.BasicAck(delivery.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Integration message {DeliveryTag} failed", delivery.DeliveryTag);
            _channel!.BasicNack(delivery.DeliveryTag, false, requeue: !delivery.Redelivered);
        }
    }

    private static async Task ProcessOutboundAsync(IServiceProvider services, string payload, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(payload); var id = json.RootElement.GetProperty("MessageId").GetGuid();
        var db = services.GetRequiredService<CentralChatDbContext>(); var message = await db.ChatMessages.SingleAsync(x => x.Id == id, ct);
        if (message.Status != MessageStatus.Queued) return;
        var contact = await db.Contacts.SingleAsync(x => x.Id == message.ContactId, ct); var channel = await db.WhatsAppChannels.SingleAsync(x => x.Id == message.ChannelId, ct);
        var client = services.GetRequiredService<IWhatsAppClient>(); var result = await client.SendTextAsync(channel.PhoneNumberId, contact.PhoneNumber, message.TextBody!, ct);
        if (result.Success && result.ExternalMessageId is not null) message.MarkSent(result.ExternalMessageId); else message.MarkFailed(result.Error ?? "Unknown provider failure");
        await db.SaveChangesAsync(ct);
        var notifier = services.GetRequiredService<IRealtimeNotifier>(); await notifier.ConversationAsync(message.ConversationId, result.Success ? "message.sent" : "message.failed", new { message.Id, message.Status, message.ExternalMessageId, message.FailureReason }, ct);
    }

    public override void Dispose() { _channel?.Dispose(); base.Dispose(); }
}
