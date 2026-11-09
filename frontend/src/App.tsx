import { useEffect, type ReactNode } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { useAuth } from "@/store/auth";
import { AppLayout } from "@/components/AppLayout";
import { Login } from "@/pages/Login";
import { Register } from "@/pages/Register";
import { Dashboard } from "@/pages/Dashboard";
import { Workflows } from "@/pages/Workflows";
import { Editor } from "@/pages/editor/Editor";
import { Executions } from "@/pages/Executions";
import { ExecutionDetail } from "@/pages/ExecutionDetail";
import { Integrations } from "@/pages/Integrations";
import { Team } from "@/pages/Team";
import { InviteAccept } from "@/pages/InviteAccept";
import {
  SettingsLayout,
  ProfileSettings,
  AppearanceSettings,
  ConnectionsSettings,
} from "@/pages/Settings";

function RequireAuth({ children }: { children: ReactNode }) {
  const token = useAuth((s) => s.token);
  if (!token) return <Navigate to="/login" replace />;
  return <>{children}</>;
}

export default function App() {
  const { token, loadMe } = useAuth();

  useEffect(() => {
    if (token) loadMe();
  }, [token, loadMe]);

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route path="/invite" element={<InviteAccept />} />

        <Route
          element={
            <RequireAuth>
              <AppLayout />
            </RequireAuth>
          }
        >
          <Route path="/" element={<Dashboard />} />
          <Route path="/workflows" element={<Workflows />} />
          <Route path="/executions" element={<Executions />} />
          <Route path="/executions/:id" element={<ExecutionDetail />} />
          <Route path="/integrations" element={<Integrations />} />
          <Route path="/team" element={<Team />} />
          <Route path="/settings" element={<SettingsLayout />}>
            <Route index element={<Navigate to="/settings/profile" replace />} />
            <Route path="profile" element={<ProfileSettings />} />
            <Route path="appearance" element={<AppearanceSettings />} />
            <Route path="connections" element={<ConnectionsSettings />} />
          </Route>
        </Route>

        {/* Editor is full-screen (outside the standard layout but still protected) */}
        <Route
          path="/workflows/:id/editor"
          element={
            <RequireAuth>
              <div className="h-full">
                <Editor />
              </div>
            </RequireAuth>
          }
        />

        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}
