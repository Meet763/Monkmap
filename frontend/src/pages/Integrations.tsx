import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, apiError } from "@/api/client";
import type { ConnectionItem, IntegrationProvider } from "@/api/types";
import { Button, Card } from "@/components/ui";
import { Check, Copy, Plug, Trash2 } from "lucide-react";

export function Integrations() {
  const qc = useQueryClient();
  const [params, setParams] = useSearchParams();
  const [banner, setBanner] = useState<{ ok: boolean; text: string } | null>(null);
  const [copiedId, setCopiedId] = useState<string | null>(null);

  const { data: providers } = useQuery({
    queryKey: ["integrations"],
    queryFn: async () => (await api.get<IntegrationProvider[]>("/integrations")).data,
  });
  const { data: connections } = useQuery({
    queryKey: ["connections"],
    queryFn: async () => (await api.get<ConnectionItem[]>("/connections")).data,
  });

  // Show the OAuth redirect outcome once, then clean the URL.
  useEffect(() => {
    if (params.get("connected")) {
      setBanner({ ok: true, text: `Connected ${params.get("provider") ?? "account"} ✓` });
      qc.invalidateQueries({ queryKey: ["integrations"] });
      qc.invalidateQueries({ queryKey: ["connections"] });
      setParams({}, { replace: true });
    } else if (params.get("error")) {
      setBanner({ ok: false, text: `Connection failed: ${params.get("error")}` });
      setParams({}, { replace: true });
    }
  }, [params, qc, setParams]);

  async function connect(providerKey: string) {
    try {
      const { data } = await api.get<{ url: string }>(`/integrations/${providerKey}/connect-url`);
      window.location.href = data.url; // full redirect to the provider's consent screen
    } catch (err) {
      setBanner({ ok: false, text: apiError(err, "Could not start connection.") });
    }
  }

  async function disconnect(id: string) {
    await api.delete(`/connections/${id}`);
    qc.invalidateQueries({ queryKey: ["integrations"] });
    qc.invalidateQueries({ queryKey: ["connections"] });
  }

  function copy(id: string) {
    navigator.clipboard?.writeText(id);
    setCopiedId(id);
    setTimeout(() => setCopiedId((c) => (c === id ? null : c)), 1500);
  }

  return (
    <div className="mx-auto max-w-4xl p-6">
      <h1 className="mb-1 text-2xl font-semibold">Integrations</h1>
      <p className="mb-6 text-sm text-muted">
        Connect an account once. Workflows and AI agents then reuse it — no integration code.
      </p>

      {banner && (
        <div
          className={`mb-4 rounded-lg border px-4 py-2 text-sm ${
            banner.ok ? "border-success/40 text-success" : "border-danger/40 text-danger"
          }`}
        >
          {banner.text}
        </div>
      )}

      {/* Providers */}
      <div className="grid gap-3 sm:grid-cols-2">
        {(providers ?? []).map((p) => (
          <Card key={p.key} className="flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3">
                <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-surface-2">
                  <Plug className="h-4 w-4 text-muted" />
                </span>
                <div>
                  <div className="font-medium">{p.displayName}</div>
                  <div className="text-xs text-muted">{p.actions.length} actions</div>
                </div>
              </div>
              {p.isConnected ? (
                <span className="flex items-center gap-1 text-xs text-success">
                  <Check className="h-4 w-4" /> Connected
                </span>
              ) : p.isConfigured ? (
                <Button size="sm" onClick={() => connect(p.key)}>
                  Connect
                </Button>
              ) : (
                <span className="text-xs text-muted">Not configured</span>
              )}
            </div>
            <div className="flex flex-wrap gap-1">
              {p.actions.map((a) => (
                <span key={a.key} className="rounded bg-surface-2 px-1.5 py-0.5 text-[11px] text-muted">
                  {a.displayName}
                </span>
              ))}
            </div>
          </Card>
        ))}
      </div>

      {/* Connected accounts */}
      <h2 className="mb-2 mt-8 text-lg font-semibold">Connected accounts</h2>
      {(connections ?? []).length === 0 ? (
        <p className="text-sm text-muted">No accounts connected yet.</p>
      ) : (
        <div className="space-y-2">
          {(connections ?? []).map((c) => (
            <Card key={c.id} className="flex items-center justify-between">
              <div className="min-w-0">
                <div className="font-medium capitalize">
                  {c.provider} — <span className="font-normal">{c.accountName}</span>
                </div>
                <div className="mt-1 flex items-center gap-2 text-xs text-muted">
                  <span>Connection id:</span>
                  <code className="rounded bg-surface-2 px-1">{c.id}</code>
                  <button
                    onClick={() => copy(c.id)}
                    className="rounded p-1 hover:bg-surface-2"
                    title="Copy id for the Integration Action node"
                  >
                    {copiedId === c.id ? <Check className="h-3.5 w-3.5 text-success" /> : <Copy className="h-3.5 w-3.5" />}
                  </button>
                </div>
              </div>
              <Button variant="danger" size="sm" onClick={() => disconnect(c.id)}>
                <Trash2 className="h-4 w-4" /> Disconnect
              </Button>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
