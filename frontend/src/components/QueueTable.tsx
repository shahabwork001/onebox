import type { Ticket } from "@/lib/types";
import { formatDuration, formatListTimestamp, secondsSince } from "@/lib/format";
import { Avatar, Spinner, StatusBadge } from "./Primitives";

/**
 * The unassigned queue as a worklist rather than a chat list: an agent can see who is waiting and how
 * long, and take one without opening it first. Claiming is first-come-first-served server side, so a
 * row losing the race reports a conflict rather than silently doing nothing.
 */
export function QueueTable({
  tickets,
  loading,
  busyId,
  onClaim,
  onOpen,
  onRefresh,
}: {
  tickets: Ticket[];
  loading: boolean;
  busyId: string | null;
  onClaim: (ticket: Ticket) => void;
  onOpen: (ticket: Ticket) => void;
  onRefresh: () => void;
}) {
  return (
    <section className="screen">
      <header className="panel-header">
        <div>
          <h1>Unassigned queue</h1>
          <p>
            {tickets.length} conversation{tickets.length === 1 ? "" : "s"} waiting · first to claim owns it
          </p>
        </div>
        <button type="button" className="icon-button" onClick={onRefresh} title="Refresh" aria-label="Refresh">
          ↻
        </button>
      </header>

      <div className="screen-body">
        {loading && tickets.length === 0 && <Spinner label="Loading queue…" />}

        {!loading && tickets.length === 0 && (
          <div className="queue-clear">
            <div aria-hidden="true">✓</div>
            <h2>Queue is clear</h2>
            <p>Every conversation has an owner. New ones appear here the moment they arrive.</p>
          </div>
        )}

        {tickets.length > 0 && (
          <div className="table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Contact</th>
                  <th>Ticket</th>
                  <th>Latest message</th>
                  <th className="num">Waiting</th>
                  <th>Status</th>
                  <th className="num">Action</th>
                </tr>
              </thead>
              <tbody>
                {tickets.map(ticket => {
                  const waiting = secondsSince(ticket.lastActivityAt);
                  return (
                    <tr key={ticket.id} className={waiting > 3600 ? "row-stale" : undefined}>
                      <td>
                        <span className="cell-identity">
                          <Avatar name={ticket.contactName || ticket.phoneNumber} size="sm" />
                          <span className="cell-copy">
                            <b>{ticket.contactName || ticket.phoneNumber}</b>
                            <small>{ticket.phoneNumber}</small>
                          </span>
                        </span>
                      </td>
                      <td className="cell-mono">{ticket.number}</td>
                      <td className="cell-preview">{ticket.lastMessage || "No message preview"}</td>
                      <td className="num" title={formatListTimestamp(ticket.lastActivityAt)}>
                        {formatDuration(waiting)}
                      </td>
                      <td>
                        <StatusBadge status={ticket.status} unclaimed />
                      </td>
                      <td className="num">
                        <span className="row-actions">
                          <button type="button" className="button button-ghost" onClick={() => onOpen(ticket)}>
                            View
                          </button>
                          <button
                            type="button"
                            className="button button-primary"
                            disabled={busyId === ticket.id}
                            onClick={() => onClaim(ticket)}
                          >
                            {busyId === ticket.id ? "Claiming…" : "Claim"}
                          </button>
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}
