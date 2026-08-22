export type TicketStatus = "New" | "Open" | "Pending" | "Resolved" | "Closed";
export type MessageDirection = "Inbound" | "Outbound";
export type MessageStatus = "Queued" | "Sent" | "Delivered" | "Read" | "Failed" | "Received";

export type User = {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  permissions: string[];
};

export type Auth = {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: User;
};

export type Ticket = {
  id: string;
  number: string;
  status: TicketStatus;
  priority: string;
  contactId: string;
  contactName: string;
  phoneNumber: string;
  conversationId: string;
  assignedAgentId: string | null;
  lastActivityAt: string;
  lastMessage: string | null;
};

export type Message = {
  id: string;
  conversationId: string;
  direction: MessageDirection;
  type: string;
  text: string | null;
  status: MessageStatus;
  timestamp: string;
  externalMessageId: string | null;
  mimeType: string | null;
  /** False while the provider binary is still being fetched by the queued download job. */
  mediaReady: boolean;
  mediaSizeBytes: number | null;
};

/**
 * A message the agent has sent but the server has not yet acknowledged. It renders immediately with a
 * pending tick so the composer never feels like it swallowed the text, and is replaced by the stored
 * message on confirmation or marked failed if the send is rejected.
 */
export const PENDING_PREFIX = "pending-";

export const isPending = (message: Pick<Message, "id">) => message.id.startsWith(PENDING_PREFIX);

export function draftMessage(conversationId: string, text: string): Message {
  return {
    id: `${PENDING_PREFIX}${Date.now()}-${Math.random().toString(36).slice(2)}`,
    conversationId,
    direction: "Outbound",
    type: "Text",
    text,
    status: "Queued",
    timestamp: new Date().toISOString(),
    externalMessageId: null,
    mimeType: null,
    mediaReady: false,
    mediaSizeBytes: null,
  };
}

export const hasAttachment = (message: Pick<Message, "type">) =>
  message.type !== "Text" && message.type !== "Unknown";

export const ROLES = ["SuperAdmin", "Admin", "TeamLead", "Agent"] as const;

export type Agent = {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  roles: string[];
};

export type Contact = {
  id: string;
  displayName: string;
  phoneNumber: string;
  whatsAppUserId: string;
  assignedAgentId: string | null;
  lastMessageAt: string | null;
  status: string;
};

/** Inbox queues in the left rail. `all` is the monitoring view for admins and team leads. */
export type Scope = "mine" | "unassigned" | "all";

/** What the rail is showing. Dashboard and the unassigned queue are full-width screens of their own. */
export type View = "dashboard" | "team" | Scope;

/** Screens that own the full content area rather than splitting into list plus thread. */
export const isFullWidthView = (view: View) => view === "dashboard" || view === "unassigned" || view === "team";

export type DashboardTotals = {
  contacts: number;
  conversations: number;
  tickets: number;
  unassigned: number;
  open: number;
  resolved: number;
  closed: number;
  inboundMessages: number;
  outboundMessages: number;
  avgFirstResponseSeconds: number | null;
};

export type AgentWorkload = {
  agentId: string;
  displayName: string;
  email: string;
  claimed: number;
  open: number;
  resolved: number;
  avgFirstResponseSeconds: number | null;
};

export type Dashboard = { totals: DashboardTotals; agents: AgentWorkload[] };

/** Status filter chips above the conversation list. */
export type StatusFilter = "active" | "new" | "open" | "pending" | "resolved" | "closed" | "all";

export const TERMINAL_STATUSES: TicketStatus[] = ["Resolved", "Closed"];

export const isTerminal = (ticket: Pick<Ticket, "status">) => TERMINAL_STATUSES.includes(ticket.status);

export const isUnclaimed = (ticket: Pick<Ticket, "status" | "assignedAgentId">) =>
  !ticket.assignedAgentId && !isTerminal(ticket);

export const PERMISSIONS = {
  ticketsAssign: "tickets.assign",
  ticketsClaim: "tickets.claim",
  ticketsResolve: "tickets.resolve",
  usersManage: "users.manage",
  messagesSend: "messages.send",
} as const;

export const initialsOf = (value: string) => {
  const cleaned = value.trim();
  if (!cleaned) return "?";
  const words = cleaned.split(/\s+/).filter(Boolean);
  if (words.length >= 2) return (words[0][0] + words[1][0]).toUpperCase();
  return Array.from(cleaned).slice(0, 2).join("").toUpperCase();
};
