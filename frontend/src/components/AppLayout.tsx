import { useEffect, useRef, useState } from "react";
import { Link, NavLink, Outlet, useNavigate } from "react-router-dom";
import { useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@/store/auth";
import { ThemeToggle } from "@/components/ThemeToggle";
import { cn } from "@/lib/cn";
import { api, apiError } from "@/api/client";
import { useAccess } from "@/lib/access";
import { PENDING_INVITE_KEY, setSelectedAccountId } from "@/lib/accountStorage";
import {
  LayoutDashboard,
  Workflow,
  History,
  Plug,
  Users,
  Settings,
  LogOut,
  Menu,
  Zap,
} from "lucide-react";

const nav = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard, end: true },
  { to: "/workflows", label: "Workflows", icon: Workflow },
  { to: "/executions", label: "Executions", icon: History },
  { to: "/integrations", label: "Integrations", icon: Plug },
  { to: "/team", label: "Team", icon: Users },
  { to: "/settings/profile", label: "Settings", icon: Settings },
];

export function AppLayout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const { accounts, current } = useAccess();
  const acceptStarted = useRef(false);

  // An invite link opened before login is saved by /invite; accept it now that we're signed in.
  useEffect(() => {
    if (acceptStarted.current) return;
    let token: string | null = null;
    try {
      token = localStorage.getItem(PENDING_INVITE_KEY);
    } catch {
      /* storage unavailable */
    }
    if (!token) return;
    acceptStarted.current = true;

    (async () => {
      try {
        const { data } = await api.post<{ accountId: string }>("/invitations/accept", { token });
        setSelectedAccountId(data.accountId);
        localStorage.removeItem(PENDING_INVITE_KEY);
        await qc.resetQueries();
        navigate("/team?accepted=1", { replace: true });
      } catch (err) {
        localStorage.removeItem(PENDING_INVITE_KEY);
        navigate(`/team?inviteError=${encodeURIComponent(apiError(err, "Could not accept the invitation."))}`, {
          replace: true,
        });
      }
    })();
  }, [navigate, qc]);

  function switchAccount(accountId: string) {
    const own = accounts.find((a) => a.isOwner)?.accountId ?? null;
    setSelectedAccountId(accountId === own ? null : accountId);
    qc.resetQueries();
  }

  const initials = (user?.displayName || user?.email || "?")
    .split(" ")
    .map((s) => s[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();

  return (
    <div className="flex h-full">
      {/* Sidebar */}
      <aside
        className={cn(
          "fixed inset-y-0 left-0 z-30 w-60 transform border-r border-border bg-surface transition-transform md:static md:translate-x-0",
          open ? "translate-x-0" : "-translate-x-full"
        )}
      >
        <div className="flex h-14 items-center gap-2 px-5">
          <Zap className="h-5 w-5 text-primary" />
          <span className="text-lg font-semibold">Monkmap</span>
        </div>
        <nav className="space-y-1 px-3 py-2">
          {nav.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              onClick={() => setOpen(false)}
              className={({ isActive }) =>
                cn(
                  "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium",
                  isActive
                    ? "bg-primary/10 text-primary"
                    : "text-muted hover:bg-surface-2 hover:text-text"
                )
              }
            >
              <item.icon className="h-[18px] w-[18px]" />
              {item.label}
            </NavLink>
          ))}
        </nav>
      </aside>

      {open && (
        <div
          className="fixed inset-0 z-20 bg-black/30 md:hidden"
          onClick={() => setOpen(false)}
        />
      )}

      {/* Main */}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 items-center justify-between border-b border-border bg-surface px-4">
          <button
            className="rounded-lg p-2 text-muted hover:bg-surface-2 md:hidden"
            onClick={() => setOpen(true)}
          >
            <Menu className="h-5 w-5" />
          </button>
          <div className="flex-1" />
          {accounts.length > 1 && (
            <select
              aria-label="Switch account"
              value={current?.accountId ?? ""}
              onChange={(e) => switchAccount(e.target.value)}
              className="mr-2 h-9 max-w-[16rem] rounded-lg border border-border bg-surface px-2 text-sm"
            >
              {accounts.map((a) => (
                <option key={a.accountId} value={a.accountId}>
                  {a.name}
                </option>
              ))}
            </select>
          )}
          <div className="flex items-center gap-2">
            <ThemeToggle />
            <div className="group relative">
              <button className="flex h-9 w-9 items-center justify-center rounded-full bg-primary text-sm font-semibold text-primary-fg">
                {initials}
              </button>
              <div className="invisible absolute right-0 top-11 z-40 w-48 rounded-lg border border-border bg-surface p-1 opacity-0 shadow-lg transition-all group-hover:visible group-hover:opacity-100">
                <div className="px-3 py-2">
                  <p className="truncate text-sm font-medium">{user?.displayName}</p>
                  <p className="truncate text-xs text-muted">{user?.email}</p>
                </div>
                <div className="my-1 h-px bg-border" />
                <Link
                  to="/settings/profile"
                  className="flex items-center gap-2 rounded-md px-3 py-2 text-sm text-text hover:bg-surface-2"
                >
                  <Settings className="h-4 w-4" /> Settings
                </Link>
                <button
                  onClick={() => {
                    logout();
                    navigate("/login");
                  }}
                  className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm text-danger hover:bg-surface-2"
                >
                  <LogOut className="h-4 w-4" /> Log out
                </button>
              </div>
            </div>
          </div>
        </header>

        <main className="min-h-0 flex-1 overflow-auto">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
