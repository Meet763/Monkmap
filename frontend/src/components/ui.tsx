import { cn } from "@/lib/cn";
import { Loader2 } from "lucide-react";
import React from "react";

type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "ghost" | "danger";
  size?: "sm" | "md";
  loading?: boolean;
};

export function Button({
  variant = "primary",
  size = "md",
  loading,
  className,
  children,
  disabled,
  ...rest
}: ButtonProps) {
  const base =
    "inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/50";
  const sizes = { sm: "h-8 px-3 text-sm", md: "h-10 px-4 text-sm" };
  const variants = {
    primary: "bg-primary text-primary-fg hover:opacity-90",
    secondary: "bg-surface-2 text-text border border-border hover:bg-surface",
    ghost: "text-text hover:bg-surface-2",
    danger: "bg-danger text-white hover:opacity-90",
  };
  return (
    <button
      className={cn(base, sizes[size], variants[variant], className)}
      disabled={disabled || loading}
      {...rest}
    >
      {loading && <Loader2 className="h-4 w-4 animate-spin" />}
      {children}
    </button>
  );
}

export function Input({ className, ...rest }: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={cn(
        "h-10 w-full rounded-lg border border-border bg-surface px-3 text-sm text-text placeholder:text-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
        className
      )}
      {...rest}
    />
  );
}

export function Textarea({
  className,
  ...rest
}: React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      className={cn(
        "w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm font-mono text-text placeholder:text-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
        className
      )}
      {...rest}
    />
  );
}

export function Select({
  className,
  children,
  ...rest
}: React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      className={cn(
        "h-10 w-full rounded-lg border border-border bg-surface px-3 text-sm text-text focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
        className
      )}
      {...rest}
    >
      {children}
    </select>
  );
}

export function Label({ className, ...rest }: React.LabelHTMLAttributes<HTMLLabelElement>) {
  return <label className={cn("mb-1.5 block text-sm font-medium text-text", className)} {...rest} />;
}

export function Card({ className, ...rest }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn("rounded-xl border border-border bg-surface p-5 shadow-sm", className)}
      {...rest}
    />
  );
}

export function Badge({
  children,
  tone = "muted",
}: {
  children: React.ReactNode;
  tone?: "muted" | "success" | "danger" | "primary" | "running";
}) {
  const tones = {
    muted: "bg-surface-2 text-muted",
    success: "bg-success/15 text-success",
    danger: "bg-danger/15 text-danger",
    primary: "bg-primary/15 text-primary",
    running: "bg-primary/15 text-primary",
  };
  return (
    <span className={cn("rounded-full px-2.5 py-0.5 text-xs font-medium", tones[tone])}>
      {children}
    </span>
  );
}

export function Spinner() {
  return (
    <div className="flex items-center justify-center p-10 text-muted">
      <Loader2 className="h-6 w-6 animate-spin" />
    </div>
  );
}
