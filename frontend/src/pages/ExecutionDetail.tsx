import { useNavigate, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { ExecutionDetail as ExecDetail } from "@/api/types";
import { Badge, Card, Spinner } from "@/components/ui";
import { ArrowLeft } from "lucide-react";

function tone(s: string) {
  if (s === "Succeeded") return "success" as const;
  if (s === "Failed") return "danger" as const;
  if (s === "Running") return "running" as const;
  return "muted" as const;
}

function pretty(json?: string | null) {
  if (!json) return "—";
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

export function ExecutionDetail() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const { data, isLoading } = useQuery({
    queryKey: ["execution", id],
    queryFn: async () => (await api.get<ExecDetail>(`/executions/${id}`)).data,
    refetchInterval: (q) =>
      q.state.data && ["Succeeded", "Failed", "Canceled"].includes(q.state.data.status)
        ? false
        : 1500,
  });

  if (isLoading || !data) return <Spinner />;

  return (
    <div className="mx-auto max-w-4xl p-6">
      <div className="mb-5 flex items-center gap-3">
        <button
          onClick={() => navigate(-1)}
          className="rounded-md p-1.5 text-muted hover:bg-surface-2"
        >
          <ArrowLeft className="h-4 w-4" />
        </button>
        <h1 className="text-xl font-semibold">Execution</h1>
        <Badge tone={tone(data.status)}>{data.status}</Badge>
        <span className="font-mono text-xs text-muted">{data.id.slice(0, 8)}</span>
      </div>

      {data.error && (
        <Card className="mb-4 border-danger/40 bg-danger/5">
          <p className="text-sm text-danger">{data.error}</p>
        </Card>
      )}

      <div className="space-y-3">
        {data.nodes.map((n) => (
          <Card key={n.id} className="p-4">
            <div className="mb-3 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Badge tone={tone(n.status)}>{n.status}</Badge>
                <span className="font-medium">{n.nodeId}</span>
                <span className="text-xs text-muted">{n.nodeType}</span>
              </div>
              {n.error && <span className="text-sm text-danger">{n.error}</span>}
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <div>
                <p className="mb-1 text-xs font-medium text-muted">Input</p>
                <pre className="max-h-48 overflow-auto rounded-lg bg-surface-2 p-3 text-xs">
                  {pretty(n.inputJson)}
                </pre>
              </div>
              <div>
                <p className="mb-1 text-xs font-medium text-muted">Output</p>
                <pre className="max-h-48 overflow-auto rounded-lg bg-surface-2 p-3 text-xs">
                  {pretty(n.outputJson)}
                </pre>
              </div>
            </div>
          </Card>
        ))}
      </div>
    </div>
  );
}
