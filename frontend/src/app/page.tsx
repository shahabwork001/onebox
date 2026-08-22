"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiError, api, refreshSession } from "@/lib/api";
import { clearSession, readSession, writeSession } from "@/lib/session";
import {
  PERMISSIONS,
  isFullWidthView,
  type Agent,
  type Auth,
  type Dashboard,
  type Message,
  type Scope,
  type StatusFilter,
  type Ticket,
  type View,
} from "@/lib/types";
import { useRealtime } from "@/hooks/useRealtime";
import { LoginView } from "@/components/LoginView";
import { Sidebar } from "@/components/Sidebar";
import { InboxPanel } from "@/components/InboxPanel";
import { ConversationPanel } from "@/components/ConversationPanel";
import { DashboardView } from "@/components/DashboardView";
import { QueueTable } from "@/components/QueueTable";

export default function Workspace() {
  const [auth, setAuth] = useState<Auth | null>(null);
  const [ready, setReady] = useState(false);

  const [view, setView] = useState<View>("dashboard");
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [dashboardLoading, setDashboardLoading] = useState(false);
  const [claimingId, setClaimingId] = useState<string | null>(null);
  const [status, setStatus] = useState<StatusFilter>("active");
  const [search, setSearch] = useState("");

  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [unassignedCount, setUnassignedCount] = useState<number | undefined>();
  const [ticketsLoading, setTicketsLoading] = useState(false);

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [messagesLoading, setMessagesLoading] = useState(false);
  const [forbidden, setForbidden] = useState(false);

  const [agents, setAgents] = useState<Agent[]>([]);
  const [composer, setComposer] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  // The dashboard has no ticket list of its own; it borrows "mine" so the rail counts stay warm.
  const scope: Scope = view === "dashboard" ? "mine" : view;
  const token = auth?.accessToken;
  const canAssign = !!auth?.user.permissions.includes(PERMISSIONS.ticketsAssign);

  useEffect(() => {
    setAuth(readSession());
    setReady(true);
  }, []);

  const signOut = useCallback(() => {
    clearSession();
    setAuth(null);
    setTickets([]);
    setDashboard(null);
    setMessages([]);
    setSelectedId(null);
  }, []);

  /**
   * Access tokens live 30 minutes. Renewing a minute early keeps an agent who sits in the inbox all
   * day from being thrown back to the login screen mid-conversation; each renewal reschedules the next.
   */
  useEffect(() => {
    if (!auth) return;
    const renewIn = Math.max(new Date(auth.expiresAt).getTime() - Date.now() - 60_000, 5_000);
    const timer = setTimeout(async () => {
      try {
        const renewed = await refreshSession(auth.refreshToken);
        writeSession(renewed);
        setAuth(renewed);
      } catch {
        signOut();
      }
    }, renewIn);
    return () => clearTimeout(timer);
  }, [auth, signOut]);

  /** Any 401 means the stored token is spent; drop straight to the login screen rather than looping. */
  const report = useCallback(
    (cause: unknown) => {
      if (cause instanceof ApiError && cause.isUnauthorized) {
        signOut();
        return;
      }
      setError((cause as Error).message);
    },
    [signOut],
  );

  const loadTickets = useCallback(async () => {
    if (!token) return;
    setTicketsLoading(true);
    try {
      const [current, unassigned] = await Promise.all([
        api.tickets(token, scope, status),
        api.tickets(token, "unassigned", "active"),
      ]);
      setTickets(current.items);
      setUnassignedCount(unassigned.total);
    } catch (cause) {
      report(cause);
    } finally {
      setTicketsLoading(false);
    }
  }, [token, scope, status, report]);

  useEffect(() => {
    loadTickets();
  }, [loadTickets]);

  useEffect(() => {
    if (!token || !canAssign) return;
    api.agents(token).then(setAgents).catch(() => setAgents([]));
  }, [token, canAssign]);

  const loadDashboard = useCallback(async () => {
    if (!token) return;
    setDashboardLoading(true);
    try {
      setDashboard(await api.dashboard(token));
    } catch (cause) {
      report(cause);
    } finally {
      setDashboardLoading(false);
    }
  }, [token, report]);

  useEffect(() => {
    if (view === "dashboard") loadDashboard();
  }, [view, loadDashboard]);

  const selected = useMemo(() => tickets.find(ticket => ticket.id === selectedId) ?? null, [tickets, selectedId]);
  const conversationId = selected?.conversationId;

  const loadMessages = useCallback(async () => {
    if (!token || !conversationId) {
      setMessages([]);
      return;
    }
    setMessagesLoading(true);
    setForbidden(false);
    try {
      const history = await api.messages(token, conversationId);
      // The API returns newest-first for cursor paging; the transcript reads oldest-first.
      setMessages([...history].reverse());
    } catch (cause) {
      if (cause instanceof ApiError && cause.isForbidden) {
        setMessages([]);
        setForbidden(true);
      } else {
        report(cause);
      }
    } finally {
      setMessagesLoading(false);
    }
  }, [token, conversationId, report]);

  useEffect(() => {
    loadMessages();
  }, [loadMessages]);

  const realtime = useRealtime(token, () => {
    loadTickets();
    loadMessages();
    if (view === "dashboard") loadDashboard();
  });

  const run = useCallback(
    async (action: () => Promise<void>, nextScope?: Scope) => {
      setBusy(true);
      try {
        await action();
        if (nextScope) setView(nextScope);
        await loadTickets();
        await loadMessages();
      } catch (cause) {
        report(cause);
      } finally {
        setBusy(false);
      }
    },
    [loadTickets, loadMessages, report],
  );

  const agentNameOf = useCallback(
    (agentId: string | null) => {
      if (!agentId) return null;
      if (agentId === auth?.user.id) return "You";
      return agents.find(agent => agent.id === agentId)?.displayName ?? "Another agent";
    },
    [agents, auth?.user.id],
  );

  const visibleTickets = useMemo(() => {
    const needle = search.trim().toLowerCase();
    if (!needle) return tickets;
    return tickets.filter(ticket =>
      [ticket.contactName, ticket.phoneNumber, ticket.number, ticket.lastMessage]
        .filter(Boolean)
        .some(field => field!.toLowerCase().includes(needle)),
    );
  }, [tickets, search]);

  if (!ready) return null;

  if (!auth) {
    return (
      <LoginView
        onSignedIn={value => {
          writeSession(value);
          setAuth(value);
        }}
      />
    );
  }

  const isOwner = !!selected && selected.assignedAgentId === auth.user.id;

  const claimFromQueue = async (ticket: Ticket) => {
    setClaimingId(ticket.id);
    try {
      await api.claim(auth.accessToken, ticket.id);
      // Taking a conversation means answering it, so land the agent in it rather than back on a list.
      setView("mine");
      setSelectedId(ticket.id);
      await loadTickets();
    } catch (cause) {
      report(cause);
      await loadTickets();
    } finally {
      setClaimingId(null);
    }
  };

  const fullWidth = isFullWidthView(view);

  return (
    <main
      className={`workspace ${fullWidth ? "view-full" : "view-split"}${selected && !fullWidth ? " has-selection" : ""}`}
    >
      <Sidebar
        user={auth.user}
        view={view}
        counts={{ unassigned: unassignedCount }}
        realtime={realtime}
        onViewChange={next => {
          setView(next);
          setSelectedId(null);
        }}
        onSignOut={signOut}
      />

      {view === "dashboard" ? (
        <DashboardView
          data={dashboard}
          loading={dashboardLoading}
          displayName={auth.user.displayName}
          canSeeAgents={canAssign}
          onOpenQueue={() => setView("unassigned")}
          onRefresh={loadDashboard}
        />
      ) : view === "unassigned" ? (
        <QueueTable
          tickets={visibleTickets}
          loading={ticketsLoading}
          busyId={claimingId}
          onClaim={claimFromQueue}
          onOpen={ticket => {
            setView("all");
            setSelectedId(ticket.id);
          }}
          onRefresh={loadTickets}
        />
      ) : (
        <>
          <InboxPanel
            scope={scope}
            status={status}
            tickets={visibleTickets}
            search={search}
            loading={ticketsLoading}
            selectedId={selectedId}
            agentNameOf={agentNameOf}
            onStatusChange={next => {
              setStatus(next);
              setSelectedId(null);
            }}
            onSearchChange={setSearch}
            onSelect={ticket => setSelectedId(ticket.id)}
            onRefresh={loadTickets}
          />

          <ConversationPanel
            ticket={selected}
            messages={messages}
            agents={agents}
            ownerName={agentNameOf(selected?.assignedAgentId ?? null)}
            loading={messagesLoading}
            forbidden={forbidden}
            busy={busy}
            canAssign={canAssign}
            isOwner={isOwner}
            composer={composer}
            onComposerChange={setComposer}
            onBack={() => setSelectedId(null)}
            actions={{
              onClaim: () => selected && run(() => api.claim(auth.accessToken, selected.id), "mine"),
              onRelease: () => selected && run(() => api.release(auth.accessToken, selected.id), "unassigned"),
              onAssign: agentId => selected && run(() => api.assign(auth.accessToken, selected.id, agentId)),
              onChangeStatus: action => selected && run(() => api.changeStatus(auth.accessToken, selected.id, action)),
              onSend: text =>
                selected &&
                run(async () => {
                  await api.sendMessage(auth.accessToken, selected.conversationId, text);
                  setComposer("");
                }),
            }}
          />
        </>
      )}

      {error && (
        <button type="button" className="toast" onClick={() => setError("")} role="alert">
          {error} <span aria-hidden="true">×</span>
        </button>
      )}
    </main>
  );
}
