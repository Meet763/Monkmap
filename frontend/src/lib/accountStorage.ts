// Small helpers for remembering which account the user is working in.
// Kept free of imports from the API client so the client can use them without a cycle.

export const ACCOUNT_KEY = "wf_account";
export const PENDING_INVITE_KEY = "wf_pending_invite";

export function getSelectedAccountId(): string | null {
  try {
    return localStorage.getItem(ACCOUNT_KEY);
  } catch {
    return null;
  }
}

export function setSelectedAccountId(id: string | null) {
  try {
    if (id) localStorage.setItem(ACCOUNT_KEY, id);
    else localStorage.removeItem(ACCOUNT_KEY);
  } catch {
    /* storage unavailable: the user just stays on their own account */
  }
}
