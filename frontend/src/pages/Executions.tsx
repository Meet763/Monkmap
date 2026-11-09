import { useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { ExecutionSummary } from "@/api/types";
import { Badge, Card, Spinner } from "@/components/ui";
import { History } from "lucide-react";

function statusTone(s: string) {
  if (s === "Succeeded") return "success" as const;
  if (s === "Failed") return "danger" as const;
  if (s === "Running") return "running" as const;
  return "muted" as const;
}

export function Executions() {
  const navigate = useNavigate();
  const { data, isLoading } = useQuery({
    queryKey: ["executions"],
    queryFn: async () => (await api.get<ExecutionSummary[]>("/executions")).data,
    refetchInterval: 4000,
  });

  return (
    <div className="mx-auto max-w-5xl p-6">
      <h1 className="mb-1 text-2xl font-semibold">Executions</h1>
      <p className="mb-6 text-sm text-muted">History of your workflow runs.</p>

      {isLoading ? (
        <Spinner />
      ) : !data || data.length === 0 ? (
        <Card className="flex flex-col items-center gap-3 py-16 text-center">
          <History className="h-10 w-10 text-muted" />
          <p className="text-muted">No runs yet. Open a workflow and hit Run.</p>
        </Card>
      ) : (
        <Card className="divide-y divide-border p-0">
          {data.map((e) => (
            <button
              key={e.id}
              onClick={() => navigate(`/executions/${e.id}`)}
              className="flex w-full items-center justify-between px-4 py-3 text-left hover:bg-surface-2"
            >
              <div className="flex items-center gap-3">
                <Badge tone={statusTone(e.status)}>{e.status}</Badge>
                <span className="font-mono text-xs text-muted">{e.id.slice(0, 8)}</span>
                <span className="text-sm text-muted">{e.triggerType}</span>
              </div>
              <span className="text-xs text-muted">
                {e.startedAt ? new Date(e.startedAt).toLocaleString() : "queued"}
              </span>
            </button>
          ))}
        </Card>
      )}
    </div>
  );
}
