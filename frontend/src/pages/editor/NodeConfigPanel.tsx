import type { NodeDefinition } from "@/api/types";
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
        {def.parameters.map((p) => (
          <div key={p.name}>
            <Label>
              {p.displayName}
              {p.required && <span className="text-danger"> *</span>}
            </Label>

            {p.type === "options" ? (
              <Select
                value={val(p.name, p.default ?? "")}
                onChange={(e) => set(p.name, e.target.value)}
              >
                {(p.options ?? []).map((o) => (
                  <option key={o} value={o}>
                    {o}
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
