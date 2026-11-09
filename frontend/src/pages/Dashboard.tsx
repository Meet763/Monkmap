import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { ExecutionSummary, WorkflowSummary } from "@/api/types";
import { Badge, Card } from "@/components/ui";
import { useAuth } from "@/store/auth";
import { Workflow, History, Plus } from "lucide-react";

export function Dashboard() {
  const { user } = useAuth();
  const { data: workflows } = useQuery({
    queryKey: ["workflows"],
    queryFn: async () => (await api.get<WorkflowSummary[]>("/workflows")).data,
  });
  const { data: executions } = useQuery({
    queryKey: ["executions"],
    queryFn: async () => (await api.get<ExecutionSummary[]>("/executions")).data,
  });

  const succeeded = executions?.filter((e) => e.status === "Succeeded").length ?? 0;
  const failed = executions?.filter((e) => e.status === "Failed").length ?? 0;

  return (
    <div className="mx-auto max-w-5xl p-6">
      <h1 className="text-2xl font-semibold">
        Welcome back{user ? `, ${user.displayName.split(" ")[0]}` : ""} 👋
      </h1>
      <p className="mb-6 text-sm text-muted">Here's what's happening in your workspace.</p>

      <div className="mb-6 grid gap-3 sm:grid-cols-3">
        <Card>
          <p className="text-sm text-muted">Workflows</p>
          <p className="mt-1 text-3xl font-semibold">{workflows?.length ?? 0}</p>
        </Card>
        <Card>
          <p className="text-sm text-muted">Successful runs</p>
          <p className="mt-1 text-3xl font-semibold text-success">{succeeded}</p>
        </Card>
        <Card>
          <p className="text-sm text-muted">Failed runs</p>
          <p className="mt-1 text-3xl font-semibold text-danger">{failed}</p>
        </Card>
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <div className="mb-3 flex items-center justify-between">
            <h2 className="flex items-center gap-2 font-medium">
              <Workflow className="h-4 w-4 text-primary" /> Recent workflows
            </h2>
            <Link to="/workflows" className="text-sm text-primary hover:underline">
              View all
            </Link>
          </div>
          <div className="space-y-1">
            {(workflows ?? []).slice(0, 5).map((w) => (
              <Link
                key={w.id}
                to={`/workflows/${w.id}/editor`}
                className="flex items-center justify-between rounded-lg px-2 py-2 text-sm hover:bg-surface-2"
              >
                <span className="truncate">{w.name}</span>
                {w.isActive ? <Badge tone="success">Active</Badge> : <Badge>Inactive</Badge>}
              </Link>
            ))}
            {(!workflows || workflows.length === 0) && (
              <Link
                to="/workflows"
                className="flex items-center gap-2 rounded-lg px-2 py-2 text-sm text-primary hover:bg-surface-2"
              >
                <Plus className="h-4 w-4" /> Create your first workflow
              </Link>
            )}
          </div>
        </Card>

        <Card>
          <div className="mb-3 flex items-center justify-between">
            <h2 className="flex items-center gap-2 font-medium">
              <History className="h-4 w-4 text-primary" /> Recent runs
            </h2>
            <Link to="/executions" className="text-sm text-primary hover:underline">
              View all
            </Link>
          </div>
          <div className="space-y-1">
            {(executions ?? []).slice(0, 5).map((e) => (
              <Link
                key={e.id}
                to={`/executions/${e.id}`}
                className="flex items-center justify-between rounded-lg px-2 py-2 text-sm hover:bg-surface-2"
              >
                <span className="font-mono text-xs text-muted">{e.id.slice(0, 8)}</span>
                <Badge
                  tone={
                    e.status === "Succeeded"
                      ? "success"
                      : e.status === "Failed"
                      ? "danger"
                      : "running"
                  }
                >
                  {e.status}
                </Badge>
              </Link>
            ))}
            {(!executions || executions.length === 0) && (
              <p className="px-2 py-2 text-sm text-muted">No runs yet.</p>
            )}
          </div>
        </Card>
      </div>
    </div>
  );
}
