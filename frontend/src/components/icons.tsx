import type { ReactNode } from "react";

/**
 * One stroked line-icon family at a single weight, so nothing in the product is drawn with a text
 * glyph or an emoji. Size is set by the caller; colour always follows currentColor.
 */
function Glyph({ children, size = 18, className }: { children: ReactNode; size?: number; className?: string }) {
  return (
    <svg
      className={className ? `icon ${className}` : "icon"}
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  );
}

type IconProps = { size?: number; className?: string };

export const IconDashboard = (p: IconProps) => (
  <Glyph {...p}>
    <rect x="3" y="3" width="7" height="9" rx="1.5" />
    <rect x="14" y="3" width="7" height="5" rx="1.5" />
    <rect x="14" y="12" width="7" height="9" rx="1.5" />
    <rect x="3" y="16" width="7" height="5" rx="1.5" />
  </Glyph>
);

export const IconInbox = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M4 5h16l1 8v5a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-5z" />
    <path d="M3 13h4l2 3h6l2-3h4" />
  </Glyph>
);

export const IconClock = (p: IconProps) => (
  <Glyph {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="M12 7v5l3 2" />
  </Glyph>
);

export const IconChat = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M21 11.5a8 8 0 0 1-11.6 7.1L4 20l1.4-5.4A8 8 0 1 1 21 11.5z" />
  </Glyph>
);

export const IconSearch = (p: IconProps) => (
  <Glyph {...p}>
    <circle cx="11" cy="11" r="7" />
    <path d="M20 20l-3.5-3.5" />
  </Glyph>
);

export const IconRefresh = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M20 11a8 8 0 1 0-1.7 6" />
    <path d="M20 4v6h-6" />
  </Glyph>
);

export const IconClose = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M18 6L6 18M6 6l12 12" />
  </Glyph>
);

export const IconSignOut = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
    <path d="M16 17l5-5-5-5" />
    <path d="M21 12H9" />
  </Glyph>
);

export const IconBack = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M15 18l-6-6 6-6" />
  </Glyph>
);

export const IconSend = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M21 3L10.5 13.5" />
    <path d="M21 3l-6.6 18-3.9-7.5L3 9.6z" />
  </Glyph>
);

export const IconLock = (p: IconProps) => (
  <Glyph {...p}>
    <rect x="4" y="10" width="16" height="11" rx="2" />
    <path d="M8 10V7a4 4 0 0 1 8 0v3" />
  </Glyph>
);

export const IconMessage = (p: IconProps) => (
  <Glyph {...p}>
    <rect x="3" y="5" width="18" height="14" rx="2" />
    <path d="M3.5 6.5L12 13l8.5-6.5" />
  </Glyph>
);

export const IconCheck = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M20 6L9 17l-5-5" />
  </Glyph>
);

export const IconSelect = (p: IconProps) => (
  <Glyph {...p}>
    <circle cx="12" cy="12" r="8" />
    <path d="M12 8v8M8 12h8" />
  </Glyph>
);

export const IconDownload = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M12 3v12" />
    <path d="M7 11l5 5 5-5" />
    <path d="M4 20h16" />
  </Glyph>
);

export const IconChevronDown = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M6 9l6 6 6-6" />
  </Glyph>
);

export const IconTeam = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M16 20v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
    <circle cx="9" cy="7" r="3.4" />
    <path d="M22 20v-2a4 4 0 0 0-3-3.9" />
    <path d="M16 3.6a4 4 0 0 1 0 7" />
  </Glyph>
);

export const IconAttach = (p: IconProps) => (
  <Glyph {...p}>
    <path d="M21 12.5L12.5 21a5 5 0 0 1-7-7l8.5-8.5a3.4 3.4 0 0 1 4.8 4.8l-8.5 8.5a1.8 1.8 0 0 1-2.5-2.5l7.8-7.8" />
  </Glyph>
);

