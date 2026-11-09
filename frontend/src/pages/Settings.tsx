import { useState } from "react";
import { NavLink, Outlet } from "react-router-dom";
import { cn } from "@/lib/cn";
import { useAuth, apiError } from "@/store/auth";
import { useTheme, type ThemeMode } from "@/store/theme";
import { Button, Card, Input, Label } from "@/components/ui";

const tabs = [
  { to: "/settings/profile", label: "Profile" },
  { to: "/settings/appearance", label: "Appearance" },
  { to: "/settings/connections", label: "Connections" },
];

export function SettingsLayout() {
  return (
    <div className="mx-auto max-w-3xl p-6">
      <h1 className="mb-5 text-2xl font-semibold">Settings</h1>
      <div className="mb-6 flex gap-1 border-b border-border">
        {tabs.map((t) => (
          <NavLink
            key={t.to}
            to={t.to}
            className={({ isActive }) =>
              cn(
                "px-4 py-2 text-sm font-medium",
                isActive
                  ? "border-b-2 border-primary text-primary"
                  : "text-muted hover:text-text"
              )
            }
          >
            {t.label}
          </NavLink>
        ))}
      </div>
      <Outlet />
    </div>
  );
}

export function ProfileSettings() {
  const { user, updateProfile } = useAuth();
  const [firstName, setFirstName] = useState(user?.firstName ?? "");
  const [lastName, setLastName] = useState(user?.lastName ?? "");
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);

  async function save() {
    setSaving(true);
    setMsg(null);
    setErr(null);
    try {
      await updateProfile(firstName, lastName, user?.avatarUrl ?? null);
      setMsg("Profile updated.");
    } catch (e) {
      setErr(apiError(e));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card className="space-y-4">
      <div className="grid grid-cols-2 gap-3">
        <div>
          <Label>First name</Label>
          <Input value={firstName} onChange={(e) => setFirstName(e.target.value)} />
        </div>
        <div>
          <Label>Last name</Label>
          <Input value={lastName} onChange={(e) => setLastName(e.target.value)} />
        </div>
      </div>
      <div>
        <Label>Email</Label>
        <Input value={user?.email ?? ""} disabled />
        <p className="mt-1 text-xs text-muted">Email changes aren't supported in this MVP.</p>
      </div>
      {msg && <p className="text-sm text-success">{msg}</p>}
      {err && <p className="text-sm text-danger">{err}</p>}
      <Button onClick={save} loading={saving}>
        Save changes
      </Button>
    </Card>
  );
}

export function AppearanceSettings() {
  const { mode, setMode } = useTheme();
  const options: { value: ThemeMode; label: string; hint: string }[] = [
    { value: "light", label: "Light", hint: "Always light" },
    { value: "dark", label: "Dark", hint: "Always dark" },
    { value: "system", label: "System", hint: "Match your OS" },
  ];
  return (
    <Card>
      <Label>Theme</Label>
      <div className="mt-2 grid gap-3 sm:grid-cols-3">
        {options.map((o) => (
          <button
            key={o.value}
            onClick={() => setMode(o.value)}
            className={cn(
              "rounded-xl border p-4 text-left transition-colors",
              mode === o.value
                ? "border-primary bg-primary/5"
                : "border-border hover:bg-surface-2"
            )}
          >
            <p className="font-medium">{o.label}</p>
            <p className="text-xs text-muted">{o.hint}</p>
          </button>
        ))}
      </div>
    </Card>
  );
}

export function ConnectionsSettings() {
  return (
    <Card className="py-10 text-center">
      <p className="text-muted">
        OAuth app connections (Google, Slack, …) arrive in the Integrations phase of the roadmap.
      </p>
    </Card>
  );
}
