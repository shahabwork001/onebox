"use client";

import { useEffect, useRef, useState } from "react";
import type { Agent } from "@/lib/types";
import { Avatar } from "./Primitives";

/** Assignment control for holders of `tickets.assign` — the admin's "tag an agent" surface. */
export function AssignMenu({
  agents,
  assignedAgentId,
  busy,
  onAssign,
}: {
  agents: Agent[];
  assignedAgentId: string | null;
  busy: boolean;
  onAssign: (agentId: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;

    const closeOnOutside = (event: MouseEvent) => {
      if (!container.current?.contains(event.target as Node)) setOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };

    document.addEventListener("mousedown", closeOnOutside);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("mousedown", closeOnOutside);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open]);

  return (
    <div className="assign" ref={container}>
      <button
        type="button"
        className="button button-ghost"
        disabled={busy}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen(value => !value)}
      >
        {assignedAgentId ? "Reassign" : "Assign"} ▾
      </button>

      {open && (
        <div className="assign-menu" role="menu">
          <p className="assign-menu-title">Assign to agent</p>

          {agents.length === 0 && <p className="assign-menu-empty">No agents available.</p>}

          {agents.map(agent => (
            <button
              key={agent.id}
              type="button"
              role="menuitem"
              className={agent.id === assignedAgentId ? "assign-option is-current" : "assign-option"}
              disabled={agent.id === assignedAgentId}
              onClick={() => {
                setOpen(false);
                onAssign(agent.id);
              }}
            >
              <Avatar name={agent.displayName} size="sm" />
              <span className="assign-option-copy">
                <b>{agent.displayName}</b>
                <small>{agent.email}</small>
              </span>
              {agent.id === assignedAgentId && <span className="assign-current-mark">Current</span>}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
