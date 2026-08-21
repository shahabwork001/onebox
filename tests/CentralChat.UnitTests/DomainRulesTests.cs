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

    [Fact]
    public void Resolving_an_open_ticket_stamps_the_resolution_time()
    {
        var ticket = OpenTicket();

        ticket.Resolve();

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.NotNull(ticket.ResolvedAt);
        Assert.Null(ticket.ClosedAt);
        Assert.True(ticket.IsTerminal);
    }

    [Fact]
    public void Closing_a_ticket_stamps_the_closure_time()
    {
        var ticket = OpenTicket();

        ticket.Close();

        Assert.Equal(TicketStatus.Closed, ticket.Status);
        Assert.NotNull(ticket.ClosedAt);
        Assert.False(ticket.CanClose);
    }

    [Fact]
    public void Reopening_clears_the_terminal_timestamps()
    {
        var ticket = OpenTicket();
        ticket.Resolve();
        ticket.Close();

        ticket.Reopen();

        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Null(ticket.ResolvedAt);
        Assert.Null(ticket.ClosedAt);
        Assert.False(ticket.IsTerminal);
    }

    [Fact]
    public void A_closed_ticket_cannot_be_resolved()
    {
        var ticket = OpenTicket();
        ticket.Close();

        Assert.False(ticket.CanResolve);
        Assert.Throws<InvalidOperationException>(ticket.Resolve);
    }

    [Fact]
    public void An_active_ticket_cannot_be_reopened()
    {
        var ticket = OpenTicket();

        Assert.False(ticket.CanReopen);
        Assert.Throws<InvalidOperationException>(ticket.Reopen);
    }

    [Fact]
    public void Unassigning_a_resolved_ticket_leaves_it_resolved()
    {
        var ticket = OpenTicket();
        ticket.Resolve();

        ticket.Assign(null);

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Null(ticket.AssignedAgentId);
    }

    private static Ticket OpenTicket()
    {
        var ticket = new Ticket(Guid.NewGuid(), Guid.NewGuid(), "WA-TEST-LIFECYCLE");
        ticket.Assign(Guid.NewGuid());
        return ticket;
    }
}
