import type { Scope, StatusFilter, Ticket } from "@/lib/types";
import { isUnclaimed } from "@/lib/types";
import { formatListTimestamp } from "@/lib/format";
import { Avatar, OwnerChip, Spinner, StatusBadge } from "./Primitives";

const FILTERS: { key: StatusFilter; label: string }[] = [
  { key: "active", label: "Active" },
  { key: "new", label: "New" },
  { key: "resolved", label: "Resolved" },
  { key: "closed", label: "Closed" },
  { key: "all", label: "All" },
];

const TITLES: Record<Scope, string> = {
  mine: "My inbox",
  unassigned: "Unassigned",
  all: "All conversations",
};

export function InboxPanel({
  scope,
  status,
  tickets,
  search,
  loading,
  selectedId,
  agentNameOf,
  onStatusChange,
  onSearchChange,
  onSelect,
  onRefresh,
}: {
  scope: Scope;
  status: StatusFilter;
  tickets: Ticket[];
  search: string;
  loading: boolean;
  selectedId: string | null;
  agentNameOf: (agentId: string | null) => string | null;
  onStatusChange: (status: StatusFilter) => void;
  onSearchChange: (search: string) => void;
  onSelect: (ticket: Ticket) => void;
  onRefresh: () => void;
}) {
  return (
    <section className="inbox" aria-label={TITLES[scope]}>
      <header className="panel-header">
        <div>
          <h1>{TITLES[scope]}</h1>
          <p>
            {tickets.length} {tickets.length === 1 ? "conversation" : "conversations"}
          </p>
        </div>
        <button type="button" className="icon-button" onClick={onRefresh} title="Refresh" aria-label="Refresh">
          ↻
        </button>
      </header>

      <div className="search">
        <span aria-hidden="true">⌕</span>
        <input
          value={search}
          onChange={event => onSearchChange(event.target.value)}
          placeholder="Search name, number or message"
          aria-label="Search conversations"
        />
        {search && (
          <button type="button" className="search-clear" onClick={() => onSearchChange("")} aria-label="Clear search">
            ×
          </button>
        )}
      </div>

      <div className="filters" role="group" aria-label="Filter by status">
        {FILTERS.map(filter => (
          <button
            key={filter.key}
            type="button"
            aria-pressed={status === filter.key}
            className={status === filter.key ? "chip is-active" : "chip"}
            onClick={() => onStatusChange(filter.key)}
          >
            {filter.label}
          </button>
        ))}
      </div>

      <div className="ticket-list">
        {loading && tickets.length === 0 && <Spinner label="Loading conversations…" />}

        {!loading && tickets.length === 0 && (
          <p className="list-empty">
            {search ? "No conversations match your search." : "This queue is empty."}
          </p>
        )}

        {tickets.map(ticket => {
          const owner = agentNameOf(ticket.assignedAgentId);
          return (
            <button
              key={ticket.id}
              type="button"
              className={selectedId === ticket.id ? "ticket is-selected" : "ticket"}
              onClick={() => onSelect(ticket)}
            >
              <Avatar name={ticket.contactName} />
              <span className="ticket-copy">
                <b>{ticket.contactName || ticket.phoneNumber}</b>
                <small>{ticket.lastMessage || "No message preview"}</small>
                {owner && <OwnerChip name={owner} />}
              </span>
              <span className="ticket-meta">
                <time dateTime={ticket.lastActivityAt}>{formatListTimestamp(ticket.lastActivityAt)}</time>
                <StatusBadge status={ticket.status} unclaimed={isUnclaimed(ticket)} />
              </span>
            </button>
          );
        })}
      </div>
    </section>
  );
}
