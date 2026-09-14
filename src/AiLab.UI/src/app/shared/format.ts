/** Parses a .NET TimeSpan string ("00:00:04.4917205" or "-" / null) into milliseconds. */
export function timeSpanToMs(value: string | null | undefined): number | null {
  if (!value) return null;
  const match = /^(-)?(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(value);
  if (!match) return null;
  const [, neg, days, hours, minutes, seconds, fraction] = match;
  const totalSeconds =
    (Number(days ?? 0) * 86400) + (Number(hours) * 3600) + (Number(minutes) * 60) + Number(seconds);
  const fractionMs = fraction ? Number(`0.${fraction}`) * 1000 : 0;
  const ms = totalSeconds * 1000 + fractionMs;
  return neg ? -ms : ms;
}

export function formatMs(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return '—';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  return `${(ms / 1000).toFixed(2)}s`;
}

export function formatTimeSpan(value: string | null | undefined): string {
  return formatMs(timeSpanToMs(value));
}

export function formatCost(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  if (value === 0) return '$0';
  if (value < 0.01) return `$${value.toFixed(6)}`;
  return `$${value.toFixed(4)}`;
}

export function formatTokensPerSecond(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  return `${value.toFixed(1)} tok/s`;
}
