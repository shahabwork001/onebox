"use client";

import { useEffect, useRef, useState } from "react";

export type MenuAction = { label: string; onSelect: () => void; tone?: "danger" };

/**
 * Secondary conversation actions live behind one trigger. Four competing buttons crowded the header
 * badly enough to squeeze the contact's own details into ellipses, and only one of them is ever the
 * thing an agent came to do.
 */
export function ActionMenu({ actions, busy }: { actions: MenuAction[]; busy: boolean }) {
  const [open, setOpen] = useState(false);
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const closeOnOutside = (event: MouseEvent) => {
      if (!container.current?.contains(event.target as Node)) setOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => event.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", closeOnOutside);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("mousedown", closeOnOutside);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open]);

  if (actions.length === 0) return null;

  return (
    <div className="action-menu" ref={container}>
      <button
        type="button"
        className="icon-button"
        disabled={busy}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label="More actions"
        title="More actions"
        onClick={() => setOpen(value => !value)}
      >
        <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor" aria-hidden="true">
          <circle cx="5" cy="12" r="1.6" />
          <circle cx="12" cy="12" r="1.6" />
          <circle cx="19" cy="12" r="1.6" />
        </svg>
      </button>

      {open && (
        <div className="action-menu-list" role="menu">
          {actions.map(action => (
            <button
              key={action.label}
              type="button"
              role="menuitem"
              className={action.tone === "danger" ? "action-item is-danger" : "action-item"}
              onClick={() => {
                setOpen(false);
                action.onSelect();
              }}
            >
              {action.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
