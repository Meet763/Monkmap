import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, apiError } from "@/api/client";
import type { InvitationRow, MemberRow } from "@/api/types";
import { Badge, Button, Card, Input, Label, Spinner } from "@/components/ui";
import {
  NO_ACCESS,
  denyReason,
  useAccess,
  usePermissionCatalog,
} from "@/lib/access";
import { Check, Copy, Trash2, UserPlus, Users } from "lucide-react";

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

/** Checkboxes for choosing permissions. Disabled ones are shown but cannot be changed. */
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
                onChange(
                  e.target.checked ? [...selected, key] : selected.filter((p) => p !== key)
                )
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
  const [params, setParams] = useSearchParams();
  const [banner, setBanner] = useState<{ ok: boolean; text: string } | null>(() => {
    if (params.get("accepted")) return { ok: true, text: "You joined the workspace." };
    if (params.get("inviteError")) return { ok: false, text: params.get("inviteError") ?? "" };
    return null;
  });

  const canManage = access.has("members.manage");
  const manageReason = denyReason(canManage);

  const { data: members, isLoading } = useQuery({
    queryKey: ["members", access.current?.accountId],
    queryFn: async () => (await api.get<MemberRow[]>("/account/members")).data,
  });

  const { data: invitations } = useQuery({
    queryKey: ["invitations", access.current?.accountId, canManage],
    queryFn: async () => (await api.get<InvitationRow[]>("/account/invitations")).data,
    enabled: canManage,
  });

  // Invite form
  const [email, setEmail] = useState("");
  const catalog = usePermissionCatalog();
  const [preset, setPreset] = useState("Viewer");
  const [customPerms, setCustomPerms] = useState<string[]>([]);
  const [inviteLink, setInviteLink] = useState<{ email: string; link: string } | null>(null);
  const [copied, setCopied] = useState(false);

  const invite = useMutation({
    mutationFn: async () => {
      const body =
        preset === "Custom"
          ? { email, permissions: customPerms }
          : { email, preset };
      return (await api.post<{ email: string; inviteLink: string }>("/account/invitations", body)).data;
    },
    onSuccess: (data) => {
      setInviteLink({ email: data.email, link: data.inviteLink });
      setEmail("");
      setCopied(false);
      setBanner(null);
      qc.invalidateQueries({ queryKey: ["invitations"] });
    },
    onError: (err) => setBanner({ ok: false, text: apiError(err, "Could not create the invitation.") }),
  });

  const revoke = useMutation({
    mutationFn: async (id: string) => api.delete(`/account/invitations/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["invitations"] }),
  });

  // Member editing
  const [editing, setEditing] = useState<{ userId: string; permissions: string[] } | null>(null);

  const saveMember = useMutation({
    mutationFn: async (m: { userId: string; permissions: string[] }) =>
      api.put(`/account/members/${m.userId}`, { permissions: m.permissions }),
    onSuccess: () => {
      setEditing(null);
      qc.invalidateQueries({ queryKey: ["members"] });
    },
    onError: (err) => setBanner({ ok: false, text: apiError(err, "Could not save the permissions.") }),
  });

  const removeMember = useMutation({
    mutationFn: async (userId: string) => api.delete(`/account/members/${userId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["members"] }),
    onError: (err) => setBanner({ ok: false, text: apiError(err, "Could not remove the member.") }),
  });

  function dismissBanner() {
    setBanner(null);
    if (params.get("accepted") || params.get("inviteError")) setParams({}, { replace: true });
  }

  const inviteChoice = preset === "Custom" ? customPerms : catalog.presetPermissions(preset);
  const canSendInvite = canManage && email.trim().length > 0 && inviteChoice.length > 0;

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Team</h1>
        <p className="text-sm text-muted">
          Invite people into this workspace and choose what each one can do.
        </p>
      </div>

      {banner && (
        <div
          className={`flex items-center justify-between rounded-lg border px-4 py-2 text-sm ${
            banner.ok ? "border-success/40 text-success" : "border-danger/40 text-danger"
          }`}
        >
          <span>{banner.text}</span>
          <button onClick={dismissBanner} className="text-muted hover:text-text">
            Dismiss
          </button>
        </div>
      )}

      {/* Invite */}
      <Card>
        <div className="mb-4 flex items-center gap-2">
          <UserPlus className="h-4 w-4 text-primary" />
          <h2 className="font-medium">Invite someone</h2>
        </div>
        <div className="grid gap-4 md:grid-cols-2">
          <div>
            <Label>Email</Label>
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
                  // Start the custom list from the preset the user was on.
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
            <PermissionPicker
              selected={customPerms}
              onChange={setCustomPerms}
              canGrant={(p) => access.has(p)}
            />
          </div>
        )}

        <div className="mt-4 flex items-center gap-3">
          <span title={canSendInvite ? "" : manageReason || "Enter an email and choose permissions"}>
            <Button
              onClick={() => invite.mutate()}
              disabled={!canSendInvite}
              loading={invite.isPending}
            >
              <UserPlus className="h-4 w-4" /> Create invite link
            </Button>
          </span>
        </div>

        {inviteLink && (
          <div className="mt-4 rounded-lg border border-border bg-surface-2 p-3">
            <p className="mb-2 text-sm">
              Send this link to <span className="font-medium">{inviteLink.email}</span>. It works for
              7 days and is shown only once.
            </p>
            <div className="flex items-center gap-2">
              <code className="flex-1 truncate rounded bg-surface px-2 py-1.5 text-xs">{inviteLink.link}</code>
              <button
                onClick={() => {
                  navigator.clipboard?.writeText(inviteLink.link);
                  setCopied(true);
                }}
                className="rounded-md p-1.5 text-muted hover:bg-surface"
                title="Copy link"
              >
                {copied ? <Check className="h-4 w-4 text-success" /> : <Copy className="h-4 w-4" />}
              </button>
            </div>
          </div>
        )}
      </Card>

      {/* Pending invitations */}
      {canManage && (
        <Card>
          <h2 className="mb-3 font-medium">Pending invitations</h2>
          {!invitations || invitations.length === 0 ? (
            <p className="text-sm text-muted">No pending invitations.</p>
          ) : (
            <div className="divide-y divide-border">
              {invitations.map((inv) => (
                <div key={inv.id} className="flex items-center justify-between gap-4 py-3">
                  <div className="min-w-0">
                    <div className="truncate text-sm font-medium">{inv.email}</div>
                    <div className="mt-1">
                      <PermissionChips permissions={inv.permissions} />
                    </div>
                  </div>
                  <Button variant="ghost" size="sm" onClick={() => revoke.mutate(inv.id)}>
                    Revoke
                  </Button>
                </div>
              ))}
            </div>
          )}
        </Card>
      )}

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
                          onClick={() => {
                            if (confirm(`Remove ${m.name} from this workspace?`)) removeMember.mutate(m.userId);
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
