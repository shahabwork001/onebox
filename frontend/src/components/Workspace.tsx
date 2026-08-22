"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ApiError, api, refreshSession } from "@/lib/api";
import { clearSession, readSession, writeSession } from "@/lib/session";
import { DEFAULT_ROUTE, buildPath, parseRoute } from "@/lib/routing";
import {
  PERMISSIONS,
  draftMessage,
  isFullWidthView,
  isPending,
  matchesView,
  type Agent,
  type Auth,
  type Dashboard,
  type Message,
  type Scope,
  type StatusFilter,
  type Ticket,
  type View,
} from "@/lib/types";
import { useRealtime, type RealtimeSignal } from "@/hooks/useRealtime";
import { LoginView } from "./LoginView";
import { Sidebar } from "./Sidebar";
import { InboxPanel } from "./InboxPanel";
import { ConversationPanel } from "./ConversationPanel";
import { DashboardView } from "./DashboardView";
import { QueueTable } from "./QueueTable";
import { TeamView } from "./TeamView";

/** Ceiling on reconciliation: at a hundred agents this is the difference between 5 and 1500 queries a second. */
const RECONCILE_INTERVAL_MS = 15_000;
const DASHBOARD_REFRESH_MS = 30_000;

export function Workspace() {
  const [auth, setAuth] = useState<Auth | null>(null);
  const [ready, setReady] = useState(false);

  const [view, setView] = useState<View>("dashboard");
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [dashboardLoading, setDashboardLoading] = useState(false);
  const [claimingId, setClaimingId] = useState<string | null>(null);
  const [team, setTeam] = useState<Agent[]>([]);
  const [teamLoading, setTeamLoading] = useState(false);
  const [teamBusyId, setTeamBusyId] = useState<string | null>(null);
  const [status, setStatus] = useState<StatusFilter>("active");
  const [search, setSearch] = useState("");
  // Typed text is applied on a delay: a request per keystroke would be one per agent per keystroke.
  const [appliedSearch, setAppliedSearch] = useState("");
  const [ticketTotal, setTicketTotal] = useState(0);
  const [ticketPage, setTicketPage] = useState(1);
  const [loadingMore, setLoadingMore] = useState(false);

  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [unassignedCount, setUnassignedCount] = useState<number | undefined>();
  const [ticketsLoading, setTicketsLoading] = useState(false);

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [messagesLoading, setMessagesLoading] = useState(false);
  const [forbidden, setForbidden] = useState(false);
  const loadedConversation = useRef<string | null>(null);

  const [agents, setAgents] = useState<Agent[]>([]);
  const [composer, setComposer] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  // The dashboard has no ticket list of its own; it borrows "mine" so the rail counts stay warm.
  const scope: Scope = view === "dashboard" || view === "team" ? "mine" : view;
  const token = auth?.accessToken;
  const canAssign = !!auth?.user.permissions.includes(PERMISSIONS.ticketsAssign);
  const canManageUsers = !!auth?.user.permissions.includes(PERMISSIONS.usersManage);

  useEffect(() => {
    const applyUrl = () => {
      const route = parseRoute(window.location.pathname);
      setView(route.view);
      setSelectedId(route.ticketId);
    };
    applyUrl();
    setAuth(readSession());
    setReady(true);

    // Back and forward should move between screens, not out of the application.
    window.addEventListener("popstate", applyUrl);
    return () => window.removeEventListener("popstate", applyUrl);
  }, []);

  useEffect(() => {
    if (!ready) return;
    const path = buildPath(auth ? { view, ticketId: selectedId } : DEFAULT_ROUTE);
    if (window.location.pathname !== path) window.history.pushState(null, "", path);
  }, [ready, auth, view, selectedId]);

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
        api.tickets(token, scope, status, appliedSearch),
        api.tickets(token, "unassigned", "active"),
      ]);
      setTickets(current.items);
      setTicketTotal(current.total);
      setTicketPage(1);
      setUnassignedCount(unassigned.total);
    } catch (cause) {
      report(cause);
    } finally {
      setTicketsLoading(false);
    }
  }, [token, scope, status, appliedSearch, report]);

  /** Appends the next page rather than replacing, so the agent keeps their place in the list. */
  const loadMoreTickets = useCallback(async () => {
    if (!token || loadingMore) return;
    setLoadingMore(true);
    try {
      const next = await api.tickets(token, scope, status, appliedSearch, ticketPage + 1);
      setTickets(current => {
        const seen = new Set(current.map(ticket => ticket.id));
        return [...current, ...next.items.filter(ticket => !seen.has(ticket.id))];
      });
      setTicketTotal(next.total);
      setTicketPage(page => page + 1);
    } catch (cause) {
      report(cause);
    } finally {
      setLoadingMore(false);
    }
  }, [token, scope, status, appliedSearch, ticketPage, loadingMore, report]);

  useEffect(() => {
    const timer = setTimeout(() => setAppliedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    loadTickets();
  }, [loadTickets]);

  useEffect(() => {
    if (!token || !canAssign) return;
    api.agents(token).then(setAgents).catch(() => setAgents([]));
  }, [token, canAssign]);

  // The team screen is the only place deactivated accounts should appear; assignment must not offer them.
  const loadTeam = useCallback(async () => {
    if (!token || !canManageUsers) return;
    setTeamLoading(true);
    try {
      setTeam(await api.agents(token, true));
    } catch (cause) {
      report(cause);
    } finally {
      setTeamLoading(false);
    }
  }, [token, canManageUsers, report]);

  useEffect(() => {
    if (view === "team") loadTeam();
  }, [view, loadTeam]);

  const runTeamAction = useCallback(
    async (id: string | null, action: () => Promise<void>) => {
      setTeamBusyId(id);
      try {
        await action();
        await loadTeam();
        if (token && canAssign) api.agents(token).then(setAgents).catch(() => undefined);
      } catch (cause) {
        report(cause);
      } finally {
        setTeamBusyId(null);
      }
    },
    [loadTeam, report, token, canAssign],
  );

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
    // A spinner on every refresh made sending look like the chat reloaded. Only a conversation the
    // agent has not opened yet may blank; anything else updates in place.
    const firstOpen = loadedConversation.current !== conversationId;
    if (firstOpen) setMessagesLoading(true);
    setForbidden(false);
    try {
      const history = await api.messages(token, conversationId);
      // The API returns newest-first for cursor paging; the transcript reads oldest-first. Anything
      // still awaiting acknowledgement is kept, so a refresh cannot make a sent message vanish.
      const stored = [...history].reverse();
      setMessages(current => [...stored, ...current.filter(isPending)]);
      loadedConversation.current = conversationId;
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

  /**
   * Events carry the changed entity, so the common path costs no requests at all: an arriving message
   * updates the open transcript and the list row it belongs to. Reloading is kept for reconciliation,
   * throttled hard, because the client decides locally whether a ticket belongs in the current view
   * and that judgement can drift from the server's.
   */
  const lastReconcile = useRef(0);
  const reconcileTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const reconcile = useCallback(
    (immediate = false) => {
      const since = Date.now() - lastReconcile.current;
      const run = () => {
        lastReconcile.current = Date.now();
        loadTickets();
        loadMessages();
      };

      if (immediate || since > RECONCILE_INTERVAL_MS) {
        if (reconcileTimer.current) clearTimeout(reconcileTimer.current);
        reconcileTimer.current = null;
        run();
        return;
      }
      if (reconcileTimer.current) return;
      reconcileTimer.current = setTimeout(() => {
        reconcileTimer.current = null;
        run();
      }, RECONCILE_INTERVAL_MS - since);
    },
    [loadTickets, loadMessages],
  );

  const applyTicket = useCallback(
    (incoming: Ticket) => {
      setTickets(current => {
        const without = current.filter(ticket => ticket.id !== incoming.id);
        // A live update must not jump into a filtered list it may not belong to.
        if (!auth || appliedSearch.trim() || !matchesView(incoming, scope, status, auth.user.id)) return without;
        return [incoming, ...without].sort(
          (a, b) => new Date(b.lastActivityAt).getTime() - new Date(a.lastActivityAt).getTime(),
        );
      });
    },
    [auth, scope, status, appliedSearch],
  );

  const onRealtime = useCallback(
    (event: RealtimeSignal, payload: unknown) => {
      if (event === "resync") {
        reconcile(true);
        return;
      }

      if (event === "ticket.upserted") {
        const ticket = payload as Ticket;
        applyTicket(ticket);
        // The queue badge is a count the payload cannot supply, so let it settle on the next pass.
        reconcile();
        return;
      }

      if (event === "ticket.removed") {
        const { ticketId } = (payload ?? {}) as { ticketId?: string };
        if (ticketId) setTickets(current => current.filter(ticket => ticket.id !== ticketId));
        return;
      }

      // Message events are scoped to a conversation group, so they only arrive for threads worth
      // updating; anything for a thread that is not open needs nothing.
      const message = payload as Message | null;
      if (!message?.conversationId || message.conversationId !== conversationId) return;

      setMessages(current => {
        const existing = current.findIndex(item => item.id === message.id);
        if (existing >= 0) {
          const next = [...current];
          next[existing] = message;
          return next;
        }
        return [...current, message];
      });
    },
    [applyTicket, conversationId, reconcile],
  );

  const realtime = useRealtime(token, onRealtime);

  // A tab left open in the background can miss events; catch up when it comes back into use.
  useEffect(() => {
    const onFocus = () => reconcile(true);
    window.addEventListener("focus", onFocus);
    return () => window.removeEventListener("focus", onFocus);
  }, [reconcile]);

  useEffect(() => {
    if (view !== "dashboard") return;
    const timer = setInterval(loadDashboard, DASHBOARD_REFRESH_MS);
    return () => clearInterval(timer);
  }, [view, loadDashboard]);

  /**
   * The draft is shown before the request leaves, which is what makes the composer feel immediate,
   * and the stored message replaces it on confirmation. A rejected send leaves the message visible
   * and marked failed rather than silently dropping what the agent wrote.
   */
  /**
   * Uploads take long enough to notice, so the attachment is shown as pending from the moment it is
   * chosen and settled when the server returns the stored message, exactly as a text reply behaves.
   */
  const sendAttachment = useCallback(
    async (file: File, caption: string) => {
      if (!auth || !selected) return;
      const conversationId = selected.conversationId;
      const draft: Message = {
        ...draftMessage(conversationId, caption),
        type: file.type.startsWith("image/")
          ? "Image"
          : file.type.startsWith("video/")
            ? "Video"
            : file.type.startsWith("audio/")
              ? "Audio"
              : "Document",
        mimeType: file.type,
        mediaSizeBytes: file.size,
      };

      setComposer("");
      setMessages(current => [...current, draft]);

      try {
        const sent = await api.sendAttachment(auth.accessToken, conversationId, file, caption);
        setMessages(current => {
          const settled = current.filter(message => message.id !== draft.id);
          return settled.some(message => message.id === sent.id) ? settled : [...settled, sent];
        });
        loadTickets();
      } catch (cause) {
        setMessages(current =>
          current.map(message => (message.id === draft.id ? { ...message, status: "Failed" } : message)),
        );
        report(cause);
      }
    },
    [auth, selected, loadTickets, report],
  );

  const sendMessage = useCallback(
    async (text: string) => {
      if (!auth || !selected) return;
      const conversationId = selected.conversationId;
      const draft = draftMessage(conversationId, text);

      setComposer("");
      setMessages(current => [...current, draft]);

      try {
        const sent = await api.sendMessage(auth.accessToken, conversationId, text);
        setMessages(current => {
          const settled = current.filter(message => message.id !== draft.id);
          return settled.some(message => message.id === sent.id) ? settled : [...settled, sent];
        });
        // Only the list preview depends on this, so it must not hold up the transcript.
        loadTickets();
      } catch (cause) {
        setMessages(current =>
          current.map(message => (message.id === draft.id ? { ...message, status: "Failed" } : message)),
        );
        report(cause);
      }
    },
    [auth, selected, loadTickets, report],
  );

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

  // The server applies the filter now, so what arrives is already the answer.
  const visibleTickets = tickets;

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
        canManageUsers={canManageUsers}
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
      ) : view === "team" ? (
        <TeamView
          agents={team}
          loading={teamLoading}
          busyId={teamBusyId}
          currentUserId={auth.user.id}
          onRefresh={loadTeam}
          actions={{
            onCreate: input => runTeamAction(null, () => api.createUser(auth.accessToken, input).then(() => undefined)),
            onChangeRole: (agent, role) =>
              runTeamAction(agent.id, () => api.updateUser(auth.accessToken, agent.id, { role }).then(() => undefined)),
            onSetActive: (agent, isActive) =>
              runTeamAction(agent.id, () => api.updateUser(auth.accessToken, agent.id, { isActive }).then(() => undefined)),
            onResetPassword: (agent, password) =>
              runTeamAction(agent.id, () => api.setUserPassword(auth.accessToken, agent.id, password)),
            onChangeOwnPassword: (currentPassword, newPassword) =>
              runTeamAction(null, () => api.changeOwnPassword(auth.accessToken, currentPassword, newPassword)),
          }}
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
            total={ticketTotal}
            loading={ticketsLoading}
            loadingMore={loadingMore}
            onLoadMore={loadMoreTickets}
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
            token={auth.accessToken}
            onComposerChange={setComposer}
            onBack={() => setSelectedId(null)}
            actions={{
              onClaim: () => selected && run(() => api.claim(auth.accessToken, selected.id), "mine"),
              onRelease: () => selected && run(() => api.release(auth.accessToken, selected.id), "unassigned"),
              onAssign: agentId => selected && run(() => api.assign(auth.accessToken, selected.id, agentId)),
              onChangeStatus: action => selected && run(() => api.changeStatus(auth.accessToken, selected.id, action)),
              onSend: sendMessage,
              onAttach: sendAttachment,
            }}
          />
        </>
      )}

      {error && (
        <button type="button" className="toast" onClick={() => setError("")} role="alert">
          <span>{error}</span>
        </button>
      )}
    </main>
  );
}
