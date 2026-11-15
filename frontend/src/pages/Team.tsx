import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, apiError } from "@/api/client";
import type { MemberRow } from "@/api/types";
import { Badge, Button, Card, Input, Label, Spinner } from "@/components/ui";
import { NO_ACCESS, denyReason, useAccess, usePermissionCatalog } from "@/lib/access";
import { useDialog } from "@/components/ConfirmDialog";
import { Eye, EyeOff, Trash2, UserPlus, Users } from "lucide-react";

function PermissionChips({ permissions }: { permissions: string[] }) {
  const { labelFor } = usePermissionCatalog();
  if (permissions.length === 0) return <span className="text-xs text-muted">No permissions</span>;
  return (
    <div className="flex flex-wrap gap-1">
      {permissions.map((p) => (
        <span key={p} className="rounded bg-surface-2 px-1.5 py-0.5 text-[11px] text-muted">
          {labelFor(p)}
        </span>
      ))}
    </div>
  );
}

/** Checkboxes for choosing permissions. Ones the user cannot give are shown but disabled. */
function PermissionPicker({
  selected,
  onChange,
  canGrant,
}: {
  selected: string[];
  onChange: (next: string[]) => void;
  canGrant: (permission: string) => boolean;
}) {
  const { permissions } = usePermissionCatalog();
  return (
    <div className="grid gap-2 sm:grid-cols-2">
      {permissions.map(({ key, label, description }) => {
        const allowed = canGrant(key);
        return (
          <label
            key={key}
            title={allowed ? description : "You can't give a permission you don't have"}
            className={`flex items-center gap-2 text-sm ${allowed ? "" : "opacity-50"}`}
          >
            <input
              type="checkbox"
              disabled={!allowed}
              checked={selected.includes(key)}
              onChange={(e) =>
                onChange(e.target.checked ? [...selected, key] : selected.filter((p) => p !== key))
              }
            />
            {label}
          </label>
        );
      })}
    </div>
  );
}

export function Team() {
  const qc = useQueryClient();
  const access = useAccess();
  const catalog = usePermissionCatalog();

  const { confirm } = useDialog();
  const canManage = access.has("members.manage");
  const manageReason = denyReason(canManage);

  const [banner, setBanner] = useState<{ ok: boolean; text: string } | null>(null);

  const { data: members, isLoading } = useQuery({
    queryKey: ["members", access.current?.accountId],
    queryFn: async () => (await api.get<MemberRow[]>("/account/members")).data,
  });

  // ---- Add member form ----
  const [email, setEmail] = useState("");
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [preset, setPreset] = useState("Viewer");
  const [customPerms, setCustomPerms] = useState<string[]>([]);

  const choice = preset === "Custom" ? customPerms : catalog.presetPermissions(preset);
  const canSubmit =
    canManage && email.trim().length > 0 && choice.length > 0 && password.length >= 6;

  const addMember = useMutation({
    mutationFn: async () => {
      const body = {
        email: email.trim(),
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        password,
        ...(preset === "Custom" ? { permissions: customPerms } : { preset }),
      };
      return (await api.post<{ message: string }>("/account/members", body)).data;
    },
    onSuccess: (data) => {
      setBanner({ ok: true, text: data.message });
      setEmail("");
      setFirstName("");
      setLastName("");
      setPassword("");
      qc.invalidateQueries({ queryKey: ["members"] });
    },
    onError: (err) => setBanner({ ok: false, text: apiError(err, "Could not add this person.") }),
  });

  // ---- Edit / remove members ----
  const [editing, setEditing] = useState<{ userId: string; permissions: string[] } | null>(null);

  const saveMember = useMutation({
    mutationFn: async (m: { userId: string; permissions: string[] }) =>
      api.put(`/account/members/${m.userId}`, { permissions: m.permissions }),
    onSuccess: () => {
      setEditing(null);
      setBanner({ ok: true, text: "Permissions updated." });
      qc.invalidateQueries({ queryKey: ["members"] });
    },
    onError: (err) => setBanner({ ok: false, text: apiError(err, "Could not save the permissions.") }),
  });

  const removeMember = useMutation({
    mutationFn: async (userId: string) => api.delete(`/account/members/${userId}`),
    onSuccess: () => {
      setBanner({ ok: true, text: "Member removed. They no longer have access to this workspace." });
      qc.invalidateQueries({ queryKey: ["members"] });
    },
    onError: (err) => setBanner({ ok: false, text: apiError(err, "Could not remove the member.") }),
  });

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Team</h1>
        <p className="text-sm text-muted">
          Add people to this workspace with an email and password, and choose what each one can do.
        </p>
      </div>

      {banner && (
        <div
          className={`flex items-center justify-between rounded-lg border px-4 py-2 text-sm ${
            banner.ok ? "border-success/40 text-success" : "border-danger/40 text-danger"
          }`}
        >
          <span>{banner.text}</span>
          <button onClick={() => setBanner(null)} className="text-muted hover:text-text">
            Dismiss
          </button>
        </div>
      )}

      {/* Add member */}
      <Card>
        <div className="mb-4 flex items-center gap-2">
          <UserPlus className="h-4 w-4 text-primary" />
          <h2 className="font-medium">Add a member</h2>
        </div>

        <div className="grid gap-4 md:grid-cols-2">
          <div>
            <Label>Email (their login)</Label>
            <Input
              type="email"
              placeholder="colleague@company.com"
              value={email}
              disabled={!canManage}
              title={manageReason}
              onChange={(e) => setEmail(e.target.value)}
            />
          </div>
          <div>
            <Label>Password (at least 6 characters)</Label>
            <div className="relative">
              <Input
                type={showPassword ? "text" : "password"}
                autoComplete="new-password"
                placeholder="Set a password for them"
                value={password}
                disabled={!canManage}
                title={manageReason}
                onChange={(e) => setPassword(e.target.value)}
                className="pr-10"
              />
              <button
                type="button"
                onClick={() => setShowPassword((s) => !s)}
                className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-muted hover:text-text"
                title={showPassword ? "Hide password" : "Show password"}
              >
                {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
          </div>
          <div>
            <Label>First name</Label>
            <Input
              placeholder="Used if this is a new login"
              value={firstName}
              disabled={!canManage}
              title={manageReason}
              onChange={(e) => setFirstName(e.target.value)}
            />
          </div>
          <div>
            <Label>Last name</Label>
            <Input
              value={lastName}
              disabled={!canManage}
              title={manageReason}
              onChange={(e) => setLastName(e.target.value)}
            />
          </div>
          <div className="md:col-span-2">
            <Label>Permission preset</Label>
            <select
              className="h-10 w-full rounded-lg border border-border bg-surface px-3 text-sm"
              value={preset}
              disabled={!canManage}
              title={manageReason}
              onChange={(e) => {
                const next = e.target.value;
                setPreset(next);
                if (next === "Custom") {
                  if (customPerms.length === 0) setCustomPerms(catalog.presetPermissions(preset));
                } else {
                  setCustomPerms(catalog.presetPermissions(next));
                }
              }}
            >
              {catalog.presets.map((p) => (
                <option key={p.name} value={p.name}>
                  {p.name} — {p.description}
                </option>
              ))}
              <option value="Custom">Custom — pick each permission</option>
            </select>
          </div>
        </div>

        {preset === "Custom" && (
          <div className="mt-4">
            <Label>Permissions</Label>
            <PermissionPicker selected={customPerms} onChange={setCustomPerms} canGrant={(p) => access.has(p)} />
          </div>
        )}

        <p className="mt-4 text-xs text-muted">
          If this email already has a login, they are added to your workspace and keep their existing
          password. The password you type is used only for a new login.
        </p>

        <div className="mt-4">
          <span
            title={
              canSubmit
                ? ""
                : manageReason ||
                  (password.length > 0 && password.length < 6
                    ? "Password must be at least 6 characters"
                    : "Enter an email, a password, and choose permissions")
            }
          >
            <Button onClick={() => addMember.mutate()} disabled={!canSubmit} loading={addMember.isPending}>
              <UserPlus className="h-4 w-4" /> Add member
            </Button>
          </span>
        </div>
      </Card>

      {/* Members */}
      <Card>
        <div className="mb-3 flex items-center gap-2">
          <Users className="h-4 w-4 text-primary" />
          <h2 className="font-medium">Members</h2>
        </div>
        {isLoading ? (
          <Spinner />
        ) : (
          <div className="divide-y divide-border">
            {(members ?? []).map((m) => {
              const isEditing = editing?.userId === m.userId;
              return (
                <div key={m.userId} className="py-3">
                  <div className="flex items-center justify-between gap-4">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="truncate font-medium">{m.name}</span>
                        {m.isOwner && <Badge tone="primary">Owner</Badge>}
                      </div>
                      <div className="truncate text-xs text-muted">{m.email}</div>
                      {!isEditing && (
                        <div className="mt-1">
                          <PermissionChips permissions={m.permissions} />
                        </div>
                      )}
                    </div>
                    {!m.isOwner && canManage && !isEditing && (
                      <div className="flex gap-2">
                        <Button
                          variant="secondary"
                          size="sm"
                          onClick={() => setEditing({ userId: m.userId, permissions: m.permissions })}
                        >
                          Edit
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          loading={removeMember.isPending && removeMember.variables === m.userId}
                          onClick={async () => {
                            const ok = await confirm({
                              title: `Remove ${m.name}?`,
                              message: `${m.email} will lose access to this workspace. Their own account and login stay.`,
                              confirmText: "Remove member",
                              danger: true,
                            });
                            if (ok) removeMember.mutate(m.userId);
                          }}
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </div>
                    )}
                  </div>

                  {isEditing && editing && (
                    <div className="mt-3 space-y-3">
                      <PermissionPicker
                        selected={editing.permissions}
                        onChange={(next) => setEditing({ userId: m.userId, permissions: next })}
                        canGrant={(p) => access.has(p)}
                      />
                      <div className="flex gap-2">
                        <Button
                          size="sm"
                          loading={saveMember.isPending}
                          disabled={editing.permissions.length === 0}
                          onClick={() => saveMember.mutate(editing)}
                        >
                          Save permissions
                        </Button>
                        <Button variant="ghost" size="sm" onClick={() => setEditing(null)}>
                          Cancel
                        </Button>
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
        {!canManage && (
          <p className="mt-3 text-xs text-muted" title={NO_ACCESS}>
            Only people with “Manage team” permission can change members.
          </p>
        )}
      </Card>
    </div>
  );
}
