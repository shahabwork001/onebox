import type { MessageStatus, TicketStatus } from "@/lib/types";
import { initialsOf } from "@/lib/types";

export function Avatar({ name, size = "md" }: { name: string; size?: "sm" | "md" | "lg" }) {
  return (
    <span className={`avatar avatar-${size}`} aria-hidden="true">
      {initialsOf(name)}
    </span>
  );
}

export function StatusBadge({ status, unclaimed }: { status: TicketStatus; unclaimed?: boolean }) {
  if (unclaimed) return <span className="badge badge-unclaimed">Unclaimed</span>;
  return <span className={`badge badge-${status.toLowerCase()}`}>{status}</span>;
}

/**
 * WhatsApp's own vocabulary: one tick sent, two delivered, two accented read. Failures are the only
 * state that earns colour, because it is the only one an agent has to act on.
 */
export function DeliveryTick({ status }: { status: MessageStatus }) {
  if (status === "Failed") return <span className="tick tick-failed">Failed</span>;
  if (status === "Queued") return <span className="tick">Sending…</span>;
  if (status === "Read") return <span className="tick tick-read">✓✓</span>;
  if (status === "Delivered") return <span className="tick">✓✓</span>;
  if (status === "Sent") return <span className="tick">✓</span>;
  return null;
}

export function OwnerChip({ name }: { name: string | null }) {
  if (!name) return null;
  return <span className="owner-chip">{name}</span>;
}

export function EmptyState({ icon, title, body }: { icon: string; title: string; body: string }) {
  return (
    <div className="empty-state">
      <div className="empty-state-icon" aria-hidden="true">
        {icon}
      </div>
      <h2>{title}</h2>
      <p>{body}</p>
    </div>
  );
}

export function Spinner({ label }: { label: string }) {
  return (
    <div className="spinner" role="status">
      <span className="spinner-dot" />
      <span>{label}</span>
    </div>
  );
}
