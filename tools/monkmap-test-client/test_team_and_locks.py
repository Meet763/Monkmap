"""
Verifies team permissions, invitations and workflow locks against a running API.

Each run creates fresh users (random emails), so it can be run again and again.
Standard library only.

Run:  python test_team_and_locks.py
Env:  MONKMAP_URL (default http://localhost:5080)
Exit code 0 = every check passed.
"""

import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = os.environ.get("MONKMAP_URL", "http://localhost:5080").rstrip("/")
PASSWORD = "Passw0rd!"
results = []


def call(method, path, body=None, token=None, account=None, query=None):
    url = BASE + path
    if query:
        url += "?" + urllib.parse.urlencode(query)
    data = None
    headers = {"Accept": "application/json"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = f"Bearer {token}"
    if account:
        headers["X-Account-Id"] = account
    req = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=60) as res:
            raw, status = res.read().decode("utf-8"), res.status
    except urllib.error.HTTPError as e:
        raw, status = e.read().decode("utf-8"), e.code
    try:
        return status, json.loads(raw) if raw else None
    except json.JSONDecodeError:
        return status, raw


def check(name, condition, detail=""):
    results.append((name, bool(condition)))
    print(("PASS " if condition else "FAIL ") + name + (f"  [{detail}]" if detail and not condition else ""))


def new_user(label):
    email = f"{label}-{uuid.uuid4().hex[:8]}@test.local"
    status, body = call("POST", "/api/auth/register",
                        {"email": email, "password": PASSWORD, "firstName": label.title(), "lastName": "Test"})
    if status not in (200, 201):
        sys.exit(f"could not register {email}: {status} {body}")
    return {"email": email, "token": body["token"], "id": body["user"]["id"]}


def graph_with_webhook():
    return json.dumps({
        "nodes": [{"id": "webhook", "type": "WebhookTrigger", "position": {"x": 0, "y": 0},
                   "parameters": {"method": "POST", "path": "order", "token": "t123"}}],
        "connections": [],
    })


def token_from_link(link):
    return link.split("token=", 1)[1]


def main():
    owner = new_user("owner")
    viewer = new_user("viewer")
    editor = new_user("editor")
    stranger = new_user("stranger")
    print(f"accounts: owner={owner['email']}\n")

    # ---- Owner creates a workflow with a webhook node ----
    s, wf = call("POST", "/api/workflows", {"name": "Orders", "description": "test"}, owner["token"])
    check("owner can create a workflow", s == 200, f"{s} {wf}")
    wid = wf["id"]
    s, _ = call("PUT", f"/api/workflows/{wid}",
                {"name": "Orders", "description": "test", "graphJson": graph_with_webhook()}, owner["token"])
    check("owner can save the graph", s == 200, str(s))

    # ---- Invitations ----
    s, inv_v = call("POST", "/api/account/invitations",
                    {"email": viewer["email"], "preset": "Viewer"}, owner["token"])
    check("owner can invite a viewer", s == 200 and "inviteLink" in (inv_v or {}), f"{s} {inv_v}")
    s, inv_e = call("POST", "/api/account/invitations",
                    {"email": editor["email"], "preset": "Editor"}, owner["token"])
    check("owner can invite an editor", s == 200, f"{s} {inv_e}")

    s, dup = call("POST", "/api/account/invitations", {"email": viewer["email"], "preset": "Viewer"}, owner["token"])
    check("duplicate pending invite is refused (409)", s == 409, f"{s} {dup}")

    s, _ = call("POST", "/api/invitations/accept", {"token": token_from_link(inv_v["inviteLink"])}, stranger["token"])
    check("invite for another email is refused (403)", s == 403, str(s))

    s, _ = call("POST", "/api/invitations/accept", {"token": token_from_link(inv_v["inviteLink"])}, viewer["token"])
    check("invited viewer accepts the link", s == 200, str(s))
    s, _ = call("POST", "/api/invitations/accept", {"token": token_from_link(inv_e["inviteLink"])}, editor["token"])
    check("invited editor accepts the link", s == 200, str(s))
    s, _ = call("POST", "/api/invitations/accept", {"token": token_from_link(inv_v["inviteLink"])}, viewer["token"])
    check("a link cannot be used twice", s == 404, str(s))

    acct = owner["id"]
    newbie_email = f"newbie-{uuid.uuid4().hex[:8]}@test.local"
    later_email = f"later-{uuid.uuid4().hex[:8]}@test.local"

    # ---- Access by membership ----
    s, lst = call("GET", "/api/workflows", token=viewer["token"], account=acct)
    check("viewer sees the owner's workflows", s == 200 and any(w["id"] == wid for w in lst), f"{s} {lst}")
    s, _ = call("GET", "/api/workflows", token=stranger["token"], account=acct)
    check("a non-member cannot open the account (403)", s == 403, str(s))

    # ---- Viewer has only view ----
    s, body = call("PUT", f"/api/workflows/{wid}", {"name": "Hacked", "graphJson": None}, viewer["token"], acct)
    check("viewer cannot save (403)", s == 403 and body.get("message") == "You have no access to do this.",
          f"{s} {body}")
    s, body = call("POST", f"/api/workflows/{wid}/lock", token=viewer["token"], account=acct)
    check("viewer cannot lock (403)", s == 403, f"{s} {body}")
    s, body = call("POST", f"/api/workflows/{wid}/run", {}, viewer["token"], acct)
    check("viewer cannot run (403)", s == 403, f"{s} {body}")

    # ---- Editor edits and locks ----
    s, body = call("PUT", f"/api/workflows/{wid}", {"name": "Orders v2", "graphJson": graph_with_webhook()},
                   editor["token"], acct)
    check("editor can save", s == 200 and body["name"] == "Orders v2", f"{s} {body}")

    s, body = call("POST", f"/api/workflows/{wid}/lock", token=editor["token"], account=acct)
    check("editor can lock", s == 200 and body.get("isLocked") is True, f"{s} {body}")
    check("locker is shown on the workflow", bool((body or {}).get("lockedBy")), str(body))

    s, lst = call("GET", "/api/workflows", token=viewer["token"], account=acct)
    locked_row = next((w for w in lst if w["id"] == wid), {})
    check("workflow list shows the lock", locked_row.get("isLocked") is True, str(locked_row))

    # ---- Everything is refused while locked ----
    s, body = call("PUT", f"/api/workflows/{wid}", {"name": "Locked edit", "graphJson": None},
                   editor["token"], acct)
    check("save while locked is refused (423)", s == 423, f"{s} {body}")
    s, body = call("POST", f"/api/workflows/{wid}/run", {}, editor["token"], acct)
    check("manual run while locked is refused (423)", s == 423, f"{s} {body}")
    s, body = call("POST", f"/api/workflows/{wid}/activate", token=editor["token"], account=acct,
                   query={"active": "true"})
    check("activate while locked is refused (423)", s == 423, f"{s} {body}")
    s, body = call("POST", f"/api/webhooks/{wid}/order", {"x": 1}, query={"token": "t123"})
    check("webhook while locked is refused (423)", s == 423, f"{s} {body}")
    s, body = call("DELETE", f"/api/workflows/{wid}", token=owner["token"])
    check("owner cannot delete while locked (423)", s == 423, f"{s} {body}")
    # The Editor preset has no delete permission, so the permission check (403) runs before the lock check.
    s, body = call("DELETE", f"/api/workflows/{wid}", token=editor["token"], account=acct)
    check("editor without delete permission is refused (403)", s == 403, f"{s} {body}")

    # ---- Unlock rules ----
    s, body = call("POST", f"/api/workflows/{wid}/unlock", token=viewer["token"], account=acct)
    check("viewer cannot unlock (403)", s == 403, f"{s} {body}")

    s, body = call("POST", f"/api/workflows/{wid}/unlock", token=editor["token"], account=acct)
    check("editor can unlock", s == 200 and body.get("isLocked") is False, f"{s} {body}")

    # ---- Accounts list and members ----
    s, accts = call("GET", "/api/accounts", token=viewer["token"])
    mine = next((a for a in (accts or []) if a["accountId"] == acct), None)
    check("viewer's account list shows the owner's account as not owned",
          s == 200 and mine is not None and mine["isOwner"] is False and mine["permissions"] == ["workflows.view"],
          f"{s} {accts}")
    s, accts = call("GET", "/api/accounts", token=owner["token"])
    check("owner's account list includes their own account with all permissions",
          s == 200 and any(a["isOwner"] and len(a["permissions"]) == 6 for a in (accts or [])), str(accts))

    s, members = call("GET", "/api/account/members", token=owner["token"])
    emails = [m["email"] for m in (members or [])]
    check("owner can list members", s == 200 and viewer["email"] in emails and editor["email"] in emails,
          f"{s} {members}")

    s, _ = call("GET", "/api/account/members", token=viewer["token"], account=acct)
    check("viewer cannot manage members (403 on invite list)", call("GET", "/api/account/invitations",
          token=viewer["token"], account=acct)[0] == 403)

    # Owner gives the editor "manage team" (plus view) only.
    s, _ = call("PUT", f"/api/account/members/{editor['id']}", {"permissions": ["workflows.view", "members.manage"]},
                owner["token"])
    check("owner can change a member's permissions", s == 200, str(s))

    # The editor can manage the team but cannot give away permissions they lack (Admin includes delete).
    s, body = call("POST", "/api/account/invitations", {"email": newbie_email, "preset": "Admin"},
                   editor["token"], acct)
    check("cannot grant permissions you don't have (403)", s == 403 and "can't give" in (body or {}).get("message", ""),
          f"{s} {body}")

    s, body = call("POST", "/api/account/invitations", {"email": newbie_email, "preset": "Viewer"},
                   editor["token"], acct)
    check("can grant permissions you do have", s == 200, f"{s} {body}")

    s, pending = call("GET", "/api/account/invitations", token=editor["token"], account=acct)
    check("pending invitations list works for a manager", s == 200 and any(
        i["email"] == newbie_email for i in (pending or [])), f"{s} {pending}")

    # Owner takes the editor back to the Editor preset.
    s, _ = call("PUT", f"/api/account/members/{editor['id']}",
                {"permissions": ["workflows.view", "workflows.create", "workflows.edit", "workflows.run"]},
                owner["token"])
    check("owner can restore the editor's permissions", s == 200, str(s))

    # ---- Executions are scoped to the account ----
    s, run = call("POST", f"/api/workflows/{wid}/run", {}, owner["token"])
    check("owner can run the unlocked workflow", s == 200 and "executionId" in (run or {}), f"{s} {run}")
    s, execs = call("GET", "/api/executions", token=viewer["token"], account=acct)
    check("viewer can see the owner's runs", s == 200 and any(
        e["workflowId"] == wid for e in (execs or [])), f"{s} {execs}")
    s, _ = call("GET", "/api/executions", token=stranger["token"], account=acct)
    check("a non-member cannot see the owner's runs (403)", s == 403, str(s))

    # ---- Removal ends access ----
    s, _ = call("DELETE", f"/api/account/members/{editor['id']}", token=owner["token"])
    check("owner can remove a member", s == 204, str(s))
    s, body = call("GET", "/api/workflows", token=editor["token"], account=acct)
    check("removed member loses access (403)", s == 403, f"{s} {body}")

    # ---- Delete only after unlock ----
    s, _ = call("DELETE", f"/api/workflows/{wid}", token=owner["token"])
    check("owner can delete once unlocked", s == 204, str(s))
    s, _ = call("GET", f"/api/workflows/{wid}", token=owner["token"])
    check("deleted workflow is gone (404)", s == 404, str(s))

    # ---- Revoked invitation ----
    s, inv_r = call("POST", "/api/account/invitations", {"email": later_email, "preset": "Viewer"},
                    owner["token"])
    if s == 200:
        s, _ = call("DELETE", f"/api/account/invitations/{inv_r['id']}", token=owner["token"])
        check("owner can revoke an invitation", s == 204, str(s))
        later = new_user("later")
        s, _ = call("POST", "/api/invitations/accept", {"token": token_from_link(inv_r["inviteLink"])},
                    later["token"])
        check("a revoked invitation is refused (404)", s == 404, str(s))

    failed = [n for n, ok in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} checks passed")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
