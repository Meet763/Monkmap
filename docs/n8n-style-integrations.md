# How n8n Handles Integrations (and how yours compares)

A practical tour of n8n's integration model, mapped onto **your** codebase, plus one concrete
example workflow you can rebuild to see it in action.

---

## 1. The big idea: one **app node**, many operations

n8n does **not** make a separate node per action. It makes **one node per app** (Gmail, Google
Sheets, Slack, Notion…). Inside that one node you choose:

- **Resource** — *what* you act on (Gmail → `Message`, `Label`, `Draft`; Sheets → `Sheet`, `Spreadsheet`)
- **Operation** — *what you do* to it (`Send`, `Get`, `Delete`, `Append`, `Update`…)

The fields on the node **change based on your Resource + Operation pick**. Pick *Message → Send* and
you see To / Subject / Body. Pick *Message → Get* and you see a Message ID field instead.

```
┌─ Gmail ────────────────────────────┐
│ Credential:  [ my-gmail ▾ ]         │   ← reusable connection (see §2)
│ Resource:    [ Message  ▾ ]         │   ← what
│ Operation:   [ Send     ▾ ]         │   ← how
│ ----------------------------------- │
│ To:      {{ $json.email }}          │   ← fields depend on Resource+Operation
│ Subject: New order #{{ $json.id }}  │
│ Body:    Thanks for your order!     │
└─────────────────────────────────────┘
```

**Your platform now works the n8n way.** Each Google service is **one app node** — *Gmail*,
*Google Calendar*, *Google Sheets* — with an **Operation** dropdown and fields that show/hide based
on the chosen operation. (We use Operation only, not Resource + Operation, because each service's
actions are a flat list today; Resource is easy to add later if a node grows many object types.)

| Model | Pro | Con |
|-------|-----|-----|
| One node per action | Simple, self-describing in the palette | Palette grows fast (100 apps × 10 ops = 1000 nodes) |
| **One node per app + Operation** (n8n, **what you have now**) | Palette stays small; discoverable | One more click; node UI must show/hide fields |

---

## 2. Credentials are a **separate, reusable thing**

This is the part that makes n8n feel clean. A **credential** (your `Connection`) is created **once**
and then **selected** from a dropdown in any node. You never paste tokens or ids into a node.

- Credential **types** are declared (`Gmail OAuth2`, `Google Sheets OAuth2`, `Slack API`, `HTTP
  Header Auth`…). Each type knows its auth URL, token URL and default scopes.
- n8n core runs the **generic OAuth2 dance** for every OAuth credential — individual apps don't
  re-implement it.
- A node **declares which credential type it needs**; the UI only offers matching credentials.

```
Credential "my-gmail" (Gmail OAuth2)  ──┐
                                        ├──▶ used by any Gmail node
Credential "work-sheets" (Sheets OAuth2)┘
```

**You already have this**, and after the last change your app nodes pick the account from a dropdown
instead of pasting a connection id. The pieces line up:

| n8n concept | Your code |
|-------------|-----------|
| Credential (stored, encrypted) | `Connection` entity + `CredentialProtector` |
| Credential **type** (auth URLs, scopes) | `IOAuthProvider` (e.g. `GoogleOAuthProvider`) |
| Generic OAuth2 engine | `IntegrationsController` connect/callback + `TokenManager` |
| Credential picker in a node | the new `"connection"` param → account dropdown |
| Scope selection | your new **per-service checkboxes** (Calendar/Gmail/Sheets/Drive) |

One thing n8n does that you could borrow: n8n stores **one credential per auth**, and a credential
can be **shared** across many nodes and workflows. You're already there.

---

## 3. Declarative nodes: adding an operation is **data, not code**

Modern n8n nodes are **declarative**. The node is basically a big description object: a list of
properties (dropdowns + fields) and a **routing** block that says which HTTP request each
operation maps to. No imperative `execute()` for most HTTP APIs.

```ts
// n8n declarative node (sketch)
{
  displayName: 'Gmail',
  properties: [
    { displayName: 'Resource',  name: 'resource',  type: 'options',
      options: [{ name: 'Message', value: 'message' }] },
    { displayName: 'Operation', name: 'operation', type: 'options',
      displayOptions: { show: { resource: ['message'] } },
      options: [{ name: 'Send', value: 'send',
        routing: { request: { method: 'POST', url: '/users/me/messages/send' } } }] },
    { displayName: 'To', name: 'to', type: 'string',
      displayOptions: { show: { resource: ['message'], operation: ['send'] } },
      routing: { send: { type: 'body', property: 'raw' /* …transform… */ } } },
  ],
}
```

**Your equivalent** is the `IIntegrationAction` interface: `InputSchema` (the fields) +
`ExecuteAsync` (the HTTP call). You write a small C# class per action and register it in
`Program.cs`. That's the **programmatic** style — more code than n8n's routing, but also more
flexible (the Gmail "build a MIME message and base64url it" step is easier in code than in routing).

The field-form you now generate from `InputSchema` is exactly n8n's `displayOptions`/`properties`
idea: **the schema drives the UI**.

| n8n | Your code |
|-----|-----------|
| `properties[]` description | `IIntegrationAction.InputSchema` (JSON Schema) |
| `routing.request` | `ExecuteAsync` + `GoogleApi.CallAsync` |
| `displayOptions.show` (conditional fields) | `NodeParameter.VisibleWhen` → config panel `visible()` |
| Resource/Operation grouping | `operation` dropdown; fields namespaced `<actionKey>::<field>` |

The app nodes are generated in `IntegrationActionNode.BuildServiceDefinitions(...)` and routed by
`NodeRegistry` (type `app.<provider>.<service>`, e.g. `app.google.gmail`). All operations of a
service run through the one `IntegrationActionNode`, which reads the `operation` parameter.

---

## 4. Everything flows by **expressions**

n8n passes JSON "items" node-to-node. You reference upstream data with expressions:

```
{{ $json.email }}                     // a field on the current item
{{ $node["Webhook"].json.body.name }} // a field from a named earlier node
{{ $now.toISODate() }}                // helpers
```

**You have this too:** `{{ $json.field }}` and `{{ $node.NodeId.field }}`, resolved by
`ExpressionResolver`. Same mental model.

---

## 5. Triggers vs actions

n8n splits nodes into **triggers** (start a run) and **actions** (do work):

- **Triggers:** Webhook, Schedule/Cron, and app triggers (e.g. *Gmail Trigger* that polls for new
  mail, *Google Sheets Trigger* on new row).
- **Actions:** everything else.

**You have:** `ManualTrigger`, `WebhookTrigger` (both `IsTrigger = true`). You don't yet have a
**Schedule/Cron trigger** or **polling app triggers** — that's the main gap if you want the
"every morning at 9am" or "when a new row appears" style automations n8n is famous for.

---

## 6. The escape hatch: HTTP Request node

When no app node exists, n8n users drop an **HTTP Request node** and call the API directly — and it
can even **borrow a stored credential** for auth. You already have an `HttpRequestNode`. Adding
"use connection X for auth" to it would let users hit *any* Google endpoint you haven't wrapped yet.

---

## 7. A concrete example — the classic "lead capture" workflow

This is the hello-world of n8n demos. **A form/webhook receives a new lead → log it to a Google
Sheet → email the person a confirmation.** Your platform can do this today with the nodes you have.

### In n8n it looks like:

```
┌─────────────┐   ┌──────────────────────┐   ┌───────────────────┐
│  Webhook    │──▶│ Google Sheets        │──▶│ Gmail             │
│  (trigger)  │   │ Append Row           │   │ Send Message      │
│  POST /lead │   │ cred: work-sheets    │   │ cred: my-gmail    │
└─────────────┘   └──────────────────────┘   └───────────────────┘
```

- **Webhook**: receives `{ "name": "...", "email": "...", "message": "..." }`
- **Sheets → Append**: `values = [ {{$json.name}}, {{$json.email}}, {{$json.message}} ]`
- **Gmail → Send**: `To = {{$json.email}}`, `Subject = "Thanks, {{$json.name}}!"`

### On YOUR platform (the n8n-style app nodes)

Rebuild it by clicking these nodes in the editor and wiring them left → right:

1. **Webhook Trigger**
   - Method `POST`, Path `lead`
   - Payload you'll send: `{ "name": "Alice", "email": "alice@example.com", "message": "Hi!" }`

2. **Google Sheets**  *(app node, table icon)*
   - **Account**: pick your connected Google account (needs the **Sheets** service connected)
   - **Operation**: `Append row`
   - **Spreadsheet Id**: the id from your sheet's URL
   - **Range**: `Sheet1`
   - **Values**: `["{{$node.trigger_1.name}}", "{{$node.trigger_1.email}}", "{{$node.trigger_1.message}}"]`

3. **Gmail**  *(app node, mail icon)*
   - **Account**: same Google account (needs the **Gmail** service connected)
   - **Operation**: `Send email`
   - **To**: `{{$node.trigger_1.email}}`
   - **Subject**: `Thanks, {{$node.trigger_1.name}}!`
   - **Body**: `Hi {{$node.trigger_1.name}}, we got your message and will reply soon.`

Pick the **Operation** first — the fields below it change to match. Wire
`Webhook ▶ Google Sheets ▶ Gmail`, Save, then fire the webhook with the sample payload. You'll see a
new row in the sheet and a confirmation email sent.

### The saved graph (for reference)

This is roughly what `graphJson` holds. Note the n8n-style shape: node `type` is the **app**
(`app.<provider>.<service>`), the chosen action lives in the `operation` parameter, and each
operation's fields are **namespaced** `<actionKey>::<field>` so operations never collide:

```json
{
  "nodes": [
    { "id": "trigger_1", "type": "WebhookTrigger",
      "position": { "x": 80, "y": 160 },
      "parameters": { "method": "POST", "path": "lead" } },

    { "id": "sheets_1", "type": "app.google.sheets",
      "position": { "x": 360, "y": 160 },
      "parameters": {
        "connectionId": "<your-google-connection-id>",
        "operation": "google.sheets.appendRow",
        "google.sheets.appendRow::spreadsheetId": "<your-sheet-id>",
        "google.sheets.appendRow::range": "Sheet1",
        "google.sheets.appendRow::values": "[\"{{$node.trigger_1.name}}\", \"{{$node.trigger_1.email}}\"]"
      } },

    { "id": "gmail_1", "type": "app.google.gmail",
      "position": { "x": 640, "y": 160 },
      "parameters": {
        "connectionId": "<your-google-connection-id>",
        "operation": "google.gmail.sendEmail",
        "google.gmail.sendEmail::to": "{{$node.trigger_1.email}}",
        "google.gmail.sendEmail::subject": "Thanks, {{$node.trigger_1.name}}!",
        "google.gmail.sendEmail::body": "Hi {{$node.trigger_1.name}}, we got your message soon."
      } }
  ],
  "connections": [
    { "id": "e1", "source": "trigger_1", "target": "sheets_1", "sourceHandle": "main", "targetHandle": "main" },
    { "id": "e2", "source": "sheets_1",  "target": "gmail_1",  "sourceHandle": "main", "targetHandle": "main" }
  ]
}
```

> Why `{{$node.trigger_1.*}}` everywhere? Data flows item-to-item. The Sheets node outputs the
> Google API response, so after it `{{$json.email}}` would point at the *Sheets response*, not the
> original lead. Reaching back to the named trigger node (n8n's `{{ $node["Webhook"].json.email }}`)
> always gets the original lead — so the example uses that form throughout.

---

## 8. If you want to move closer to n8n — a short roadmap

Ranked by value for effort (✅ = already done):

1. ✅ **App node + Operation grouping** — one node per app, Operation dropdown, operation-scoped fields.
2. ✅ **Credential picker + per-service scope selection** — account dropdown, pick which services to grant.
3. **Schedule/Cron trigger node** — unlocks the entire "every morning / every 5 min" category.
   *~1 new `INode`.*
4. **Auth on the HTTP Request node** — "use connection X", so users can call any endpoint you
   haven't wrapped yet. *Small change to `HttpRequestNode`.*
5. **Resource + Operation (two levels)** — add a Resource dropdown above Operation for apps that act
   on many object types. *Medium; extend node generation.*
6. **Polling app triggers** (new row, new email) — most work; needs a scheduler + per-provider
   poll logic.

Your foundation (`IOAuthProvider`, `IIntegrationAction`, `Connection`, `ExpressionResolver`,
schema-driven forms, app-node generation) now matches n8n's spine. The main remaining differences
are **trigger variety** (Schedule + polling triggers), not architecture.
