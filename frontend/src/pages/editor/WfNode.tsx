import { Handle, Position, useReactFlow, type NodeProps } from "@xyflow/react";
import * as Icons from "lucide-react";
import { cn } from "@/lib/cn";

export type NodeStatus = "idle" | "running" | "Succeeded" | "Failed" | "Skipped";

export interface WfNodeData {
  nodeType: string;
  label: string;
  icon: string;
  isTrigger: boolean;
  outputPorts: string[];
  parameters: Record<string, unknown>;
  status: NodeStatus;
  [key: string]: unknown;
}

function iconFor(name: string) {
  const map: Record<string, keyof typeof Icons> = {
    play: "Play",
    pencil: "Pencil",
    globe: "Globe",
    "git-branch": "GitBranch",
    database: "Database",
    sparkles: "Sparkles",
    webhook: "Webhook",
    plug: "Plug",
    box: "Box",
  };
  const key = map[name] ?? "Box";
  return (Icons[key] ?? Icons.Box) as Icons.LucideIcon;
}

export function WfNode({ id, data, selected }: NodeProps) {
  const d = data as WfNodeData;
  const Icon = iconFor(d.icon);
  const { deleteElements } = useReactFlow();

  const statusRing =
    d.status === "running"
      ? "border-primary animate-pulse"
      : d.status === "Succeeded"
      ? "border-success"
      : d.status === "Failed"
      ? "border-danger"
      : selected
      ? "border-primary"
      : "border-border";

  const ports = d.outputPorts;
  const multi = ports.length > 1;

  return (
    <div
      className={cn(
        "group relative min-w-[200px] rounded-xl border-2 bg-surface px-3 py-2.5 shadow-sm",
        statusRing
      )}
    >
      {/* Delete button (shows on hover or when selected) */}
      <button
        title="Delete node"
        className={cn(
          "nodrag absolute -right-2 -top-2 z-10 h-5 w-5 items-center justify-center rounded-full bg-danger text-white shadow hover:opacity-90",
          selected ? "flex" : "hidden group-hover:flex"
        )}
        onMouseDown={(e) => e.stopPropagation()}
        onClick={(e) => {
          e.stopPropagation();
          deleteElements({ nodes: [{ id }] });
        }}
      >
        <Icons.X className="h-3 w-3" />
      </button>

      {!d.isTrigger && <Handle type="target" position={Position.Left} id="main" />}

      {/* Header */}
      <div className="flex items-center gap-2.5">
        <div
          className={cn(
            "flex h-8 w-8 items-center justify-center rounded-lg",
            d.isTrigger ? "bg-success/15 text-success" : "bg-primary/15 text-primary"
          )}
        >
          <Icon className="h-[18px] w-[18px]" />
        </div>
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-text">{d.label}</p>
          <p className="truncate text-[11px] text-muted">{d.nodeType}</p>
        </div>
      </div>

      {multi ? (
        /* Multiple outputs: distributed evenly down the right edge, centered, no divider */
        ports.map((port, i) => {
          const top = `${((i + 1) / (ports.length + 1)) * 100}%`;
          return (
            <div key={port}>
              <span
                className="absolute right-4 text-[11px] font-medium leading-none text-muted"
                style={{ top, transform: "translateY(-50%)" }}
              >
                {port}
              </span>
              <Handle type="source" position={Position.Right} id={port} style={{ top }} />
            </div>
          );
        })
      ) : (
        <Handle type="source" position={Position.Right} id={ports[0] ?? "main"} />
      )}
    </div>
  );
}
