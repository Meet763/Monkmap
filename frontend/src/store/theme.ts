import { create } from "zustand";

export type ThemeMode = "light" | "dark" | "system";

function apply(mode: ThemeMode) {
  const dark =
    mode === "dark" ||
    (mode === "system" && window.matchMedia("(prefers-color-scheme: dark)").matches);
  document.documentElement.classList.toggle("dark", dark);
  try {
    localStorage.setItem("theme", mode);
  } catch {
    /* ignore */
  }
}

interface ThemeState {
  mode: ThemeMode;
  setMode: (mode: ThemeMode) => void;
  cycle: () => void;
}

export const useTheme = create<ThemeState>((set, get) => ({
  mode: (localStorage.getItem("theme") as ThemeMode) || "system",
  setMode: (mode) => {
    apply(mode);
    set({ mode });
  },
  cycle: () => {
    const order: ThemeMode[] = ["light", "dark", "system"];
    const next = order[(order.indexOf(get().mode) + 1) % order.length];
    apply(next);
    set({ mode: next });
  },
}));
