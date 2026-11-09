import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ReactFlow,
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  addEdge,
  useNodesState,
  useEdgesState,
  type Node,
  type Edge,
  type Connection,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";

import { api, apiError } from "@/api/client";
import type { NodeDefinition, WorkflowDetail, WorkflowGraph } from "@/api/types";
import { Button, Badge } from "@/components/ui";
import { WfNode, type WfNodeData, type NodeStatus } from "./WfNode";
import { NodeConfigPanel } from "./NodeConfigPanel";
import { NodePalette } from "./NodePalette";
import { TestConsole, type WebhookInfo } from "./TestConsole";
import { createExecutionConnection } from "@/lib/signalr";
import { LOCKED_REASON, NO_ACCESS, useAccess } from "@/lib/access";
import { ArrowLeft, Lock, Play, Save, Unlock, Zap } from "lucide-react";

const nodeTypes = { wfNode: WfNode };

export function Editor() {
  const { id = "" } = useParams();
  const navigate = useNavigate();

  const isNew = id === "new";
  // Null until the workflow is actually persisted (first Save). New workflows are NOT
  // created in the database until the user clicks Save.
  const [workflowId, setWorkflowId] = useState<string | null>(isNew ? null : id);

  const [nodes, setNodes, onNodesChange] = useNodesState<Node<WfNodeData>>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [rightTab, setRightTab] = useState<"config" | "test">("config");
  const [name, setName] = useState("Untitled Workflow");
  const [isActive, setIsActive] = useState(false);
  const [saving, setSaving] = useState(false);
  const [running, setRunning] = useState(false);
  const [runStatus, setRunStatus] = useState<string | null>(null);
  const [lastExecutionId, setLastExecutionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const hydrated = useRef(false);
  const counter = useRef(0);
  const qc = useQueryClient();

  const { data: defs } = useQuery({
    queryKey: ["node-types"],
    queryFn: async () => (await api.get<NodeDefinition[]>("/node-types")).data,
  });

  const { data: workflow } = useQuery({
    queryKey: ["workflow", id],
    queryFn: async () => (await api.get<WorkflowDetail>(`/workflows/${id}`)).data,
    enabled: !isNew,
  });

  // ---- Permissions and lock state ----
  // Permission is checked first ("You have no access"); then the lock ("Locked — unlock it").
  const access = useAccess();
  const locked = !isNew && !!workflow?.isLocked;
  const canEditPerm = access.has("workflows.edit");
  const canRunPerm = access.has("workflows.run");
  const canCreatePerm = access.has("workflows.create");
  const canEdit = canEditPerm && !locked && (workflowId ? true : canCreatePerm);
  const canRun = canRunPerm && !locked;
  const editReason = !canEditPerm
    ? NO_ACCESS
    : locked
    ? LOCKED_REASON
    : !workflowId && !canCreatePerm
    ? NO_ACCESS
    : "";
  const runReason = !canRunPerm ? NO_ACCESS : locked ? LOCKED_REASON : "";
  const lockReason = !canEditPerm ? NO_ACCESS : !workflowId ? "Save the workflow first" : "";

  const defByType = useMemo(() => {
    const m = new Map<string, NodeDefinition>();
    defs?.forEach((d) => m.set(d.type, d));
    return m;
  }, [defs]);

  // Hydrate canvas from saved graph once data is ready.
  useEffect(() => {
    if (hydrated.current || !defs) return;
    if (isNew) {
      hydrated.current = true; // brand-new workflow: start with a blank canvas
      return;
    }
    if (!workflow) return;
    hydrated.current = true;
    setName(workflow.name);
    setIsActive(workflow.isActive);

    let graph: WorkflowGraph = { nodes: [], connections: [] };
    try {
      graph = JSON.parse(workflow.graphJson);
    } catch {
      /* ignore */
    }

    setNodes(
      graph.nodes.map((n) => {
        const def = defByType.get(n.type);
        return {
          id: n.id,
          type: "wfNode",
          position: n.position,
          data: {
            nodeType: n.type,
            label: def?.displayName ?? n.type,
            icon: def?.icon ?? "box",
            isTrigger: def?.isTrigger ?? false,
            outputPorts: def?.outputPorts ?? ["main"],
            parameters: n.parameters ?? {},
            status: "idle" as NodeStatus,
          },
        } satisfies Node<WfNodeData>;
      })
    );
    setEdges(
      graph.connections.map((c) => ({
        id: c.id,
        source: c.source,
        target: c.target,
        sourceHandle: c.sourceHandle ?? undefined,
        targetHandle: c.targetHandle ?? undefined,
      }))
    );
  }, [workflow, defs, defByType, isNew, setNodes, setEdges]);

  const onConnect = useCallback(
    (c: Connection) => {
      if (!canEdit) return;
      setEdges((eds) =>
        addEdge({ ...c, id: `e_${c.source}_${c.sourceHandle}_${c.target}_${Date.now()}` }, eds)
      );
    },
    [setEdges, canEdit]
  );

  function addNode(def: NodeDefinition) {
    if (!canEdit) return;
    counter.current += 1;
    const newId = `${def.type}_${counter.current}_${Math.floor(Math.random() * 1000)}`;
    const defaults: Record<string, unknown> = {};
    def.parameters.forEach((p) => {
      if (p.default != null) defaults[p.name] = p.default;
    });
    const node: Node<WfNodeData> = {
      id: newId,
      type: "wfNode",
      position: { x: 120 + Math.random() * 200, y: 120 + Math.random() * 160 },
      data: {
        nodeType: def.type,
        label: def.displayName,
        icon: def.icon,
        isTrigger: def.isTrigger,
        outputPorts: def.outputPorts,
        parameters: defaults,
        status: "idle",
      },
    };
    setNodes((ns) => [...ns, node]);
    setSelectedId(newId);
  }

  function updateParams(nodeId: string, params: Record<string, unknown>) {
    if (!canEdit) return;
    setNodes((ns) =>
      ns.map((n) => (n.id === nodeId ? { ...n, data: { ...n.data, parameters: params } } : n))
    );
  }

  function deleteNode(nodeId: string) {
    if (!canEdit) return;
    setNodes((ns) => ns.filter((n) => n.id !== nodeId));
    setEdges((es) => es.filter((e) => e.source !== nodeId && e.target !== nodeId));
    if (selectedId === nodeId) setSelectedId(null);
  }

  function setAllStatus(status: NodeStatus) {
    setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status } })));
  }
  function setNodeStatus(nodeId: string, status: NodeStatus) {
    setNodes((ns) =>
      ns.map((n) => (n.id === nodeId ? { ...n, data: { ...n.data, status } } : n))
    );
  }

  function buildGraphJson(): string {
    const graph: WorkflowGraph = {
      nodes: nodes.map((n) => ({
        id: n.id,
        type: n.data.nodeType,
        position: n.position,
        parameters: n.data.parameters,
      })),
      connections: edges.map((e) => ({
        id: e.id,
        source: e.source,
        target: e.target,
        sourceHandle: e.sourceHandle ?? "main",
        targetHandle: e.targetHandle ?? "main",
      })),
    };
    return JSON.stringify(graph);
  }

  // Persists the workflow. Creates it on first save (nothing is stored until then),
  // then updates the URL to the real id. Returns the id, or null on failure.
  async function save(): Promise<string | null> {
    if (!canEdit) {
      setError(editReason || "You can't save this workflow.");
      return null;
    }
    setSaving(true);
    setError(null);
    try {
      let wid = workflowId;
      if (!wid) {
        const { data } = await api.post<WorkflowDetail>("/workflows", {
          name,
          description: null,
        });
        wid = data.id;
        setWorkflowId(wid);
        navigate(`/workflows/${wid}/editor`, { replace: true });
      }
      await api.put(`/workflows/${wid}`, { name, description: null, graphJson: buildGraphJson() });
      return wid;
    } catch (err) {
      setError(apiError(err, "Failed to save."));
      return null;
    } finally {
      setSaving(false);
    }
  }

  async function toggleLock() {
    if (!workflowId) return;
    setError(null);
    try {
      await api.post(`/workflows/${workflowId}/${locked ? "unlock" : "lock"}`);
      await qc.invalidateQueries({ queryKey: ["workflow", workflowId] });
      await qc.invalidateQueries({ queryKey: ["workflows"] });
    } catch (err) {
      setError(apiError(err, "Could not change the lock."));
    }
  }

  async function toggleActive() {
    if (!canEdit) return;
    let wid = workflowId;
    if (!wid) {
      wid = await save();
      if (!wid) return;
    }
    const next = !isActive;
    setIsActive(next);
    await api.post(`/workflows/${wid}/activate?active=${next}`);
  }

  async function run() {
    if (!canRun) {
      setError(runReason || "You can't run this workflow.");
      return;
    }
    setRunning(true);
    setError(null);
    setRunStatus("Saving…");
    try {
      const wid = await save();
      if (!wid) {
        setRunStatus("Failed");
        setRunning(false);
        return;
      }
      setAllStatus("idle");
      const { data } = await api.post<{ executionId: string }>(`/workflows/${wid}/run`, {
        triggerDataJson: null,
      });
      setLastExecutionId(data.executionId);
      setRunStatus("Running…");

      const conn = createExecutionConnection();
      conn.on("NodeStarted", (_exec: string, nodeId: string) => setNodeStatus(nodeId, "running"));
      conn.on("NodeCompleted", (_exec: string, nodeId: string, status: string) =>
        setNodeStatus(nodeId, status as NodeStatus)
      );
      conn.on("ExecutionFinished", (_exec: string, status: string) => {
        setRunStatus(status);
        setRunning(false);
        conn.stop();
      });
      await conn.start();
      await conn.invoke("Subscribe", data.executionId);
    } catch (err) {
      setError(apiError(err, "Failed to run."));
      setRunStatus("Failed");
      setRunning(false);
    }
  }

  const selectedNode = nodes.find((n) => n.id === selectedId);
  const selectedDef = selectedNode ? defByType.get(selectedNode.data.nodeType) : undefined;

  const webhookNode = nodes.find((n) => n.data.nodeType === "WebhookTrigger");
  const webhook: WebhookInfo | null = webhookNode
    ? {
        method: String(webhookNode.data.parameters.method ?? "POST"),
        path: String(webhookNode.data.parameters.path ?? ""),
        token: String(webhookNode.data.parameters.token ?? ""),
      }
    : null;

  return (
    <div className="flex h-full flex-col">
      {/* Top bar */}
      <div className="flex h-14 items-center gap-3 border-b border-border bg-surface px-4">
        <button
          onClick={() => navigate("/workflows")}
          className="rounded-md p-1.5 text-muted hover:bg-surface-2"
        >
          <ArrowLeft className="h-4 w-4" />
        </button>
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          disabled={!canEdit}
          title={editReason}
          className="w-64 rounded-md bg-transparent px-2 py-1 text-sm font-medium focus:bg-surface-2 focus:outline-none disabled:cursor-not-allowed disabled:opacity-60"
        />
        {runStatus && (
          <Badge
            tone={
              runStatus === "Succeeded"
                ? "success"
                : runStatus === "Failed"
                ? "danger"
                : "running"
            }
          >
            {runStatus}
          </Badge>
        )}
        {error && <span className="text-sm text-danger">{error}</span>}
        <div className="flex-1" />
        <span title={canEdit ? "Arm triggers" : editReason}>
          <button
            onClick={toggleActive}
            disabled={!canEdit}
            className="flex items-center gap-1.5 rounded-lg px-2 py-1 text-sm text-muted hover:bg-surface-2 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <Zap className={isActive ? "h-4 w-4 text-success" : "h-4 w-4"} />
            {isActive ? "Active" : "Inactive"}
          </button>
        </span>
        {lastExecutionId && (
          <Button variant="ghost" size="sm" onClick={() => navigate(`/executions/${lastExecutionId}`)}>
            View run
          </Button>
        )}
        <span title={lockReason || (locked ? "Unlock this workflow" : "Lock this workflow")}>
          <Button
            variant="secondary"
            size="sm"
            onClick={toggleLock}
            disabled={!!lockReason}
          >
            {locked ? <Unlock className="h-4 w-4" /> : <Lock className="h-4 w-4" />}
            {locked ? "Unlock" : "Lock"}
          </Button>
        </span>
        <span title={editReason}>
          <Button variant="secondary" size="sm" onClick={save} loading={saving} disabled={!canEdit}>
            <Save className="h-4 w-4" /> Save
          </Button>
        </span>
        <span title={runReason}>
          <Button size="sm" onClick={run} loading={running} disabled={!canRun}>
            <Play className="h-4 w-4" /> Run
          </Button>
        </span>
      </div>

      {locked && (
        <div className="flex items-center gap-2 border-b border-border bg-danger/10 px-4 py-2 text-sm text-danger">
          <Lock className="h-4 w-4" />
          <span>
            Locked by {workflow?.lockedBy ?? "someone"}
            {workflow?.lockedAt ? ` on ${new Date(workflow.lockedAt).toLocaleString()}` : ""}.{" "}
            {canEditPerm
              ? "Unlock it to make changes."
              : "You can view it, but you have no access to unlock it."}
          </span>
        </div>
      )}

      {/* Body */}
      <div className="flex min-h-0 flex-1">
        <div title={canEdit ? "" : editReason} className="shrink-0">
          <div className={canEdit ? "" : "pointer-events-none opacity-50"}>
            <NodePalette defs={defs ?? []} onAdd={addNode} />
          </div>
        </div>

        <div className="min-w-0 flex-1">
          <ReactFlow
            nodes={nodes}
            edges={edges}
            onNodesChange={canEdit ? onNodesChange : undefined}
            onEdgesChange={canEdit ? onEdgesChange : undefined}
            onConnect={onConnect}
            nodesDraggable={canEdit}
            nodesConnectable={canEdit}
            onNodeClick={(_, n) => setSelectedId(n.id)}
            onPaneClick={() => setSelectedId(null)}
            onNodesDelete={(deleted) => {
              if (deleted.some((n) => n.id === selectedId)) setSelectedId(null);
            }}
            nodeTypes={nodeTypes}
            deleteKeyCode={canEdit ? ["Delete", "Backspace"] : null}
            fitView
            proOptions={{ hideAttribution: true }}
          >
            <Background variant={BackgroundVariant.Dots} gap={18} size={1} color="var(--color-border)" />
            <Controls />
            <MiniMap pannable zoomable className="!bg-surface-2" />
          </ReactFlow>
        </div>

        <div className="flex w-80 shrink-0 flex-col border-l border-border bg-surface">
          <div className="flex border-b border-border text-sm">
            <button
              onClick={() => setRightTab("config")}
              className={`flex-1 px-3 py-2 ${
                rightTab === "config" ? "border-b-2 border-primary font-medium" : "text-muted hover:bg-surface-2"
              }`}
            >
              Config
            </button>
            <button
              onClick={() => setRightTab("test")}
              className={`flex-1 px-3 py-2 ${
                rightTab === "test" ? "border-b-2 border-primary font-medium" : "text-muted hover:bg-surface-2"
              }`}
            >
              Test API
            </button>
          </div>
          <div className="min-h-0 flex-1">
            {rightTab === "config" ? (
              // A disabled fieldset turns off every input, select and button inside it at once.
              <fieldset
                disabled={!canEdit}
                title={canEdit ? "" : editReason}
                className="flex h-full min-w-0 flex-col border-0 p-0"
              >
                <NodeConfigPanel
                  def={selectedDef}
                  parameters={selectedNode?.data.parameters ?? {}}
                  onChange={(params) => selectedId && updateParams(selectedId, params)}
                  onClose={() => setSelectedId(null)}
                  onDelete={() => selectedId && deleteNode(selectedId)}
                />
              </fieldset>
            ) : (
              <TestConsole
                workflowId={workflowId}
                webhook={webhook}
                save={save}
                onViewRun={(eid) => navigate(`/executions/${eid}`)}
              />
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
