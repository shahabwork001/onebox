"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";

const API = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";
type User = { id: string; email: string; displayName: string; permissions: string[] };
type Auth = { accessToken: string; refreshToken: string; expiresAt: string; user: User };
type TicketStatus = "New" | "Open" | "Pending" | "Resolved" | "Closed";
type Ticket = { id: string; number: string; status: TicketStatus; contactName: string; phoneNumber: string; conversationId: string; assignedAgentId?: string; lastActivityAt: string; lastMessage?: string };
type Message = { id: string; direction: "Inbound" | "Outbound" | 0 | 1; text?: string; status: string | number; timestamp: string };

const SCOPES: [string, string][] = [["mine", "My inbox"], ["unassigned", "Unassigned"], ["all", "All conversations"]];
const FILTERS: [string, string][] = [["active", "Active"], ["resolved", "Resolved"], ["closed", "Closed"], ["all", "All"]];
const REALTIME_EVENTS = ["message.received", "message.sent", "ticket.created", "ticket.claimed", "ticket.assignment.added", "ticket.assignment.removed", "ticket.status.changed"];
const isTerminal = (ticket: Ticket) => ticket.status === "Resolved" || ticket.status === "Closed";

async function request<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API}${path}`, { ...init, headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}`, ...init?.headers } });
  if (!response.ok) { const problem = await response.json().catch(() => null); throw new Error(problem?.detail ?? `Request failed (${response.status})`); }
  if (response.status === 204) return undefined as T;
  return response.json();
}

export default function Home() {
  const [auth, setAuth] = useState<Auth | null>(null);
  const [scope, setScope] = useState("mine");
  const [status, setStatus] = useState("active");
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [selected, setSelected] = useState<Ticket | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [composer, setComposer] = useState("");
  const [error, setError] = useState("");
  const canAssign = auth?.user.permissions.includes("tickets.assign");

  useEffect(() => { const saved = localStorage.getItem("centralchat.auth"); if (saved) setAuth(JSON.parse(saved)); }, []);
  const loadTickets = useCallback(async (): Promise<Ticket[]> => {
    if (!auth) return [];
    try { const data = await request<{ items: Ticket[] }>(`/api/tickets?scope=${scope}&status=${status}&pageSize=50`, auth.accessToken); setTickets(data.items); setSelected(current => current ? data.items.find(x => x.id === current.id) ?? current : null); return data.items; }
    catch (e) { setError((e as Error).message); return []; }
  }, [auth, scope, status]);
  useEffect(() => { loadTickets(); }, [loadTickets]);
  useEffect(() => { if (!auth || !selected) return; request<Message[]>(`/api/conversations/${selected.conversationId}/messages?limit=100`, auth.accessToken).then(x => setMessages(x.reverse())).catch(e => setError(e.message)); }, [auth, selected]);
  useEffect(() => {
    if (!auth) return;
    const connection = new signalR.HubConnectionBuilder().withUrl(`${API}/hubs/communication`, { accessTokenFactory: () => auth.accessToken }).withAutomaticReconnect().build();
    const refresh = () => { loadTickets(); };
    for (const event of REALTIME_EVENTS) connection.on(event, refresh);
    connection.start().catch(() => setError("Realtime connection unavailable; REST data remains available."));
    return () => { connection.stop(); };
  }, [auth, loadTickets]);

  if (!auth) return <Login onLogin={value => { localStorage.setItem("centralchat.auth", JSON.stringify(value)); setAuth(value); }} />;
  const owns = !!selected && (selected.assignedAgentId === auth.user.id || canAssign);
  const claim = async () => { if (!selected) return; try { await request(`/api/tickets/${selected.id}/claim`, auth.accessToken, { method: "POST" }); setScope("mine"); await loadTickets(); } catch (e) { setError((e as Error).message); } };
  const changeStatus = async (action: "resolve" | "close" | "reopen") => {
    if (!selected) return;
    try { await request(`/api/tickets/${selected.id}/${action}`, auth.accessToken, { method: "POST", body: JSON.stringify({ reason: null }) }); const items = await loadTickets(); setSelected(items.find(x => x.id === selected.id) ?? null); }
    catch (e) { setError((e as Error).message); }
  };
  const send = async (e: FormEvent) => { e.preventDefault(); if (!selected || !composer.trim()) return; try { const message = await request<Message>(`/api/conversations/${selected.conversationId}/messages`, auth.accessToken, { method: "POST", body: JSON.stringify({ text: composer }) }); setMessages(x => [...x, message]); setComposer(""); } catch (err) { setError((err as Error).message); } };
  const logout = () => { localStorage.removeItem("centralchat.auth"); setAuth(null); };

  return <main className="app-shell">
    <aside className="sidebar">
      <div className="brand"><span className="brand-mark">C</span><div><b>CentralChat</b><small>WhatsApp workspace</small></div></div>
      <nav>{SCOPES.map(([key, label]) => <button key={key} className={scope === key ? "active" : ""} onClick={() => { setScope(key); setSelected(null); }}>{label}</button>)}</nav>
      <div className="profile"><span>{auth.user.displayName.slice(0,2).toUpperCase()}</span><div><b>{auth.user.displayName}</b><small>{auth.user.email}</small></div><button onClick={logout} title="Sign out">↗</button></div>
    </aside>
    <section className="inbox-panel">
      <header><div><h1>{SCOPES.find(([key]) => key === scope)?.[1]}</h1><p>{tickets.length} conversations</p></div><button className="icon-button" onClick={loadTickets}>↻</button></header>
      <div className="search">⌕ <input placeholder="Search contacts" /></div>
      <div className="filters">{FILTERS.map(([key, label]) => <button key={key} className={status === key ? "chip active" : "chip"} onClick={() => { setStatus(key); setSelected(null); }}>{label}</button>)}</div>
      <div className="ticket-list">{tickets.map(ticket => <button key={ticket.id} className={selected?.id === ticket.id ? "ticket selected" : "ticket"} onClick={() => setSelected(ticket)}><span className="avatar">{ticket.contactName.slice(0,2).toUpperCase()}</span><span className="ticket-copy"><b>{ticket.contactName}</b><small>{ticket.lastMessage || "No message preview"}</small></span><span className="ticket-meta"><time>{new Date(ticket.lastActivityAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</time>{!ticket.assignedAgentId && !isTerminal(ticket) ? <i className="state unclaimed">Unclaimed</i> : <i className={`state ${ticket.status.toLowerCase()}`}>{ticket.status}</i>}</span></button>)}{tickets.length === 0 && <div className="empty">No conversations in this queue.</div>}</div>
    </section>
    <section className="conversation">
      {selected ? <><header><span className="avatar large">{selected.contactName.slice(0,2).toUpperCase()}</span><div><h2>{selected.contactName}</h2><p>{selected.phoneNumber} · {selected.number} · <i className={`state ${selected.status.toLowerCase()}`}>{selected.status}</i></p></div><div className="actions">{!selected.assignedAgentId && !isTerminal(selected) && <button className="claim" onClick={claim}>Claim conversation</button>}{owns && !isTerminal(selected) && <button className="ghost" onClick={() => changeStatus("resolve")}>Resolve</button>}{owns && selected.status !== "Closed" && <button className="ghost" onClick={() => changeStatus("close")}>Close</button>}{owns && isTerminal(selected) && <button className="claim" onClick={() => changeStatus("reopen")}>Reopen</button>}</div></header>
      <div className="messages">{messages.map(m => <div key={m.id} className={`bubble ${m.direction === "Outbound" || m.direction === 1 ? "outbound" : "inbound"}`}><p>{m.text || "Unsupported media message"}</p><small>{new Date(m.timestamp).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })} {m.direction === "Outbound" || m.direction === 1 ? `· ${m.status}` : ""}</small></div>)}</div>
      <form className="composer" onSubmit={send}><textarea value={composer} onChange={e => setComposer(e.target.value)} placeholder={isTerminal(selected) ? "Reopen this ticket to reply" : owns ? "Type a reply…" : "Claim this conversation to reply"} disabled={!owns || isTerminal(selected)} onKeyDown={e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); e.currentTarget.form?.requestSubmit(); } }} /><button disabled={!composer.trim()}>Send</button></form></> : <div className="conversation-empty"><div>◌</div><h2>Select a conversation</h2><p>Choose a contact from the inbox to view their history.</p></div>}
    </section>
    {error && <button className="toast" onClick={() => setError("")}>{error} ×</button>}
  </main>;
}

function Login({ onLogin }: { onLogin: (auth: Auth) => void }) {
  const [email, setEmail] = useState("agent1@example.local"); const [password, setPassword] = useState("CentralChat1!"); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const submit = async (e: FormEvent) => { e.preventDefault(); setBusy(true); try { const response = await fetch(`${API}/api/auth/login`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email, password }) }); if (!response.ok) throw new Error("Check your email and password."); onLogin(await response.json()); } catch (err) { setError((err as Error).message); } finally { setBusy(false); } };
  return <main className="login"><section><div className="brand login-brand"><span className="brand-mark">C</span><div><b>CentralChat</b><small>One inbox. Every customer.</small></div></div><h1>Welcome back</h1><p>Sign in to manage your WhatsApp conversations.</p><form onSubmit={submit}><label>Email<input type="email" value={email} onChange={e => setEmail(e.target.value)} required /></label><label>Password<input type="password" value={password} onChange={e => setPassword(e.target.value)} required /></label>{error && <div className="form-error">{error}</div>}<button disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button></form><small className="demo-hint">Development account is pre-filled.</small></section><aside><div className="login-art">✦</div><blockquote>Bring every customer conversation into one calm, focused workspace.</blockquote><p>Secure assignment · Real-time delivery · Complete history</p></aside></main>;
}
