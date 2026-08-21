"use client";

import { FormEvent, useEffect, useRef } from "react";
import type { Agent, Message, Ticket } from "@/lib/types";
import { isTerminal, isUnclaimed } from "@/lib/types";
import { dayKeyOf, formatDayHeading, formatTime } from "@/lib/format";
import { AssignMenu } from "./AssignMenu";
import { Avatar, DeliveryTick, EmptyState, Spinner, StatusBadge } from "./Primitives";

export type ConversationActions = {
  onClaim: () => void;
  onRelease: () => void;
  onAssign: (agentId: string) => void;
  onChangeStatus: (action: "resolve" | "close" | "reopen") => void;
  onSend: (text: string) => void;
};

export function ConversationPanel({
  ticket,
  messages,
  agents,
  ownerName,
  loading,
  forbidden,
  busy,
  canAssign,
  isOwner,
  composer,
  onComposerChange,
  actions,
}: {
  ticket: Ticket | null;
  messages: Message[];
  agents: Agent[];
  ownerName: string | null;
  loading: boolean;
  forbidden: boolean;
  busy: boolean;
  canAssign: boolean;
  isOwner: boolean;
  composer: string;
  onComposerChange: (value: string) => void;
  actions: ConversationActions;
}) {
  const bottom = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottom.current?.scrollIntoView({ block: "end" });
  }, [messages, ticket?.id]);

  if (!ticket) {
    return (
      <section className="conversation">
        <EmptyState
          icon="◌"
          title="Select a conversation"
          body="Choose a contact from the inbox to read the history and reply."
        />
      </section>
    );
  }

  const terminal = isTerminal(ticket);
  const unclaimed = isUnclaimed(ticket);
  const canReply = isOwner && !terminal && !forbidden;

  const composerPlaceholder = terminal
    ? "Reopen this ticket to reply"
    : unclaimed
      ? "Claim this conversation to reply"
      : isOwner
        ? "Type a reply…"
        : `Assigned to ${ownerName ?? "another agent"}`;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (composer.trim()) actions.onSend(composer.trim());
  };

  return (
    <section className="conversation">
      <header className="panel-header conversation-header">
        <Avatar name={ticket.contactName} size="lg" />
        <div className="conversation-identity">
          <h2>{ticket.contactName || ticket.phoneNumber}</h2>
          <p>
            <span>{ticket.phoneNumber}</span>
            <span className="dot" aria-hidden="true">
              ·
            </span>
            <span>{ticket.number}</span>
            <StatusBadge status={ticket.status} unclaimed={unclaimed} />
            {ownerName && <span className="assigned-to">Assigned to {ownerName}</span>}
          </p>
        </div>

        <div className="actions">
          {unclaimed && (
            <button type="button" className="button button-primary" disabled={busy} onClick={actions.onClaim}>
              Claim conversation
            </button>
          )}
          {!unclaimed && !terminal && (isOwner || canAssign) && (
            <button type="button" className="button button-ghost" disabled={busy} onClick={actions.onRelease}>
              Release to queue
            </button>
          )}
          {canAssign && !terminal && (
            <AssignMenu
              agents={agents}
              assignedAgentId={ticket.assignedAgentId}
              busy={busy}
              onAssign={actions.onAssign}
            />
          )}
          {(isOwner || canAssign) && !terminal && (
            <button
              type="button"
              className="button button-ghost"
              disabled={busy}
              onClick={() => actions.onChangeStatus("resolve")}
            >
              Resolve
            </button>
          )}
          {(isOwner || canAssign) && ticket.status !== "Closed" && (
            <button
              type="button"
              className="button button-ghost"
              disabled={busy}
              onClick={() => actions.onChangeStatus("close")}
            >
              Close
            </button>
          )}
          {(isOwner || canAssign) && terminal && (
            <button
              type="button"
              className="button button-primary"
              disabled={busy}
              onClick={() => actions.onChangeStatus("reopen")}
            >
              Reopen
            </button>
          )}
        </div>
      </header>

      <div className="messages">
        {forbidden ? (
          <EmptyState
            icon="🔒"
            title="Claim to read this conversation"
            body="This conversation is not assigned to you. Claim it from the header to open the history and reply."
          />
        ) : loading ? (
          <Spinner label="Loading messages…" />
        ) : messages.length === 0 ? (
          <EmptyState icon="✉" title="No messages yet" body="Nothing has been exchanged on this ticket." />
        ) : (
          <MessageStream messages={messages} />
        )}
        <div ref={bottom} />
      </div>

      <form className="composer" onSubmit={submit}>
        <textarea
          value={composer}
          onChange={event => onComposerChange(event.target.value)}
          placeholder={composerPlaceholder}
          disabled={!canReply || busy}
          aria-label="Reply"
          onKeyDown={event => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              event.currentTarget.form?.requestSubmit();
            }
          }}
        />
        <button type="submit" className="button button-primary" disabled={!canReply || busy || !composer.trim()}>
          Send
        </button>
      </form>
    </section>
  );
}

function MessageStream({ messages }: { messages: Message[] }) {
  let lastDay = "";

  return (
    <>
      {messages.map(message => {
        const day = dayKeyOf(message.timestamp);
        const heading = day === lastDay ? null : formatDayHeading(message.timestamp);
        lastDay = day;
        const outbound = message.direction === "Outbound";

        return (
          <div key={message.id} className="message-group">
            {heading && <div className="day-divider">{heading}</div>}
            <div className={outbound ? "bubble bubble-outbound" : "bubble bubble-inbound"}>
              <p>{message.text || `Unsupported ${message.type.toLowerCase()} message`}</p>
              <small>
                <time dateTime={message.timestamp}>{formatTime(message.timestamp)}</time>
                {outbound && <DeliveryTick status={message.status} />}
              </small>
            </div>
          </div>
        );
      })}
    </>
  );
}
