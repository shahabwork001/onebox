import type { Agent, Auth, Contact, Dashboard, Message, Scope, StatusFilter, Ticket } from "./types";

/**
 * Empty in production: the reverse proxy serves the app and the API from one origin and forwards
 * `/api/*` and `/hubs/*` to the API without stripping the prefix. Local development points at the
 * API's own origin instead. Paths below therefore always carry their own `/api` prefix.
 */
export const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
    this.name = "ApiError";
  }

  get isForbidden() {
    return this.status === 403;
  }

  get isUnauthorized() {
    return this.status === 401;
  }
}

type PagedResult<T> = { items: T[]; page: number; pageSize: number; total: number };

async function send<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}`, ...init?.headers },
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(response.status, problem?.detail ?? problem?.title ?? `Request failed (${response.status})`);
  }

  if (response.status === 204) return undefined as T;
  return response.json();
}

export async function login(email: string, password: string): Promise<Auth> {
  const response = await fetch(`${API_BASE}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password }),
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(response.status, problem?.detail ?? "Check your email and password.");
  }

  return response.json();
}

export async function refreshSession(refreshToken: string): Promise<Auth> {
  const response = await fetch(`${API_BASE}/api/auth/refresh`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }),
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(response.status, problem?.detail ?? "Session could not be renewed.");
  }

  return response.json();
}

export const api = {
  tickets: (token: string, scope: Scope, status: StatusFilter) =>
    send<PagedResult<Ticket>>(`/api/tickets?scope=${scope}&status=${status}&pageSize=100`, token),

  messages: (token: string, conversationId: string) =>
    send<Message[]>(`/api/conversations/${conversationId}/messages?limit=100`, token),

  /** Multipart, so the browser sets its own boundary: never set Content-Type by hand here. */
  sendAttachment: async (token: string, conversationId: string, file: File, caption: string) => {
    const form = new FormData();
    form.append("file", file);
    if (caption.trim()) form.append("caption", caption.trim());

    const response = await fetch(`${API_BASE}/api/conversations/${conversationId}/attachments`, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
      body: form,
    });

    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new ApiError(response.status, problem?.detail ?? `Upload failed (${response.status})`);
    }
    return (await response.json()) as Message;
  },

  sendMessage: (token: string, conversationId: string, text: string) =>
    send<Message>(`/api/conversations/${conversationId}/messages`, token, {
      method: "POST",
      body: JSON.stringify({ text }),
    }),

  agents: (token: string, includeInactive = false) =>
    send<Agent[]>(`/api/users?includeInactive=${includeInactive}`, token),

  createUser: (token: string, body: { email: string; displayName: string; password: string; role: string }) =>
    send<Agent>("/api/users", token, { method: "POST", body: JSON.stringify(body) }),

  updateUser: (token: string, id: string, body: { displayName?: string; role?: string; isActive?: boolean }) =>
    send<Agent>(`/api/users/${id}`, token, { method: "PATCH", body: JSON.stringify(body) }),

  setUserPassword: (token: string, id: string, password: string) =>
    send<void>(`/api/users/${id}/password`, token, { method: "POST", body: JSON.stringify({ password }) }),

  changeOwnPassword: (token: string, currentPassword: string, newPassword: string) =>
    send<void>("/api/auth/password", token, {
      method: "POST",
      body: JSON.stringify({ currentPassword, newPassword }),
    }),

  dashboard: (token: string) => send<Dashboard>("/api/dashboard", token),

  contacts: (token: string) => send<PagedResult<Contact>>("/api/contacts?pageSize=100", token),

  claim: (token: string, ticketId: string) =>
    send<void>(`/api/tickets/${ticketId}/claim`, token, { method: "POST" }),

  /** Releases a ticket back to the unassigned queue. Agents may release their own; assigners, anyone's. */
  release: (token: string, ticketId: string, reason?: string) =>
    send<void>(`/api/tickets/${ticketId}/unassign`, token, {
      method: "POST",
      body: JSON.stringify({ reason: reason ?? null }),
    }),

  assign: (token: string, ticketId: string, agentId: string, reason?: string) =>
    send<void>(`/api/tickets/${ticketId}/assign`, token, {
      method: "POST",
      body: JSON.stringify({ agentId, reason: reason ?? null }),
    }),

  changeStatus: (token: string, ticketId: string, action: "resolve" | "close" | "reopen", reason?: string) =>
    send<void>(`/api/tickets/${ticketId}/${action}`, token, {
      method: "POST",
      body: JSON.stringify({ reason: reason ?? null }),
    }),
};
