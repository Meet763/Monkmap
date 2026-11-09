# Plan — Webhook Trigger + built-in API test console

Status: **proposal, not built yet.** Read it and tell me if it's good or what to change.

## What you asked for

> A testing environment inside the platform where you can create an API endpoint for any
> request, and use it to test a workflow.

## What this means in our platform

Two pieces that work together:

1. **Webhook Trigger node** — a new trigger node that gives a workflow its own HTTP API URL.
   Any HTTP request to that URL starts the workflow, and the request (method, headers, query,
   body) becomes the trigger payload the rest of the nodes can read with `{{$json.*}}`.
   This is the "create one API for any request" part.

2. **Test console** — a small request builder *inside the editor* (a Postman-lite panel) so you
   can fire a request at that URL and watch the run, without leaving the app or installing
   anything.

Good news: the groundwork is already there —
- `TriggerType.Webhook` already exists in [Entities.cs](backend/WorkflowPlatform.Api/Entities/Entities.cs).
- `Execution.TriggerDataJson` already carries a trigger payload into a run (used today by the
  manual `/run` endpoint).
- The node system auto-renders any new node in the palette/config form, so the frontend work is
  small.

---

## Part 1 — Webhook Trigger node (backend)

### A. The node
New file `Engine/Nodes/WebhookTriggerNode.cs`, implementing `INode`, `IsTrigger = true`,
`Group = "Triggers"`, `Icon = "webhook"`. It doesn't *call out* — it just emits the trigger
payload that the webhook endpoint handed to the run (same shape as `ManualTriggerNode`, which
emits its input). Parameters:

| Param | Purpose |
|---|---|
| `method` | GET / POST / PUT / PATCH / DELETE / ANY — which verb the endpoint accepts |
| `path` | optional extra path segment, e.g. `order-created`, so one workflow can have a tidy URL |
| `responseMode` | `Async` (return `{ executionId }` immediately) or `Sync` (wait and return the last node's output as the HTTP response) |

### B. The public endpoint
New file `Controllers/WebhooksController.cs`, route `**/api/webhooks/{workflowId:guid}/{path?}**`,
marked `[AllowAnonymous]` (it's a public endpoint by design). On a request it:

1. Loads the workflow and its **active version** graph.
2. Finds the Webhook Trigger node; checks the HTTP method matches (and the token — see Security).
3. Builds the payload:
   ```json
   { "method": "POST", "headers": { ... }, "query": { ... }, "body": { ... } }
   ```
4. Creates an `Execution` with `TriggerType = Webhook` and `TriggerDataJson = payload`, then
   enqueues it (exactly like the existing `/run` flow in
   [WorkflowsController.cs](backend/WorkflowPlatform.Api/Controllers/WorkflowsController.cs#L126)).
5. Responds:
   - **Async mode:** `202 { executionId }` straight away.
   - **Sync mode:** waits for the run to finish and returns the final node output as the HTTP
     body. (See "Sync response" below — it's the one non-trivial bit.)

### C. Making the trigger drive the run
Small change in [WorkflowExecutor.cs](backend/WorkflowPlatform.Api/Engine/WorkflowExecutor.cs):
the start/trigger node should emit `Execution.TriggerDataJson` as its output item so the payload
flows downstream. (Manual runs already pass `TriggerDataJson`; this just makes the webhook payload
land in the same place.)

### D. Sync response (the only tricky part)
Today runs are queued and executed by a background worker, so the HTTP request can't just "return
the result". Options, simplest first:

- **Option 1 (recommended for MVP):** webhook always returns `202 { executionId }`. The test
  console then polls `GET /api/executions/{id}` (which already exists) until it's done and shows
  the output. Zero engine changes.
- **Option 2:** true sync response — register a `TaskCompletionSource` keyed by `executionId`,
  have the executor signal it on completion, and the controller `await`s it with a timeout. More
  code, but gives a real synchronous API.

I suggest shipping Option 1 first; add Option 2 later if you want real request/response APIs.

---

## Part 2 — Test console (frontend)

A **"Test" tab** in the editor (next to the node config panel), driven by whatever Webhook
Trigger node is on the canvas:

- Shows the workflow's webhook **URL** (with a copy button) and the method.
- Lets you set **query params, headers, and a JSON body** (prefilled with a sample).
- A **Send** button fires the request to the webhook URL.
- Shows the **response** (status + body) and a link to **View run** for the per-node detail you
  already have (with the live SignalR node lighting-up).
- A **cURL snippet** of the same request, so you can copy it to call the API from outside.

No new node-rendering code needed — this is one new panel component + a tiny API client method.

New/changed frontend files (estimate):
- `frontend/src/pages/editor/TestConsole.tsx` — the panel.
- small wiring in `frontend/src/pages/editor/Editor.tsx` to add the tab.
- one method in `frontend/src/api/client.ts`.
- register a `webhook` icon in `WfNode.tsx` / `NodePalette.tsx` (same one-line edits we did for
  the AI node).

---

## Security (important, since the endpoint is public)

- **Per-workflow webhook token.** Generate a secret when a webhook node is saved; require it in
  the URL (`?token=...`) or an `X-Webhook-Token` header. Reject if missing/wrong. Prevents random
  people triggering your workflow.
- **Active-only (optional).** Only accept requests when the workflow is active, or add a
  "test mode" flag so a workflow under development listens only while you're testing.
- **Payload size limit** and a basic **rate limit** so a public URL can't be abused.
- Note: this is the same security posture caveat already flagged for the Database/AI nodes —
  fine for MVP, tighten before untrusted exposure.

---

## Suggested build order

1. **Webhook Trigger node** + **public endpoint** (async mode) + executor payload wiring. → You
   can already test with curl/Postman.
2. **Webhook token** security.
3. **Test console** panel in the editor.
4. *(optional later)* **Sync response mode** for real request/response APIs.

Steps 1–3 are the core "testing environment". Each is small and builds on the node system that's
already in place.

---

## Open questions for you

1. **Async vs sync first?** I recommend async + poll (Option 1). OK?
2. **Where should the test console live** — a tab in the editor (my suggestion), or a separate
   "API Test" page in the sidebar?
3. **Auth on the webhook:** token in the URL, a header, or (for pure local testing) none at first?
4. Do you also want a **"catch the next real request" mode** (like n8n's test webhook that
   captures one incoming call and shows its payload), or is the manual request builder enough?
