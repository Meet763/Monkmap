import { useTheme } from "@/store/theme";
import { Monitor, Moon, Sun } from "lucide-react";

export function ThemeToggle() {
  const { mode, cycle } = useTheme();
  const Icon = mode === "light" ? Sun : mode === "dark" ? Moon : Monitor;
  return (
    <button
      onClick={cycle}
      title={`Theme: ${mode} (click to change)`}
      className="flex h-9 w-9 items-center justify-center rounded-lg text-muted hover:bg-surface-2 hover:text-text"
    >
      <Icon className="h-[18px] w-[18px]" />
    </button>
  );
}
