using CentralChat.Domain;

namespace CentralChat.UnitTests;

public sealed class DomainRulesTests
{
    [Fact]
    public void Ticket_assignment_is_independent_from_conversation_history()
    {
        var contactId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var ticket = new Ticket(contactId, conversationId, "WA-TEST-1");
        var firstAgent = Guid.NewGuid();
        var secondAgent = Guid.NewGuid();

        ticket.Assign(firstAgent);
        ticket.Assign(secondAgent);

        Assert.Equal(secondAgent, ticket.AssignedAgentId);
        Assert.Equal(conversationId, ticket.ConversationId);
        Assert.Equal(TicketStatus.Open, ticket.Status);
    }

    [Fact]
    public void Contact_ownership_survives_future_message_activity()
    {
        var contact = new Contact(Guid.NewGuid(), "+923001111111", "923001111111", "Customer");
        var agent = Guid.NewGuid();
        contact.Assign(agent);

        contact.Touch(DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(agent, contact.CurrentAssignedAgentId);
        Assert.NotNull(contact.LastMessageAt);
    }

    [Fact]
    public void Outbound_message_starts_queued_and_records_provider_result()
    {
        var message = new ChatMessage(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MessageDirection.Outbound, MessageType.Text, "Sure, let me check.", null, DateTimeOffset.UtcNow);

        Assert.Equal(MessageStatus.Queued, message.Status);
        message.MarkSent("wamid.test");

        Assert.Equal(MessageStatus.Sent, message.Status);
        Assert.Equal("wamid.test", message.ExternalMessageId);
        Assert.NotNull(message.SentAt);
    }

    [Fact]
    public void Assignment_history_preserves_previous_and_new_owner()
    {
        var previous = Guid.NewGuid();
        var next = Guid.NewGuid();
        var history = new ContactAssignmentHistory(Guid.NewGuid(), Guid.NewGuid(), previous, next, Guid.NewGuid(), AssignmentAction.Reassigned, "Shift handover");

        Assert.Equal(previous, history.PreviousAgentId);
        Assert.Equal(next, history.NewAgentId);
        Assert.Equal(AssignmentAction.Reassigned, history.Action);
    }
}
