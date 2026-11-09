import axios from "axios";
import { getSelectedAccountId, setSelectedAccountId } from "@/lib/accountStorage";

export const TOKEN_KEY = "wf_token";

export const api = axios.create({ baseURL: "/api" });

api.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_KEY);
  if (token) config.headers.Authorization = `Bearer ${token}`;

  // Work inside the selected account (only when the user switched to a shared one).
  const accountId = getSelectedAccountId();
  if (accountId) config.headers["X-Account-Id"] = accountId;
  return config;
});

api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err?.response?.status === 401 && localStorage.getItem(TOKEN_KEY)) {
      localStorage.removeItem(TOKEN_KEY);
      if (!location.pathname.startsWith("/login")) location.href = "/login";
    }
    // The selected account was removed or revoked: fall back to your own account.
    const message: string = err?.response?.data?.message ?? "";
    if (err?.response?.status === 403 && message.includes("access to this account") && getSelectedAccountId()) {
      setSelectedAccountId(null);
      location.reload();
    }
    return Promise.reject(err);
  }
);

export function apiError(err: unknown, fallback = "Something went wrong"): string {
  if (axios.isAxiosError(err)) {
    return err.response?.data?.message ?? err.message ?? fallback;
  }
  return fallback;
}
