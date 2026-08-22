const TIME = { hour: "2-digit", minute: "2-digit" } as const;

export const formatTime = (value: string) => new Date(value).toLocaleTimeString([], TIME);

/** Today shows a clock, this week a weekday, anything older a short date — as chat lists conventionally do. */
export function formatListTimestamp(value: string) {
  const date = new Date(value);
  const now = new Date();
  const sameDay = date.toDateString() === now.toDateString();
  if (sameDay) return date.toLocaleTimeString([], TIME);

  const daysApart = Math.floor((now.getTime() - date.getTime()) / 86_400_000);
  if (daysApart < 7) return date.toLocaleDateString([], { weekday: "short" });
  return date.toLocaleDateString([], { day: "2-digit", month: "short" });
}

export function formatDayHeading(value: string) {
  const date = new Date(value);
  const now = new Date();
  if (date.toDateString() === now.toDateString()) return "Today";

  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);
  if (date.toDateString() === yesterday.toDateString()) return "Yesterday";

  return date.toLocaleDateString([], { day: "numeric", month: "long", year: "numeric" });
}

export const dayKeyOf = (value: string) => new Date(value).toDateString();

/** Compact durations for queue and metric cells: 45s, 12m, 3h 20m, 2d 4h. */
export function formatDuration(seconds: number | null | undefined) {
  if (seconds === null || seconds === undefined) return "—";
  const total = Math.max(Math.round(seconds), 0);
  if (total < 60) return `${total}s`;
  const minutes = Math.floor(total / 60);
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return hours + "h" + (minutes % 60 ? ` ${minutes % 60}m` : "");
  const days = Math.floor(hours / 24);
  return days + "d" + (hours % 24 ? ` ${hours % 24}h` : "");
}

export const secondsSince = (iso: string) => (Date.now() - new Date(iso).getTime()) / 1000;

