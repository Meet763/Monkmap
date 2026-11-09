import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import { getSelectedAccountId } from "@/lib/accountStorage";

export const NO_ACCESS = "You have no access";
export const LOCKED_REASON = "Locked — unlock it to make changes";

export interface PermissionInfo {
  key: string;
  label: string;
  description: string;
}

export interface PresetInfo {
  name: string;
  description: string;
  permissions: string[];
}

interface Catalog {
  permissions: PermissionInfo[];
  presets: PresetInfo[];
}

/**
 * Permissions and presets as stored in the database. Changing them there shows up here on the
 * next load, so the UI never hard-codes what a preset contains.
 */
export function usePermissionCatalog() {
  const { data } = useQuery({
    queryKey: ["permissions"],
    queryFn: async () => (await api.get<Catalog>("/permissions")).data,
    staleTime: 60_000,
  });

  const permissions = data?.permissions ?? [];
  const presets = data?.presets ?? [];
  return {
    permissions,
    presets,
    labelFor: (key: string) => permissions.find((p) => p.key === key)?.label ?? key,
    presetPermissions: (name: string) => presets.find((p) => p.name === name)?.permissions ?? [],
  };
}

export interface AccountOption {
  accountId: string;
  name: string;
  isOwner: boolean;
  permissions: string[];
}

/**
 * What the current user may do in the account they are working in.
 * While the list is loading, nothing is allowed, so controls start disabled and never flash on.
 */
export function useAccess() {
  const { data: accounts = [] } = useQuery({
    queryKey: ["accounts"],
    queryFn: async () => (await api.get<AccountOption[]>("/accounts")).data,
  });

  const selectedId = getSelectedAccountId();
  const current =
    accounts.find((a) => a.accountId === selectedId) ?? accounts.find((a) => a.isOwner) ?? null;
  const permissions = new Set(current?.permissions ?? []);

  return {
    accounts,
    current,
    isOwner: current?.isOwner ?? false,
    has: (permission: string) => permissions.has(permission),
  };
}

/** Tooltip text for a control the user cannot use. Empty string means it is allowed. */
export function denyReason(allowed: boolean, extra?: string): string {
  if (!allowed) return NO_ACCESS;
  return extra ?? "";
}
