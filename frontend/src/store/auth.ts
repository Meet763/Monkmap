import { create } from "zustand";
import { api, TOKEN_KEY, apiError } from "@/api/client";
import type { AuthResponse, User } from "@/api/types";

interface AuthState {
  user: User | null;
  token: string | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (
    email: string,
    password: string,
    firstName: string,
    lastName: string
  ) => Promise<void>;
  loadMe: () => Promise<void>;
  updateProfile: (
    firstName: string,
    lastName: string,
    avatarUrl?: string | null
  ) => Promise<void>;
  logout: () => void;
}

export const useAuth = create<AuthState>((set) => ({
  user: null,
  token: localStorage.getItem(TOKEN_KEY),
  loading: false,

  login: async (email, password) => {
    const { data } = await api.post<AuthResponse>("/auth/login", { email, password });
    localStorage.setItem(TOKEN_KEY, data.token);
    set({ token: data.token, user: data.user });
  },

  register: async (email, password, firstName, lastName) => {
    const { data } = await api.post<AuthResponse>("/auth/register", {
      email,
      password,
      firstName,
      lastName,
    });
    localStorage.setItem(TOKEN_KEY, data.token);
    set({ token: data.token, user: data.user });
  },

  loadMe: async () => {
    if (!localStorage.getItem(TOKEN_KEY)) return;
    set({ loading: true });
    try {
      const { data } = await api.get<User>("/me");
      set({ user: data });
    } catch {
      /* interceptor handles 401 */
    } finally {
      set({ loading: false });
    }
  },

  updateProfile: async (firstName, lastName, avatarUrl) => {
    const { data } = await api.put<User>("/me", { firstName, lastName, avatarUrl });
    set({ user: data });
  },

  logout: () => {
    localStorage.removeItem(TOKEN_KEY);
    set({ token: null, user: null });
  },
}));

export { apiError };
