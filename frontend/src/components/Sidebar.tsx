import type { RealtimeState } from "@/hooks/useRealtime";
import type { Scope, User } from "@/lib/types";
import { Avatar } from "./Primitives";

const QUEUES: { key: Scope; label: string; hint: string }[] = [
  { key: "mine", label: "My inbox", hint: "Conversations assigned to you" },
  { key: "unassigned", label: "Unassigned", hint: "Waiting to be claimed" },
  { key: "all", label: "All conversations", hint: "Everything across the workspace" },
];

const REALTIME_LABEL: Record<RealtimeState, string> = {
  live: "Live",
  connecting: "Connecting…",
  offline: "Offline",
};

export function Sidebar({
  user,
  scope,
  counts,
  realtime,
  onScopeChange,
  onSignOut,
}: {
  user: User;
  scope: Scope;
  counts: Partial<Record<Scope, number>>;
  realtime: RealtimeState;
  onScopeChange: (scope: Scope) => void;
  onSignOut: () => void;
}) {
  return (
    <aside className="sidebar">
      <div className="brand">
        <span className="brand-mark" aria-hidden="true">
          C
        </span>
        <div className="brand-copy">
          <b>CentralChat</b>
          <small>WhatsApp workspace</small>
        </div>
      </div>

      <nav aria-label="Conversation queues">
        {QUEUES.map(queue => (
          <button
            key={queue.key}
            type="button"
            title={queue.hint}
            aria-current={scope === queue.key ? "page" : undefined}
            className={scope === queue.key ? "nav-item is-active" : "nav-item"}
            onClick={() => onScopeChange(queue.key)}
          >
            <span className="nav-label">{queue.label}</span>
            {counts[queue.key] !== undefined && <span className="nav-count">{counts[queue.key]}</span>}
          </button>
        ))}
      </nav>

      <div className={`realtime realtime-${realtime}`}>
        <span className="realtime-dot" aria-hidden="true" />
        {REALTIME_LABEL[realtime]}
      </div>

      <div className="profile">
        <Avatar name={user.displayName} size="sm" />
        <div className="profile-copy">
          <b>{user.displayName}</b>
          <small>{user.email}</small>
        </div>
        <button type="button" className="profile-signout" onClick={onSignOut} title="Sign out" aria-label="Sign out">
          ↗
        </button>
      </div>
    </aside>
  );
}
