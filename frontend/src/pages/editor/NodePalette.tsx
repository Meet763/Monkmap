import { useMemo, useState } from "react";
import type { NodeDefinition } from "@/api/types";
import * as Icons from "lucide-react";
import { Search, PanelLeft } from "lucide-react";

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
    mail: "Mail",
    calendar: "Calendar",
    table: "Table",
    "hard-drive": "HardDrive",
    box: "Box",
  };
  return (Icons[map[name] ?? "Box"] ?? Icons.Box) as Icons.LucideIcon;
}

const GROUP_ORDER = ["Triggers", "Logic", "Apps"];

/**
 * Full-height sidebar with search. Click a node to add it to the canvas.
 * Scrolls on its own, so long lists never push the page around.
 */
export function NodePalette({
  defs,
  onAdd,
}: {
  defs: NodeDefinition[];
  onAdd: (def: NodeDefinition) => void;
}) {
  const [query, setQuery] = useState("");
  const [collapsed, setCollapsed] = useState(false);

  const groups = useMemo(() => {
    const q = query.trim().toLowerCase();
    const matches = defs.filter(
      (d) => !q || d.displayName.toLowerCase().includes(q) || d.group.toLowerCase().includes(q)
    );
    const byGroup = matches.reduce<Record<string, NodeDefinition[]>>((acc, d) => {
      (acc[d.group] ??= []).push(d);
      return acc;
    }, {});
    const rank = (g: string) => (GROUP_ORDER.indexOf(g) === -1 ? 99 : GROUP_ORDER.indexOf(g));
    return Object.entries(byGroup).sort(([a], [b]) => rank(a) - rank(b) || a.localeCompare(b));
  }, [defs, query]);

  if (collapsed) {
    return (
      <div className="flex h-full w-12 shrink-0 flex-col items-center border-r border-border bg-surface py-3">
        <button
          onClick={() => setCollapsed(false)}
          className="rounded-md p-2 text-muted hover:bg-surface-2 hover:text-text"
          title="Show nodes"
        >
          <PanelLeft className="h-4 w-4" />
        </button>
      </div>
    );
  }

  return (
    <aside className="flex h-full w-64 shrink-0 flex-col border-r border-border bg-surface">
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <div>
          <h2 className="text-sm font-semibold">Nodes</h2>
          <p className="text-xs text-muted">Click a node to add it</p>
        </div>
        <button
          onClick={() => setCollapsed(true)}
          className="rounded-md p-1.5 text-muted hover:bg-surface-2 hover:text-text"
          title="Hide nodes"
        >
          <PanelLeft className="h-4 w-4" />
        </button>
      </div>

      <div className="border-b border-border p-3">
        <div className="relative">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search nodes…"
            className="h-9 w-full rounded-lg border border-border bg-surface-2 pl-8 pr-3 text-sm placeholder:text-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          />
        </div>
      </div>

      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto p-3">
        {groups.length === 0 && <p className="px-1 text-sm text-muted">No nodes match “{query}”.</p>}

        {groups.map(([group, items]) => (
          <section key={group}>
            <div className="mb-2 flex items-center justify-between px-1">
              <h3 className="text-[11px] font-semibold uppercase tracking-wide text-muted">{group}</h3>
              <span className="text-[11px] text-muted">{items.length}</span>
            </div>
            <div className="space-y-1.5">
              {items.map((def) => {
                const Icon = iconFor(def.icon);
                return (
                  <button
                    key={def.type}
                    onClick={() => onAdd(def)}
                    title={`Add ${def.displayName}`}
                    className="group flex w-full items-center gap-3 rounded-lg border border-border bg-surface px-2.5 py-2 text-left text-sm transition-colors hover:border-primary/50 hover:bg-surface-2"
                  >
                    <span
                      className={
                        def.isTrigger
                          ? "flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-success/15 text-success"
                          : "flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-primary/15 text-primary"
                      }
                    >
                      <Icon className="h-4 w-4" />
                    </span>
                    <span className="min-w-0 flex-1 truncate font-medium">{def.displayName}</span>
                    <span className="text-xs text-muted opacity-0 group-hover:opacity-100">+</span>
                  </button>
                );
              })}
            </div>
          </section>
        ))}
      </div>
    </aside>
  );
}
