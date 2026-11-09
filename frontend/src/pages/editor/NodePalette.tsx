import type { NodeDefinition } from "@/api/types";
import * as Icons from "lucide-react";

function iconFor(name: string): Icons.LucideIcon {
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
  return (Icons[map[name] ?? "Box"] ?? Icons.Box) as Icons.LucideIcon;
}

export function NodePalette({
  defs,
  onAdd,
}: {
  defs: NodeDefinition[];
  onAdd: (def: NodeDefinition) => void;
}) {
  const groups = defs.reduce<Record<string, NodeDefinition[]>>((acc, d) => {
    (acc[d.group] ??= []).push(d);
    return acc;
  }, {});

  return (
    <div className="w-56 shrink-0 overflow-auto border-r border-border bg-surface p-3">
      <h3 className="mb-2 px-1 text-xs font-semibold uppercase tracking-wide text-muted">
        Nodes
      </h3>
      {Object.entries(groups).map(([group, items]) => (
        <div key={group} className="mb-4">
          <p className="mb-1.5 px-1 text-[11px] font-medium text-muted">{group}</p>
          <div className="space-y-1">
            {items.map((def) => {
              const Icon = iconFor(def.icon);
              return (
                <button
                  key={def.type}
                  onClick={() => onAdd(def)}
                  className="flex w-full items-center gap-2.5 rounded-lg border border-border bg-surface px-2.5 py-2 text-left text-sm hover:border-primary/50 hover:bg-surface-2"
                >
                  <span
                    className={
                      def.isTrigger
                        ? "flex h-7 w-7 items-center justify-center rounded-md bg-success/15 text-success"
                        : "flex h-7 w-7 items-center justify-center rounded-md bg-primary/15 text-primary"
                    }
                  >
                    <Icon className="h-4 w-4" />
                  </span>
                  <span className="truncate">{def.displayName}</span>
                </button>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}
