import type { ReactNode } from "react";
import { IconChat, IconClock, IconDashboard, IconInbox, IconSignOut, IconTeam } from "./icons";
import type { RealtimeState } from "@/hooks/useRealtime";
import type { User, View } from "@/lib/types";
import { Avatar } from "./Primitives";


const QUEUES: { key: View; label: string; hint: string; icon: ReactNode }[] = [
  {
    key: "dashboard",
    label: "Dashboard",
    hint: "Workspace metrics at a glance",
    icon: <IconDashboard className="nav-icon" />,
  },
  {
    key: "mine",
    label: "My inbox",
    hint: "Conversations assigned to you",
    icon: <IconInbox className="nav-icon" />,
  },
  {
    key: "unassigned",
    label: "Unassigned",
    hint: "Waiting to be claimed",
    icon: <IconClock className="nav-icon" />,
  },
  {
    key: "all",
    label: "All conversations",
    hint: "Everything across the workspace",
    icon: <IconChat className="nav-icon" />,
  },
];

const TEAM_QUEUE = {
  key: "team" as View,
  label: "Team",
  hint: "Add and manage agent accounts",
  icon: <IconTeam className="nav-icon" />,
};

const REALTIME_LABEL: Record<RealtimeState, string> = {
  live: "Live",
  connecting: "Connecting…",
  offline: "Offline",
};

export function Sidebar({
  user,
  view,
  counts,
  canManageUsers,
  realtime,
  onViewChange,
  onSignOut,
}: {
  user: User;
  view: View;
  counts: Partial<Record<View, number>>;
  canManageUsers: boolean;
  realtime: RealtimeState;
  onViewChange: (view: View) => void;
  onSignOut: () => void;
}) {
  return (
    <aside className="sidebar">
      <div className="brand">
        <span className="brand-mark" aria-hidden="true">
          O
        </span>
        <div className="brand-copy">
          <b>Onebox</b>
          <small>WhatsApp workspace</small>
        </div>
      </div>

      <nav aria-label="Conversation queues">
        {[...QUEUES, ...(canManageUsers ? [TEAM_QUEUE] : [])].map(queue => (
          <button
            key={queue.key}
            type="button"
            title={queue.hint}
            aria-current={view === queue.key ? "page" : undefined}
            className={view === queue.key ? "nav-item is-active" : "nav-item"}
            onClick={() => onViewChange(queue.key)}
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
          <IconSignOut size={17} />
        </button>
      </div>
    </aside>
  );
}
