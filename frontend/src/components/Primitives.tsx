import type { ReactNode } from "react";
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
  if (status === "Queued") return <span className="tick">Sending</span>;

  const doubled = status === "Read" || status === "Delivered";
  if (!doubled && status !== "Sent") return null;

  return (
    <span className={status === "Read" ? "tick tick-read" : "tick"} title={status}>
      <svg viewBox="0 0 20 12" width="16" height="10" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <path d="M2 6.5L5.5 10L12 2" />
        {doubled && <path d="M8.5 8.6L10 10l6.5-8" />}
      </svg>
    </span>
  );
}

export function OwnerChip({ name }: { name: string | null }) {
  if (!name) return null;
  return <span className="owner-chip">{name}</span>;
}

export function EmptyState({ icon, title, body }: { icon: ReactNode; title: string; body: string }) {
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
