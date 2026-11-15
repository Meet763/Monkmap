import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { AlertTriangle, Info } from "lucide-react";
import { Button } from "@/components/ui";

interface ConfirmOptions {
  title: string;
  message?: string;
  confirmText?: string;
  cancelText?: string;
  danger?: boolean;
}

interface DialogState extends ConfirmOptions {
  resolve: (ok: boolean) => void;
  infoOnly: boolean;
}

interface DialogApi {
  /** Resolves true if the user confirms, false if they cancel. */
  confirm: (options: ConfirmOptions) => Promise<boolean>;
  /** Shows a message with a single OK button. */
  notify: (title: string, message?: string) => Promise<void>;
}

const DialogContext = createContext<DialogApi | null>(null);

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<DialogState | null>(null);

  const confirm = useCallback(
    (options: ConfirmOptions) =>
      new Promise<boolean>((resolve) => setState({ ...options, resolve, infoOnly: false })),
    []
  );

  const notify = useCallback(
    (title: string, message?: string) =>
      new Promise<void>((resolve) =>
        setState({ title, message, confirmText: "OK", resolve: () => resolve(), infoOnly: true })
      ),
    []
  );

  function close(ok: boolean) {
    state?.resolve(ok);
    setState(null);
  }

  return (
    <DialogContext.Provider value={{ confirm, notify }}>
      {children}
      {state && <Dialog state={state} onClose={close} />}
    </DialogContext.Provider>
  );
}

function Dialog({ state, onClose }: { state: DialogState; onClose: (ok: boolean) => void }) {
  const confirmRef = useRef<HTMLButtonElement>(null);
  const danger = !!state.danger;

  useEffect(() => {
    confirmRef.current?.focus();
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape" && !state.infoOnly) onClose(false);
      if (e.key === "Enter") onClose(true);
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [state, onClose]);

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center p-4">
      <div
        className="absolute inset-0 bg-black/50 backdrop-blur-sm"
        onClick={() => !state.infoOnly && onClose(false)}
      />
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="dialog-title"
        className="relative w-full max-w-md rounded-2xl border border-border bg-surface p-6 shadow-2xl"
      >
        <div className="flex items-start gap-4">
          <span
            className={
              danger
                ? "flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-danger/15 text-danger"
                : "flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/15 text-primary"
            }
          >
            {danger ? <AlertTriangle className="h-5 w-5" /> : <Info className="h-5 w-5" />}
          </span>
          <div className="min-w-0 flex-1">
            <h2 id="dialog-title" className="text-base font-semibold text-text">
              {state.title}
            </h2>
            {state.message && <p className="mt-1.5 text-sm text-muted">{state.message}</p>}
          </div>
        </div>

        <div className="mt-6 flex justify-end gap-2">
          {!state.infoOnly && (
            <Button variant="secondary" onClick={() => onClose(false)}>
              {state.cancelText ?? "Cancel"}
            </Button>
          )}
          <button
            ref={confirmRef}
            onClick={() => onClose(true)}
            className={
              danger
                ? "inline-flex h-10 items-center justify-center rounded-lg bg-danger px-4 text-sm font-medium text-white hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-danger/50"
                : "inline-flex h-10 items-center justify-center rounded-lg bg-primary px-4 text-sm font-medium text-primary-fg hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
            }
          >
            {state.confirmText ?? "Confirm"}
          </button>
        </div>
      </div>
    </div>
  );
}

/** Use inside any component: `const { confirm, notify } = useDialog();` */
export function useDialog(): DialogApi {
  const ctx = useContext(DialogContext);
  if (!ctx) throw new Error("useDialog must be used inside ConfirmProvider");
  return ctx;
}
