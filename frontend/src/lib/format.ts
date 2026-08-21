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
