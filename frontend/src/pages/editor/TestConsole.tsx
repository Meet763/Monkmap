import { useMemo, useState } from "react";
import { api } from "@/api/client";
import type { ExecutionDetail } from "@/api/types";
import { Button, Label, Textarea } from "@/components/ui";
import { Copy, Send } from "lucide-react";

export interface WebhookInfo {
  method: string;
  path: string;
  token: string;
}

interface Props {
  workflowId: string | null;
  webhook: WebhookInfo | null;
  /** Persists the workflow (creating it on first save) and returns its id, or null. */
  save: () => Promise<string | null>;
  onViewRun: (executionId: string) => void;
}

const FINISHED = new Set(["Succeeded", "Failed", "Canceled"]);

export function TestConsole({ workflowId, webhook, save, onViewRun }: Props) {
  const [body, setBody] = useState('{\n  "hello": "world"\n}');
  const [sending, setSending] = useState(false);
  const [httpStatus, setHttpStatus] = useState<number | null>(null);
  const [execId, setExecId] = useState<string | null>(null);
  const [execStatus, setExecStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const method = (webhook?.method && webhook.method !== "ANY" ? webhook.method : "POST").toUpperCase();
  const hasBody = method !== "GET";

  // Built from the *current* id; before the first save there's no real URL yet.
  const url = useMemo(() => {
    if (!workflowId) return null;
    const base = `${window.location.origin}/api/webhooks/${workflowId}`;
    const path = webhook?.path ? `/${webhook.path.replace(/^\/+/, "")}` : "";
    const query = webhook?.token ? `?token=${encodeURIComponent(webhook.token)}` : "";
    return `${base}${path}${query}`;
  }, [workflowId, webhook?.path, webhook?.token]);

  const curl = useMemo(() => {
    const shown = url ?? "<save the workflow to get the URL>";
    const parts = [`curl -X ${method} "${shown}"`];
    if (hasBody) {
      parts.push(`  -H "Content-Type: application/json"`);
      parts.push(`  -d '${body.replace(/'/g, "'\\''")}'`);
    }
    return parts.join(" \\\n");
  }, [url, method, hasBody, body]);

  async function pollExecution(id: string) {
    const deadline = Date.now() + 30_000;
    while (Date.now() < deadline) {
      try {
        const { data } = await api.get<ExecutionDetail>(`/executions/${id}`);
        setExecStatus(data.status);
        if (FINISHED.has(data.status)) return;
      } catch {
        /* keep polling */
      }
      await new Promise((r) => setTimeout(r, 800));
    }
  }

  async function send() {
    setError(null);
    setHttpStatus(null);
    setExecId(null);
    setExecStatus(null);

    if (!webhook) {
      setError("Add a Webhook Trigger node to the canvas first.");
      return;
    }
    if (hasBody) {
      try {
        if (body.trim()) JSON.parse(body);
      } catch {
        setError("Request body is not valid JSON.");
        return;
      }
    }

    setSending(true);
    try {
      // Save first so the workflow + active version exist and the URL is real.
      const wid = await save();
      if (!wid) {
        setError("Could not save the workflow.");
        return;
      }
      const base = `${window.location.origin}/api/webhooks/${wid}`;
      const path = webhook.path ? `/${webhook.path.replace(/^\/+/, "")}` : "";
      const query = webhook.token ? `?token=${encodeURIComponent(webhook.token)}` : "";
      const target = `${base}${path}${query}`;

      // Plain fetch (no auth header) so it mirrors a real external call.
      const res = await fetch(target, {
        method,
        headers: hasBody ? { "Content-Type": "application/json" } : undefined,
        body: hasBody ? body : undefined,
      });
      setHttpStatus(res.status);

      const json = await res.json().catch(() => null);
      const id = json?.executionId as string | undefined;
      if (res.ok && id) {
        setExecId(id);
        setExecStatus("Running");
        await pollExecution(id);
      } else if (!res.ok) {
        setError(json?.message ?? `Request failed (HTTP ${res.status}).`);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Request failed.");
    } finally {
      setSending(false);
    }
  }

  const statusTone =
    execStatus === "Succeeded"
      ? "text-success"
      : execStatus === "Failed"
      ? "text-danger"
      : "text-muted";

  return (
    <div className="flex h-full flex-col">
      <div className="border-b border-border px-4 py-3">
        <h3 className="font-medium">Test API</h3>
        <p className="text-xs text-muted">Send a request to this workflow's webhook and watch the run.</p>
      </div>

      <div className="flex-1 space-y-4 overflow-auto p-4">
        {!webhook ? (
          <p className="text-sm text-muted">
            Add a <span className="font-medium">Webhook Trigger</span> node from the palette to expose
            this workflow as an API endpoint.
          </p>
        ) : (
          <>
            <div>
              <Label>Endpoint</Label>
              <div className="flex items-center gap-2">
                <code className="flex-1 truncate rounded-md bg-surface-2 px-2 py-1.5 text-xs">
                  {method} {url ?? "(save to generate URL)"}
                </code>
                {url && (
                  <button
                    onClick={() => navigator.clipboard?.writeText(url)}
                    className="rounded-md p-1.5 text-muted hover:bg-surface-2"
                    title="Copy URL"
                  >
                    <Copy className="h-4 w-4" />
                  </button>
                )}
              </div>
            </div>

            {hasBody && (
              <div>
                <Label>Request body (JSON)</Label>
                <Textarea rows={6} value={body} onChange={(e) => setBody(e.target.value)} />
              </div>
            )}

            <Button className="w-full" onClick={send} loading={sending}>
              <Send className="h-4 w-4" /> Send request
            </Button>

            {error && <p className="text-sm text-danger">{error}</p>}

            {(httpStatus !== null || execId) && (
              <div className="space-y-1 rounded-md border border-border p-3 text-sm">
                {httpStatus !== null && (
                  <div>
                    HTTP response: <span className="font-medium">{httpStatus}</span>
                  </div>
                )}
                {execStatus && (
                  <div>
                    Run status: <span className={`font-medium ${statusTone}`}>{execStatus}</span>
                  </div>
                )}
                {execId && (
                  <Button variant="ghost" size="sm" onClick={() => onViewRun(execId)}>
                    View run detail
                  </Button>
                )}
              </div>
            )}

            <div>
              <Label>cURL</Label>
              <div className="flex items-start gap-2">
                <pre className="flex-1 overflow-auto rounded-md bg-surface-2 px-2 py-1.5 text-[11px] leading-relaxed">
                  {curl}
                </pre>
                <button
                  onClick={() => navigator.clipboard?.writeText(curl)}
                  className="rounded-md p-1.5 text-muted hover:bg-surface-2"
                  title="Copy cURL"
                >
                  <Copy className="h-4 w-4" />
                </button>
              </div>
            </div>

            <p className="pt-1 text-xs text-muted">
              Tip: read the payload downstream with{" "}
              <code className="rounded bg-surface-2 px-1">{"{{$json.body.field}}"}</code> or{" "}
              <code className="rounded bg-surface-2 px-1">{"{{$json.query.x}}"}</code>.
            </p>
          </>
        )}
      </div>
    </div>
  );
}
