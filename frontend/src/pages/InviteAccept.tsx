import { useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Spinner } from "@/components/ui";
import { PENDING_INVITE_KEY } from "@/lib/accountStorage";

/**
 * Opened from an invite link. Saves the token, then sends the person to the app. If they are
 * not logged in yet, the login page appears first, and AppLayout accepts the invite after login.
 */
export function InviteAccept() {
  const [params] = useSearchParams();
  const navigate = useNavigate();

  useEffect(() => {
    const token = params.get("token");
    if (token) {
      try {
        localStorage.setItem(PENDING_INVITE_KEY, token);
      } catch {
        /* storage unavailable: the person will have to open the link again */
      }
    }
    navigate("/", { replace: true });
  }, [params, navigate]);

  return <Spinner />;
}
