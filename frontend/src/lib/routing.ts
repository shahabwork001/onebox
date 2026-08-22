import type { View } from "./types";

/**
 * The workspace is one client component, but every screen still deserves an address: a refresh, a
 * bookmark and the browser back button should all land where the agent was, and a conversation
 * should be shareable with a colleague as a link.
 */
const VIEW_TO_SEGMENT: Record<View, string> = {
  dashboard: "dashboard",
  unassigned: "queue",
  mine: "inbox",
  all: "all",
  team: "team",
};

const SEGMENT_TO_VIEW: Record<string, View> = {
  dashboard: "dashboard",
  queue: "unassigned",
  inbox: "mine",
  all: "all",
  team: "team",
};

export type Route = { view: View; ticketId: string | null };

export const DEFAULT_ROUTE: Route = { view: "dashboard", ticketId: null };

/** Only the split views address a single conversation; dashboard and queue are screens of their own. */
const addressesConversation = (view: View) => view === "mine" || view === "all";

export function parseRoute(pathname: string): Route {
  const [segment, id] = pathname.split("/").filter(Boolean);
  const view = SEGMENT_TO_VIEW[segment ?? ""];
  if (!view) return DEFAULT_ROUTE;
  return { view, ticketId: addressesConversation(view) && id ? id : null };
}

export function buildPath({ view, ticketId }: Route) {
  const base = `/${VIEW_TO_SEGMENT[view]}`;
  return ticketId && addressesConversation(view) ? `${base}/${ticketId}` : base;
}
