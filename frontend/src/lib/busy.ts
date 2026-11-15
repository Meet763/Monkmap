import { useSyncExternalStore } from "react";

// Counts requests that are in flight, so the UI can show that something is loading or saving.
let inFlight = 0;
const listeners = new Set<() => void>();

function emit() {
  listeners.forEach((l) => l());
}

export function startBusy() {
  inFlight += 1;
  emit();
}

export function endBusy() {
  inFlight = Math.max(0, inFlight - 1);
  emit();
}

/** Number of requests currently in progress (saves, loads, runs, deletes...). */
export function useBusyCount(): number {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => inFlight
  );
}
