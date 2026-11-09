"""
Monkmap test client: plays the role of an EXTERNAL APP (like your Django project).

Business scenario ("new order"):
  1. An order system POSTs an order to our webhook.
  2. An AI node writes a thank-you email (Claude).
  3. An integration action logs the order in a Google Sheet.
  4. An integration action emails the customer through Gmail.

Only uses the Python standard library (no pip install needed).

Environment variables (all optional except MONKMAP_EMAIL/PASSWORD if you want auto-register):
  MONKMAP_URL        default http://localhost:5080
  MONKMAP_EMAIL      default tester@example.com
  MONKMAP_PASSWORD   default Passw0rd!
  ANTHROPIC_API_KEY    enables the AI step (skipped if empty)
  CLAUDE_MODEL         default claude-haiku-5-5
  GOOGLE_CONNECTION_ID enables the Sheets + Gmail steps (copy from the Integrations page)
  SHEET_ID             Google Sheet id (from its URL) for the Sheets step
  CUSTOMER_EMAIL       who receives the Gmail step (default: your own address)
  WEBHOOK_TOKEN        must match the token in the webhook node (default test-secret-123)

Run:  python run_scenario.py
"""

import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = os.environ.get("MONKMAP_URL", "http://localhost:5080").rstrip("/")
EMAIL = os.environ.get("MONKMAP_EMAIL", "tester@example.com")
PASSWORD = os.environ.get("MONKMAP_PASSWORD", "Passw0rd!")
ANTHROPIC_KEY = os.environ.get("ANTHROPIC_API_KEY", "")
CLAUDE_MODEL = os.environ.get("CLAUDE_MODEL", "claude-haiku-5-5")
GOOGLE_CONN = os.environ.get("GOOGLE_CONNECTION_ID", "")
SHEET_ID = os.environ.get("SHEET_ID", "")
CUSTOMER_EMAIL = os.environ.get("CUSTOMER_EMAIL", EMAIL)
WEBHOOK_TOKEN = os.environ.get("WEBHOOK_TOKEN", "test-secret-123")


def call(method, path, body=None, token=None, query=None):
    """Tiny HTTP helper. Returns (status, parsed_json_or_text)."""
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
    req = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=60) as res:
            raw = res.read().decode("utf-8")
            status = res.status
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8")
        status = e.code
    try:
        return status, json.loads(raw) if raw else None
    except json.JSONDecodeError:
        return status, raw


def login_or_register():
    status, body = call("POST", "/api/auth/login", {"email": EMAIL, "password": PASSWORD})
    if status == 200:
        print(f"[auth] logged in as {EMAIL}")
        return body["token"]
    status, body = call("POST", "/api/auth/register",
                        {"email": EMAIL, "password": PASSWORD, "firstName": "Test", "lastName": "User"})
    if status not in (200, 201):
        sys.exit(f"[auth] could not log in or register: HTTP {status} {body}")
    print(f"[auth] registered {EMAIL}")
    return body["token"]


def build_graph():
    """The business workflow as a graph. Only includes steps whose credentials are configured."""
    nodes = [{
        "id": "webhook", "type": "WebhookTrigger", "position": {"x": 0, "y": 0},
        "parameters": {"method": "POST", "path": "new-order", "token": WEBHOOK_TOKEN},
    }]
    connections = []
    previous = "webhook"

    if ANTHROPIC_KEY:
        nodes.append({
            "id": "ai", "type": "AiAgent", "position": {"x": 240, "y": 0},
            "parameters": {
                "mode": "Chat", "provider": "Anthropic", "model": CLAUDE_MODEL,
                "apiKey": ANTHROPIC_KEY, "temperature": "0.4", "maxTokens": "300",
                "system": "Write short, friendly, plain-text emails. No quotation marks, no markdown.",
                "prompt": ("Write a 3-sentence thank-you email to {{$json.body.customer.name}} "
                           "for order {{$json.body.order_id}} totalling {{$json.body.total}} USD."),
            },
        })
        connections.append({"id": f"e_{previous}_ai", "source": previous, "target": "ai",
                            "sourceHandle": "main", "targetHandle": "main"})
        previous = "ai"

    if GOOGLE_CONN and SHEET_ID:
        nodes.append({
            "id": "sheet", "type": "IntegrationAction", "position": {"x": 480, "y": 0},
            "parameters": {
                "action": "google.sheets.appendRow", "connectionId": GOOGLE_CONN,
                "input": json.dumps({
                    "spreadsheetId": SHEET_ID, "range": "Orders",
                    "values": ["{{$json.body.order_id}}", "{{$json.body.customer.name}}",
                               "{{$json.body.total}}"],
                }),
            },
        })
        connections.append({"id": f"e_{previous}_sheet", "source": previous, "target": "sheet",
                            "sourceHandle": "main", "targetHandle": "main"})
        previous = "sheet"

    if GOOGLE_CONN and ANTHROPIC_KEY:
        nodes.append({
            "id": "gmail", "type": "IntegrationAction", "position": {"x": 720, "y": 0},
            "parameters": {
                "action": "google.gmail.sendEmail", "connectionId": GOOGLE_CONN,
                "input": json.dumps({
                    "to": CUSTOMER_EMAIL,
                    "subject": "Thanks for your order {{$json.body.order_id}}",
                    "body": "{{$node.ai.text}}",
                }),
            },
        })
        connections.append({"id": f"e_{previous}_gmail", "source": previous, "target": "gmail",
                            "sourceHandle": "main", "targetHandle": "main"})

    return {"nodes": nodes, "connections": connections}


def main():
    token = login_or_register()

    name = f"Business scenario test {uuid.uuid4().hex[:6]}"
    status, wf = call("POST", "/api/workflows", {"name": name, "description": "Created by test client"}, token)
    if status not in (200, 201):
        sys.exit(f"[workflow] create failed: HTTP {status} {wf}")
    wid = wf["id"]
    print(f"[workflow] created {wid}")

    graph = build_graph()
    status, _ = call("PUT", f"/api/workflows/{wid}",
                     {"name": name, "description": None, "graphJson": json.dumps(graph)}, token)
    if status != 200:
        sys.exit(f"[workflow] save failed: HTTP {status}")
    steps = [n["type"] for n in graph["nodes"]]
    print(f"[workflow] saved with steps: {' -> '.join(steps)}")

    status, _ = call("POST", f"/api/workflows/{wid}/activate", token=token, query={"active": "true"})
    if status != 200:
        sys.exit(f"[workflow] activate failed: HTTP {status}")
    print("[workflow] activated")

    order = {
        "order_id": f"ORD-{uuid.uuid4().hex[:6].upper()}",
        "customer": {"name": "Asha Patel", "email": CUSTOMER_EMAIL},
        "total": "149.00",
        "items": [{"title": "Blue T-Shirt", "quantity": 2}],
    }
    status, accepted = call("POST", f"/api/webhooks/{wid}/new-order", order, query={"token": WEBHOOK_TOKEN})
    if status != 202:
        sys.exit(f"[webhook] expected 202, got HTTP {status}: {accepted}")
    exec_id = accepted["executionId"]
    print(f"[webhook] accepted order {order['order_id']} -> run {exec_id}")

    deadline = time.time() + 180
    detail = None
    while time.time() < deadline:
        _, detail = call("GET", f"/api/executions/{exec_id}", token=token)
        if detail and detail.get("status") in ("Succeeded", "Failed", "Canceled"):
            break
        time.sleep(2)

    print(f"\n[run] final status: {detail.get('status') if detail else 'unknown (timed out)'}")
    if detail and detail.get("error"):
        print(f"[run] error: {detail['error']}")
    for n in (detail or {}).get("nodes", []):
        print(f"  - {n['nodeId']:<10} {n['nodeType']:<18} {n['status']:<10} {n.get('error') or ''}")
    print(f"\nView it in the app: {BASE.replace(':5080', ':5173')}/executions/{exec_id}")


if __name__ == "__main__":
    main()
