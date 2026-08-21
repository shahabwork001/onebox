import type { ReactNode } from "react";
import type { RealtimeState } from "@/hooks/useRealtime";
import type { Scope, User } from "@/lib/types";
import { Avatar } from "./Primitives";

/** Icons carry the rail once labels are hidden on narrow screens, so every queue needs one. */
const Icon = ({ children }: { children: ReactNode }) => (
  <svg
    className="nav-icon"
    viewBox="0 0 24 24"
    width="19"
    height="19"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
  >
    {children}
  </svg>
);

const QUEUES: { key: Scope; label: string; hint: string; icon: ReactNode }[] = [
  {
    key: "mine",
    label: "My inbox",
    hint: "Conversations assigned to you",
    icon: (
      <Icon>
        <path d="M4 5h16l1 8v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-5z" />
        <path d="M3 13h4l2 3h6l2-3h4" />
      </Icon>
    ),
  },
  {
    key: "unassigned",
    label: "Unassigned",
    hint: "Waiting to be claimed",
    icon: (
      <Icon>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7v5l3 2" />
      </Icon>
    ),
  },
  {
    key: "all",
    label: "All conversations",
    hint: "Everything across the workspace",
    icon: (
      <Icon>
        <path d="M21 11.5a8 8 0 0 1-11.6 7.1L4 20l1.4-5.4A8 8 0 1 1 21 11.5z" />
      </Icon>
    ),
  },
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
            {queue.icon}
            <span className="nav-label">{queue.label}</span>
            {counts[queue.key] !== undefined && <span className="nav-count">{counts[queue.key]}</span>}
          </button>
        ))}
      </nav>

      <div className={`realtime realtime-${realtime}`} title={`Realtime connection: ${REALTIME_LABEL[realtime]}`}>
        <span className="realtime-dot" aria-hidden="true" />
        <span className="realtime-label">{REALTIME_LABEL[realtime]}</span>
      </div>

      <div className="profile">
        <Avatar name={user.displayName} size="sm" />
        <div className="profile-copy">
          <b>{user.displayName}</b>
          <small>{user.email}</small>
        </div>
        <button type="button" className="profile-signout" onClick={onSignOut} title="Sign out" aria-label="Sign out">
          <Icon>
            <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
            <path d="M16 17l5-5-5-5" />
            <path d="M21 12H9" />
          </Icon>
        </button>
      </div>
    </aside>
  );
}
