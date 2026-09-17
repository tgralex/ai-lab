export const MIN_PANEL_WIDTH = 200;
export const MAX_PANEL_WIDTH = 700;

export function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

export function loadPanelWidth(key: string, fallback: number): number {
  try {
    const saved = localStorage.getItem(`ailab.panel-width.${key}`);
    return saved ? Number(saved) || fallback : fallback;
  } catch {
    return fallback;
  }
}

export function savePanelWidth(key: string, width: number) {
  try {
    localStorage.setItem(`ailab.panel-width.${key}`, String(width));
  } catch {
    // Per-viewer convenience only — ignore storage failures.
  }
}
