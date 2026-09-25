export function duration(seconds: number): string {
  if (seconds <= 0) return "0 ч 0 мин";
  if (seconds < 1) return "<1 с";
  if (seconds < 60) return `${Math.floor(seconds)} с`;
  return `${Math.floor(seconds / 3600)} ч ${Math.floor(seconds / 60) % 60} мин`;
}

export function calendarDuration(seconds: number): string {
  if (seconds <= 0) return "0";
  if (seconds < 60) return duration(seconds);
  if (seconds < 3600) return `${Math.floor(seconds / 60)} мин`;
  return `${(seconds / 3600).toFixed(1)} ч`;
}

export function compactCalendarDuration(seconds: number): string {
  if (seconds <= 0) return "0";
  if (seconds < 1) return "<1с";
  if (seconds < 60) return `${Math.floor(seconds)}с`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}м`;
  return `${Math.floor(seconds / 3600)}ч`;
}
