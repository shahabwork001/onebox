"use client";

import { FormEvent, useEffect, useRef } from "react";
import type { Agent, Message, Ticket } from "@/lib/types";
import { hasAttachment, isTerminal, isUnclaimed } from "@/lib/types";
import { dayKeyOf, formatDayHeading, formatTime } from "@/lib/format";
import { ActionMenu, type MenuAction } from "./ActionMenu";
import { AssignMenu } from "./AssignMenu";
import { MediaAttachment } from "./MediaAttachment";
import { Avatar, DeliveryTick, EmptyState, Spinner, StatusBadge } from "./Primitives";
import { IconBack, IconLock, IconMessage, IconSelect, IconSend } from "./icons";

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
  token,
  onComposerChange,
  onBack,
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
  token: string;
  onComposerChange: (value: string) => void;
  onBack: () => void;
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
          icon={<IconSelect size={30} />}
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

  // Everything except the one action an agent came to perform goes behind the overflow trigger.
  const secondary: MenuAction[] = [];
  if (!unclaimed && !terminal && (isOwner || canAssign)) secondary.push({ label: "Release to queue", onSelect: actions.onRelease });
  if ((isOwner || canAssign) && !terminal) secondary.push({ label: "Resolve", onSelect: () => actions.onChangeStatus("resolve") });
  if ((isOwner || canAssign) && ticket.status !== "Closed") secondary.push({ label: "Close", onSelect: () => actions.onChangeStatus("close"), tone: "danger" });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (composer.trim()) actions.onSend(composer.trim());
  };

  return (
    <section className="conversation">
      <header className="panel-header conversation-header">
        {/* Only rendered on narrow layouts, where the list and the thread share one column. */}
        <button type="button" className="back-button" onClick={onBack} aria-label="Back to conversations">
          <IconBack size={19} />
        </button>
        <Avatar name={ticket.contactName} size="lg" />
        <div className="conversation-identity">
          <h2>{ticket.contactName || ticket.phoneNumber}</h2>
          <p title={`${ticket.number} · ${ownerName ? `assigned to ${ownerName}` : "unassigned"}`}>
            <span className="identity-phone">{ticket.phoneNumber}</span>
            <StatusBadge status={ticket.status} unclaimed={unclaimed} />
            {ownerName && <span className="assigned-to">{ownerName}</span>}
          </p>
        </div>

        <div className="actions">
          {unclaimed && (
            <button type="button" className="button button-primary" disabled={busy} onClick={actions.onClaim}>
              Claim
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
          {canAssign && !terminal && (
            <AssignMenu
              agents={agents}
              assignedAgentId={ticket.assignedAgentId}
              busy={busy}
              onAssign={actions.onAssign}
            />
          )}
          <ActionMenu actions={secondary} busy={busy} />
        </div>
      </header>

      <div className="messages">
        {forbidden ? (
          <EmptyState
            icon={<IconLock size={28} />}
            title="Claim to read this conversation"
            body="This conversation is not assigned to you. Claim it from the header to open the history and reply."
          />
        ) : loading ? (
          <Spinner label="Loading messages…" />
        ) : messages.length === 0 ? (
          <EmptyState icon={<IconMessage size={28} />} title="No messages yet" body="Nothing has been exchanged on this ticket." />
        ) : (
          <MessageStream messages={messages} token={token} />
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
        <button type="submit" className="button button-primary composer-send" disabled={!canReply || busy || !composer.trim()}>
          <IconSend size={16} />
          <span>Send</span>
        </button>
      </form>
    </section>
  );
}

function MessageStream({ messages, token }: { messages: Message[]; token: string }) {
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
              {hasAttachment(message) && <MediaAttachment message={message} token={token} />}
              {message.text && <p>{message.text}</p>}
              {!message.text && !hasAttachment(message) && <p className="message-empty">No content</p>}
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
