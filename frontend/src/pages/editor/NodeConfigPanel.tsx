import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { ConnectionItem, NodeDefinition, NodeParameter } from "@/api/types";
import { Button, Input, Label, Select, Textarea } from "@/components/ui";
import { Trash2, X } from "lucide-react";

interface Props {
  def: NodeDefinition | undefined;
  parameters: Record<string, unknown>;
  onChange: (params: Record<string, unknown>) => void;
  onClose: () => void;
  onDelete: () => void;
}

export function NodeConfigPanel({ def, parameters, onChange, onClose, onDelete }: Props) {
  // Connections power the "Account" picker on app nodes. Fetched once and cached; harmless for
  // nodes that have no connection field. Hook stays above the early return (rules of hooks).
  const { data: connections } = useQuery({
    queryKey: ["connections"],
    queryFn: async () => (await api.get<ConnectionItem[]>("/connections")).data,
  });

  if (!def) {
    return (
      <div className="flex h-full items-center justify-center p-6 text-center text-sm text-muted">
        Select a node to configure it.
      </div>
    );
  }

  const set = (name: string, value: unknown) => onChange({ ...parameters, [name]: value });
  const val = (name: string, fallback = "") =>
    parameters[name] !== undefined && parameters[name] !== null
      ? String(parameters[name])
      : fallback;

  // The effective current value of a parameter (stored value, else its default).
  const defaults = new Map(def.parameters.map((p) => [p.name, p.default ?? ""]));
  const current = (name: string) => val(name, defaults.get(name) ?? "");

  // n8n-style "displayOptions": a field with `visibleWhen` only shows when the controlling
  // parameter (e.g. Operation) currently holds one of the listed values.
  const visible = (p: NodeParameter) =>
    !p.visibleWhen || p.visibleWhen.values.includes(current(p.visibleWhen.param));

  // The provider for a connection field: carried in the field's placeholder (set server-side),
  // falling back to the second segment of an "app.<provider>...." node type.
  function providerFor(p: NodeParameter): string {
    return p.placeholder || def!.type.split(".")[1] || "";
  }

  function renderConnection(p: NodeParameter) {
    const provider = providerFor(p);
    const options = (connections ?? []).filter((c) => c.provider === provider);
    if (options.length === 0) {
      return (
        <p className="rounded-md border border-dashed border-border px-3 py-2 text-xs text-muted">
          No {provider || "matching"} account connected.{" "}
          <Link to="/integrations" className="text-primary hover:underline">
            Connect one
          </Link>
          , then pick it here.
        </p>
      );
    }
    return (
      <Select value={val(p.name)} onChange={(e) => set(p.name, e.target.value)}>
        <option value="">Select account…</option>
        {options.map((c) => (
          <option key={c.id} value={c.id}>
            {c.accountName}
          </option>
        ))}
      </Select>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <div>
          <h3 className="font-medium">{def.displayName}</h3>
          <p className="text-xs text-muted">{def.type}</p>
        </div>
        <button onClick={onClose} className="rounded-md p-1 text-muted hover:bg-surface-2">
          <X className="h-4 w-4" />
        </button>
      </div>

      <div className="flex-1 space-y-4 overflow-auto p-4">
        {def.parameters.length === 0 && (
          <p className="text-sm text-muted">This node has no parameters.</p>
        )}
        {def.parameters.filter(visible).map((p) => (
          <div key={p.name}>
            <Label>
              {p.displayName}
              {p.required && <span className="text-danger"> *</span>}
            </Label>

            {p.type === "connection" ? (
              renderConnection(p)
            ) : p.type === "options" ? (
              <Select
                value={val(p.name, p.default ?? "")}
                onChange={(e) => set(p.name, e.target.value)}
              >
                {(p.options ?? []).map((o, i) => (
                  <option key={o} value={o}>
                    {p.optionLabels?.[i] ?? o}
                  </option>
                ))}
              </Select>
            ) : p.type === "boolean" ? (
              <Select
                value={val(p.name, p.default ?? "false")}
                onChange={(e) => set(p.name, e.target.value)}
              >
                <option value="false">false</option>
                <option value="true">true</option>
              </Select>
            ) : p.type === "json" || p.type === "text" ? (
              <Textarea
                rows={5}
                placeholder={p.placeholder ?? ""}
                value={val(p.name, p.default ?? "")}
                onChange={(e) => set(p.name, e.target.value)}
              />
            ) : p.type === "secret" ? (
              <Input
                type="password"
                autoComplete="off"
                placeholder={p.placeholder ?? ""}
                value={val(p.name, p.default ?? "")}
                onChange={(e) => set(p.name, e.target.value)}
              />
            ) : (
              <Input
                placeholder={p.placeholder ?? ""}
                value={val(p.name, p.default ?? "")}
                onChange={(e) => set(p.name, e.target.value)}
              />
            )}
          </div>
        ))}
        <p className="pt-2 text-xs text-muted">
          Tip: reference earlier data with{" "}
          <code className="rounded bg-surface-2 px-1">{"{{$json.field}}"}</code> or{" "}
          <code className="rounded bg-surface-2 px-1">{"{{$node.NodeId.field}}"}</code>.
        </p>
      </div>

      <div className="border-t border-border p-3">
        <Button variant="danger" className="w-full" onClick={onDelete}>
          <Trash2 className="h-4 w-4" /> Delete node
        </Button>
      </div>
    </div>
  );
}
