import { Loader2 } from "lucide-react";
import { useBusyCount } from "@/lib/busy";

/**
 * Shown on every page while any request is running: a thin progress bar across the top and a small
 * "Working…" pill in the bottom-right corner. It appears for saves, loads, runs, locks and deletes.
 */
export function GlobalLoader() {
  const busy = useBusyCount();
  if (busy === 0) return null;

  return (
    <>
      <div
        role="progressbar"
        aria-label="Loading"
        className="fixed inset-x-0 top-0 z-50 h-0.5 overflow-hidden bg-primary/20"
      >
        <div className="h-full w-1/3 animate-[loadbar_1.2s_ease-in-out_infinite] bg-primary" />
      </div>
      <div
        role="status"
        aria-live="polite"
        className="fixed bottom-4 right-4 z-50 flex items-center gap-2 rounded-full border border-border bg-surface px-3 py-2 text-sm text-text shadow-lg"
      >
        <Loader2 className="h-4 w-4 animate-spin text-primary" />
        Working{busy > 1 ? ` (${busy})` : ""}…
      </div>
      <style>{`@keyframes loadbar { 0% { transform: translateX(-100%); } 100% { transform: translateX(300%); } }`}</style>
    </>
  );
}
