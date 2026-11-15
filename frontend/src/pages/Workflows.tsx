import { useNavigate } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, apiError } from "@/api/client";
import type { WorkflowSummary } from "@/api/types";
import { Badge, Button, Card, Spinner } from "@/components/ui";
import { LOCKED_REASON, NO_ACCESS, denyReason, useAccess } from "@/lib/access";
import { useDialog } from "@/components/ConfirmDialog";
import { Lock, Plus, Workflow as WorkflowIcon, Trash2 } from "lucide-react";

export function Workflows() {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const access = useAccess();
  const { confirm, notify } = useDialog();
  const canCreate = access.has("workflows.create");
  const canDelete = access.has("workflows.delete");

  const { data, isLoading } = useQuery({
    queryKey: ["workflows", access.current?.accountId],
    queryFn: async () => (await api.get<WorkflowSummary[]>("/workflows")).data,
  });

  // Opens a blank editor; nothing is saved to the database until the user clicks Save.
  const createNew = () => navigate("/workflows/new/editor");

  const remove = useMutation({
    mutationFn: async (id: string) => api.delete(`/workflows/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["workflows"] }),
    onError: (err) => void notify("Could not delete the workflow", apiError(err, "Something went wrong.")),
  });

  return (
    <div className="mx-auto max-w-5xl p-6">
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Workflows</h1>
          <p className="text-sm text-muted">Design, save and run your automations.</p>
        </div>
        <span title={denyReason(canCreate)}>
          <Button onClick={createNew} disabled={!canCreate}>
            <Plus className="h-4 w-4" /> New workflow
          </Button>
        </span>
      </div>

      {isLoading ? (
        <Spinner />
      ) : !data || data.length === 0 ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <WorkflowIcon className="h-10 w-10 text-muted" />
          <p className="text-muted">No workflows yet.</p>
          {canCreate && (
            <Button onClick={createNew}>
              <Plus className="h-4 w-4" /> Create your first workflow
            </Button>
          )}
        </Card>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {data.map((wf) => {
            const deleteReason = !canDelete ? NO_ACCESS : wf.isLocked ? LOCKED_REASON : "";
            return (
              <Card
                key={wf.id}
                className="cursor-pointer transition-colors hover:border-primary/50"
                onClick={() => navigate(`/workflows/${wf.id}/editor`)}
              >
                <div className="mb-3 flex items-start justify-between">
                  <WorkflowIcon className="h-5 w-5 text-primary" />
                  <div className="flex items-center gap-1.5">
                    {wf.isLocked && (
                      <span title={`Locked by ${wf.lockedBy ?? "someone"}`}>
                        <Badge tone="danger">
                          <Lock className="mr-1 inline h-3 w-3" />
                          Locked
                        </Badge>
                      </span>
                    )}
                    {wf.isActive ? <Badge tone="success">Active</Badge> : <Badge>Inactive</Badge>}
                  </div>
                </div>
                <h3 className="truncate font-medium">{wf.name}</h3>
                <p className="mt-1 line-clamp-2 min-h-[2.5rem] text-sm text-muted">
                  {wf.description || "No description"}
                </p>
                <div className="mt-3 flex items-center justify-between border-t border-border pt-3">
                  <span className="text-xs text-muted">
                    Updated {new Date(wf.updatedAt).toLocaleDateString()}
                  </span>
                  <span title={deleteReason}>
                    <button
                      disabled={!!deleteReason}
                      className="rounded-md p-1.5 text-muted hover:bg-surface-2 hover:text-danger disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-muted"
                      onClick={async (e) => {
                        e.stopPropagation();
                        const ok = await confirm({
                          title: `Delete "${wf.name}"?`,
                          message: "This permanently removes the workflow and its run history. This cannot be undone.",
                          confirmText: "Delete workflow",
                          danger: true,
                        });
                        if (ok) remove.mutate(wf.id);
                      }}
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </span>
                </div>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
