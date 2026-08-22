"use client";

import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { API_BASE } from "@/lib/api";

/**
 * Hub events carry the changed entity, so a client updates what it already holds instead of reloading
 * every list on every event. That distinction is what makes a hundred connected agents affordable:
 * one arriving message used to cost one reload per agent.
 *
 * A periodic reconciliation still runs, because the client decides locally whether a changed ticket
 * belongs in the current view and that judgement can drift from the server's.
 */
const EVENTS = [
  "ticket.upserted",
  "ticket.removed",
  "ticket.status.changed",
  "message.received",
  "message.sent",
  "message.failed",
  "message.updated",
] as const;

export type RealtimeEvent = (typeof EVENTS)[number];

/** Emitted after a reconnect: everything that happened while disconnected was missed entirely. */
export type RealtimeSignal = RealtimeEvent | "resync";
export type RealtimeState = "connecting" | "live" | "offline";

export function useRealtime(token: string | undefined, onEvent: (event: RealtimeSignal, payload: unknown) => void) {
  const [state, setState] = useState<RealtimeState>("connecting");
  const handler = useRef(onEvent);
  handler.current = onEvent;

  useEffect(() => {
    if (!token) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/communication`, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    for (const event of EVENTS) connection.on(event, (payload: unknown) => handler.current(event, payload));

    connection.onreconnecting(() => setState("connecting"));
    connection.onreconnected(() => {
      setState("live");
      handler.current("resync", null);
    });
    connection.onclose(() => setState("offline"));

    connection
      .start()
      .then(() => setState("live"))
      .catch(() => setState("offline"));

    return () => {
      connection.stop().catch(() => undefined);
    };
  }, [token]);

  return state;
}
