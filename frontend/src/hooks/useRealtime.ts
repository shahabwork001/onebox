"use client";

import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { API_BASE } from "@/lib/api";

/**
 * Hub events are hints, not data. Every one of them triggers a REST reload, so the UI is always
 * showing authoritative server state and a dropped connection degrades to "slightly stale" rather
 * than "wrong".
 */
const EVENTS = [
  "message.received",
  "message.updated",
  "message.sent",
  "message.failed",
  "ticket.created",
  "ticket.claimed",
  "ticket.removed",
  "ticket.assignment.added",
  "ticket.assignment.removed",
  "ticket.status.changed",
] as const;

export type RealtimeState = "connecting" | "live" | "offline";

export function useRealtime(token: string | undefined, onEvent: () => void) {
  const [state, setState] = useState<RealtimeState>("connecting");
  const handler = useRef(onEvent);
  handler.current = onEvent;

  useEffect(() => {
    if (!token) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/communication`, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    for (const event of EVENTS) connection.on(event, () => handler.current());
    connection.onreconnecting(() => setState("connecting"));
    connection.onreconnected(() => {
      setState("live");
      handler.current();
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
