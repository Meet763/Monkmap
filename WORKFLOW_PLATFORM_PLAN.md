# Workflow Automation Platform — Architecture & Implementation Plan

> A personal learning project to build an n8n / Elsa-style workflow automation platform
> using **React.js** (frontend) and **ASP.NET Core / C#** (backend).

**Author:** meetkathiriya596@gmail.com
**Last updated:** 2026-11-08

---

## Table of Contents

1. [Vision & Scope](#1-vision--scope)
2. [Core Concepts & Domain Model](#2-core-concepts--domain-model)
3. [High-Level Architecture](#3-high-level-architecture)
4. [Technology Stack](#4-technology-stack)
5. [Database Design](#5-database-design)
6. [Component Communication](#6-component-communication)
7. [The Workflow Execution Engine](#7-the-workflow-execution-engine)
8. [Integrations & App Connectors (No-Code)](#8-integrations--app-connectors-no-code)
9. [Frontend Design (React)](#9-frontend-design-react)
10. [Backend Design (ASP.NET Core)](#10-backend-design-aspnet-core)
11. [Development Roadmap](#11-development-roadmap)
12. [Step-by-Step Implementation Plan](#12-step-by-step-implementation-plan)
13. [Scalability Strategy](#13-scalability-strategy)
14. [Security Considerations](#14-security-considerations)
15. [Learning Outcomes](#15-learning-outcomes)

---

## 1. Vision & Scope

### What you're building

A platform where users visually design **workflows** by connecting **nodes** on a canvas. Each node performs an action (send an HTTP request, transform data, send an email, run a condition). Workflows are **triggered** (by a schedule, a webhook, or manually) and the backend **executes** them node-by-node, passing data between nodes.

### Minimum Viable Product (MVP)

| Capability | Included in MVP? |
|---|---|
| Visual drag-and-drop workflow editor | ✅ |
| A handful of core nodes (HTTP, Set/Transform, IF, Webhook trigger, Schedule trigger) | ✅ |
| Save / load / version workflows | ✅ |
| Execute a workflow and view results per node | ✅ |
| Manual + webhook + scheduled triggers | ✅ |
| Execution history & logs | ✅ |
| User authentication | ✅ |
| Credential management (store API keys for nodes) | ✅ |
| Multi-tenant orgs, RBAC, marketplace | ❌ (future) |

> **Learning tip:** Resist scope creep. A platform with 5 solid nodes and a reliable engine teaches you more than 50 half-working nodes.

---

## 2. Core Concepts & Domain Model

Understanding the vocabulary first makes every later decision easier.

- **Node** — a single unit of work (a "step"). Has a *type* (e.g. `HttpRequest`), *parameters* (configured by the user), and *input/output ports*.
- **Node Type / Node Definition** — the blueprint for a node: its parameters schema, its execution logic. This is where your extensibility lives.
- **Connection (Edge)** — a directed link from one node's output port to another node's input port. Defines data flow and execution order.
- **Workflow** — a graph of nodes + connections + metadata. Essentially a **DAG** (Directed Acyclic Graph), though loops can be modeled explicitly.
- **Trigger** — a special node that *starts* a workflow (webhook, schedule, manual).
- **Execution (Run)** — one instance of a workflow running. Has a status (`Running`, `Succeeded`, `Failed`), start/end times, and per-node results.
- **Credential** — reusable, encrypted secrets (API keys, tokens) referenced by nodes.
- **Execution Context** — the data bag that flows through the workflow; each node reads inputs and writes outputs into it.

```
Trigger Node ──▶ HTTP Node ──▶ IF Node ──┬──(true)──▶ Email Node
                                          └──(false)─▶ Slack Node
```

---

## 3. High-Level Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│                            CLIENT (Browser)                            │
│   React SPA: Workflow Editor (React Flow) · Dashboard · Executions     │
└───────────────┬──────────────────────────────────┬────────────────────┘
                │ REST (JSON)                        │ WebSocket / SignalR
                │ (CRUD, trigger runs)               │ (live execution updates)
┌───────────────▼──────────────────────────────────▼────────────────────┐
│                      ASP.NET Core Web API (Gateway)                     │
│   Controllers · Auth (JWT) · Validation · SignalR Hub · Webhook endpts  │
└───────┬───────────────────┬──────────────────────────┬─────────────────┘
        │                   │                           │
        │ (read/write)      │ (enqueue job)             │ (schedule)
┌───────▼────────┐  ┌───────▼─────────┐        ┌────────▼──────────┐
│  PostgreSQL    │  │  Message Queue   │        │  Scheduler        │
│  (EF Core)     │  │  (RabbitMQ /     │        │  (Quartz.NET /    │
│  workflows,    │  │   Redis Streams) │        │   Hangfire)       │
│  executions    │  └───────┬──────────┘        └────────┬──────────┘
└────────────────┘          │                            │
                            │ dequeue                     │ triggers
                   ┌────────▼─────────────────────────────▼──────────┐
                   │        Worker Service (Execution Engine)         │
                   │  Loads workflow · walks the graph · runs nodes   │
                   │  · persists results · pushes live updates        │
                   └──────────────────────────────────────────────────┘
```

### Why split the API from the Worker?

- The **API** must stay responsive (fast request/response). It should never block while a 10-minute workflow runs.
- The **Worker** does the slow, CPU/IO-heavy execution. You can scale workers independently (run 10 workers, 1 API).
- They communicate through a **queue**, which also gives you durability (a crashed worker doesn't lose the job).

> **Phase 1 simplification:** You don't need the queue/worker split on day one. Start with an **in-process background execution** (`IHostedService` + `Channel<T>`) inside the API. Extract it to a separate worker service only when you're comfortable — this is a deliberate roadmap milestone (see §10).

---

## 4. Technology Stack

### Backend

| Concern | Choice | Why |
|---|---|---|
| Runtime | **.NET 9** (or latest LTS) | Modern C#, performance, long support |
| API | **ASP.NET Core Web API** (Minimal APIs or Controllers) | Core of the platform |
| ORM | **EF Core 9** | Productive, migrations, LINQ |
| Database | **PostgreSQL** | Great JSON support (`jsonb`), free, scalable |
| Real-time | **SignalR** | Push live execution status to the UI |
| Background jobs | **Hangfire** (easy) or **Quartz.NET** (cron-rich) | Scheduled triggers, retries |
| Queue (later) | **RabbitMQ** or **Redis Streams** | Decouple API from workers |
| Auth | **ASP.NET Core Identity + JWT** | Users, roles, tokens |
| Validation | **FluentValidation** | Clean request validation |
| Mapping | **Mapster** or manual | DTO ↔ entity mapping |
| Secrets encryption | **ASP.NET Data Protection** / AES | Encrypt stored credentials |
| Logging | **Serilog** | Structured logs |
| Testing | **xUnit + FluentAssertions + Testcontainers** | Unit + integration tests |
| API docs | **Swagger / OpenAPI (Swashbuckle)** | Explore & test endpoints |

### Frontend

| Concern | Choice | Why |
|---|---|---|
| Framework | **React 18 + TypeScript** | Type safety is a huge learning win |
| Build tool | **Vite** | Fast dev server & builds |
| Node canvas | **React Flow (@xyflow/react)** | The single most important choice — handles the node graph UI |
| State (server) | **TanStack Query** | Caching, refetching API data |
| State (client) | **Zustand** | Lightweight UI/editor state |
| Routing | **React Router** | SPA navigation |
| Forms | **React Hook Form + Zod** | Node parameter forms |
| UI components | **shadcn/ui + Tailwind CSS** | Fast, clean, customizable |
| Real-time | **@microsoft/signalr** client | Receive live updates |
| HTTP | **Axios** or fetch wrapper | API calls |

### DevOps / Tooling

- **Docker + Docker Compose** — run Postgres, Redis/RabbitMQ, API, and worker locally with one command.
- **Git + GitHub** — version control; GitHub Actions for CI later.
- **dotnet-ef** CLI — migrations.

> **React Flow is the keystone of the frontend.** Spend time learning it early — it gives you draggable nodes, connectable edges, pan/zoom, and a serializable graph model out of the box. Building that by hand would cost weeks.

---

## 5. Database Design

PostgreSQL with `jsonb` columns is ideal here: the *structure* of a workflow (its nodes/edges) is flexible and best stored as JSON, while *metadata* stays relational.

### Entity-Relationship Overview

```
User ──1:N──▶ Workflow ──1:N──▶ WorkflowVersion
  │                │
  │                └──1:N──▶ Execution ──1:N──▶ NodeExecution
  │
  └──1:N──▶ Credential
```

### Tables

#### `users`
| Column | Type | Notes |
|---|---|---|
| id | uuid (PK) | |
| email | text (unique) | |
| password_hash | text | via Identity |
| display_name | text | |
| created_at | timestamptz | |

#### `workflows`
| Column | Type | Notes |
|---|---|---|
| id | uuid (PK) | |
| user_id | uuid (FK → users) | owner |
| name | text | |
| description | text | |
| is_active | boolean | are triggers armed? |
| active_version_id | uuid (FK → workflow_versions) | currently published version |
| created_at / updated_at | timestamptz | |

#### `workflow_versions`
Storing versions separately gives you undo/rollback and safe edits while a workflow is running.
| Column | Type | Notes |
|---|---|---|
| id | uuid (PK) | |
| workflow_id | uuid (FK) | |
| version_number | int | |
| graph | **jsonb** | the full nodes + edges definition |
| created_at | timestamptz | |

**Example `graph` jsonb:**
```json
{
  "nodes": [
    {
      "id": "n1",
      "type": "ScheduleTrigger",
      "position": { "x": 100, "y": 200 },
      "parameters": { "cron": "0 */5 * * * *" }
    },
    {
      "id": "n2",
      "type": "HttpRequest",
      "position": { "x": 400, "y": 200 },
      "parameters": {
        "method": "GET",
        "url": "https://api.example.com/data",
        "credentialId": "cred-123"
      }
    }
  ],
  "connections": [
    { "id": "e1", "source": "n1", "sourceHandle": "main", "target": "n2", "targetHandle": "main" }
  ]
}
```

#### `executions`
| Column | Type | Notes |
|---|---|---|
| id | uuid (PK) | |
| workflow_id | uuid (FK) | |
| workflow_version_id | uuid (FK) | which version ran |
| status | text enum | `Queued`, `Running`, `Succeeded`, `Failed`, `Canceled` |
| trigger_type | text | `Manual`, `Webhook`, `Schedule` |
| started_at / finished_at | timestamptz | |
| error | text (nullable) | top-level failure reason |

#### `node_executions`
One row per node per run — your execution log and debugging goldmine.
| Column | Type | Notes |
|---|---|---|
| id | uuid (PK) | |
| execution_id | uuid (FK) | |
| node_id | text | matches node id in graph |
| status | text enum | `Pending`, `Running`, `Succeeded`, `Failed`, `Skipped` |
| input_data | jsonb | what the node received |
| output_data | jsonb | what the node produced |
| error | text (nullable) | |
| started_at / finished_at | timestamptz | |

#### `credentials`
| Column | Type | Notes |
|---|---|---|
| id | uuid (PK) | |
| user_id | uuid (FK) | |
| name | text | user-friendly label |
| type | text | `ApiKey`, `BasicAuth`, `OAuth2`, etc. |
| encrypted_data | bytea / text | **AES-encrypted** JSON blob of secrets |
| created_at | timestamptz | |

> **Never store secrets in plaintext.** Encrypt `encrypted_data` with ASP.NET Core Data Protection or AES, with the key held outside the DB (user-secrets in dev, a key vault later).

### Indexing tips
- Index `executions(workflow_id, started_at desc)` for history listing.
- Index `node_executions(execution_id)`.
- Consider a retention/cleanup job — execution logs grow fast.

---

## 6. Component Communication

### 6.1 Frontend ↔ Backend (REST)

Standard JSON over HTTPS for all CRUD and commands. Representative endpoints:

```
POST   /api/auth/login
POST   /api/auth/register

GET    /api/workflows
POST   /api/workflows
GET    /api/workflows/{id}
PUT    /api/workflows/{id}            # save graph (creates a new version)
DELETE /api/workflows/{id}
POST   /api/workflows/{id}/activate   # arm triggers
POST   /api/workflows/{id}/run        # manual execution → returns executionId

GET    /api/executions?workflowId=... # history
GET    /api/executions/{id}           # detail incl. node_executions

GET    /api/node-types                # drives the frontend node palette
GET    /api/credentials
POST   /api/credentials

GET    /api/me                        # current user profile / settings
PUT    /api/me                        # update profile (name, avatar)
GET    /api/workflows/{id}/shares      # list collaborators
POST   /api/workflows/{id}/shares      # invite collaborator (role + permissions)
PUT    /api/workflows/{id}/shares/{userId}   # change role / granular flags
DELETE /api/workflows/{id}/shares/{userId}   # remove collaborator

POST   /api/webhooks/{workflowId}/{token}   # public webhook trigger entry point
```

### 6.2 Backend → Frontend (SignalR, real-time)

When a workflow runs, the UI shouldn't poll. The worker pushes updates:

```
Hub: /hubs/executions

Server → Client events:
  ExecutionStarted(executionId)
  NodeStarted(executionId, nodeId)
  NodeCompleted(executionId, nodeId, status, outputPreview)
  ExecutionFinished(executionId, status)
```

The React client subscribes to the group for the `executionId` it's watching and lights up nodes on the canvas as they run (green = success, red = failed, spinner = running).

### 6.3 API → Worker (Queue)

```
1. User clicks "Run" → API creates an Execution row (status=Queued)
2. API publishes { executionId } to the queue
3. API immediately returns executionId to the client (non-blocking)
4. A Worker dequeues the message
5. Worker loads the workflow version, runs the engine
6. Worker writes node_executions + pushes SignalR events
7. Worker marks Execution Succeeded/Failed
```

> **Phase 1:** Replace steps 2–4 with an in-process `System.Threading.Channels.Channel<Guid>` consumed by an `IHostedService`. Same conceptual flow, zero infrastructure. Swapping in RabbitMQ later only changes the publish/consume lines because you'll hide them behind an `IExecutionQueue` interface.

### 6.4 Communication design principle

Define a thin interface for each boundary so you can swap implementations:
```csharp
public interface IExecutionQueue
{
    Task EnqueueAsync(Guid executionId, CancellationToken ct);
}
// InProcessChannelQueue  → Phase 1
// RabbitMqExecutionQueue → Phase 4
```

---

## 7. The Workflow Execution Engine

This is the **heart of the project** and where you'll learn the most. Build it carefully.

### 7.1 The Node abstraction

```csharp
public interface INode
{
    string Type { get; }               // "HttpRequest"
    NodeDefinition GetDefinition();     // params schema for the UI

    Task<NodeResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct);
}

public sealed record NodeExecutionContext(
    IReadOnlyDictionary<string, JsonElement> Parameters,
    IReadOnlyList<JsonElement> InputItems,     // data from upstream nodes
    ICredentialResolver Credentials,
    ILogger Logger
);

public sealed record NodeResult(
    bool Success,
    IReadOnlyList<JsonElement> OutputItems,
    string? Error = null,
    string? OutputPort = "main"       // which edge to follow (for IF nodes)
);
```

Each concrete node implements `INode`:
- `HttpRequestNode`
- `SetNode` (transform/map fields)
- `IfNode` (returns `"true"` or `"false"` as `OutputPort`)
- `WebhookTriggerNode`, `ScheduleTriggerNode`, `ManualTriggerNode`

Register them in DI and resolve by `Type`. A `NodeRegistry` maps `"HttpRequest" → HttpRequestNode`. **This registry pattern is what makes adding new nodes trivial** — the core engine never changes.

### 7.2 The engine algorithm (topological execution)

```
1. Parse the graph (nodes + connections) from the version's jsonb.
2. Find the trigger node → it produces the initial data items.
3. Build adjacency: for each node, which nodes does it feed?
4. Execute in dependency order:
     - A node runs only once ALL its inputs are ready.
     - Use a topological sort (Kahn's algorithm) for a DAG.
5. For each node:
     a. Gather input items from upstream node outputs.
     b. Resolve parameter expressions (e.g. {{ $node["HTTP"].data.id }}).
     c. Call node.ExecuteAsync(...).
     d. Persist NodeExecution (input, output, status).
     e. Push SignalR event.
     f. For branching nodes (IF), follow only the matching OutputPort.
6. If a node fails: mark it failed; stop (or continue, per error policy).
7. Mark the Execution finished.
```

### 7.3 Expressions / data passing

Users need to reference earlier nodes' output in later parameters (e.g. URL `https://x/{{$json.userId}}`). Start simple:
- **Phase 1:** support `{{$json.fieldName}}` referring to the immediate input, resolved with a small regex + `System.Text.Json` lookup.
- **Later:** a richer expression evaluator (even embedding **Jint**, a JS interpreter, to mirror n8n's JS expressions — a great learning exercise).

### 7.4 Reliability features (add incrementally)
- **Retries** with backoff per node.
- **Timeouts** per node (`CancellationToken`).
- **Idempotency** for webhook triggers.
- **Cancellation** of a running execution.

---

## 8. Integrations & App Connectors (No-Code)

This is the feature that turns your project from a "workflow engine" into a real **platform**: ready-made connectors for apps like **Google (Gmail, Sheets, Drive, Calendar), Slack, GitHub, Notion, Stripe**, etc. A user clicks *"Connect Google"*, authorizes once, and then drags a *"Gmail → Send Email"* node into a workflow — **no code at all**. This is exactly the Zapier / n8n / Make model.

### 8.1 The two layers of an integration

Separate these two concepts — it's the key design insight:

1. **Connection (the account)** — the user's authenticated link to a third-party app, obtained via **OAuth2**. "My Gmail account." Stored as encrypted tokens. Reusable across many workflows.
2. **Actions & Triggers (the operations)** — what you can *do* with that account: *Gmail → Send Email*, *Sheets → Append Row*, *Slack → Post Message* (actions), and *Gmail → New Email Received*, *Sheets → New Row* (triggers).

```
┌──────────────────────┐        ┌───────────────────────────────────┐
│  App Connection       │        │  Connector Actions / Triggers      │
│  "My Google account"  │  used  │  • Gmail: Send Email               │
│  (OAuth tokens,       │  by →  │  • Sheets: Append Row              │
│   auto-refreshed)     │        │  • Calendar: Create Event          │
└──────────────────────┘        └───────────────────────────────────┘
```

### 8.2 How "no-code" actually works — declarative connectors

The trick that makes a connector *no-code* for your users **and** low-code for you: don't write a new C# class for every single action. Describe each app and its operations in a **declarative manifest** (JSON/YAML). A single **generic connector engine** reads the manifest and performs the right authenticated HTTP call. This is how n8n's "declarative-style nodes" work.

**Example connector manifest (`google-sheets.json`):**
```json
{
  "appKey": "google-sheets",
  "displayName": "Google Sheets",
  "icon": "google-sheets.svg",
  "auth": {
    "type": "oauth2",
    "provider": "google",
    "scopes": ["https://www.googleapis.com/auth/spreadsheets"]
  },
  "actions": [
    {
      "key": "appendRow",
      "displayName": "Append Row",
      "parameters": [
        { "name": "spreadsheetId", "type": "string", "required": true },
        { "name": "sheetName", "type": "string", "required": true },
        { "name": "values", "type": "json", "required": true }
      ],
      "request": {
        "method": "POST",
        "url": "https://sheets.googleapis.com/v4/spreadsheets/{{spreadsheetId}}/values/{{sheetName}}:append?valueInputOption=USER_ENTERED",
        "body": { "values": "{{values}}" }
      }
    }
  ]
}
```

With this, **adding a whole new app = adding one manifest file + its OAuth provider config**. No engine changes. The frontend reads the manifest to render the node's parameter form automatically. *That* is the no-code extensibility you asked about.

> For the few complex apps that a manifest can't express (weird pagination, binary uploads), keep an escape hatch: a full C# `INode` implementation. So you have **two node kinds**: declarative (manifest-driven, the majority) and programmatic (hand-written, the exceptions).

### 8.3 The OAuth2 connection flow

This is the piece that lets companies connect their Google (or any) account securely without sharing passwords:

```
1. User clicks "Connect Google" in your UI.
2. Frontend → GET /api/integrations/google/connect
3. Backend builds the provider's OAuth consent URL (client_id, scopes,
   redirect_uri, state) and returns it. Browser redirects to Google.
4. User logs into Google & grants consent.
5. Google redirects back → GET /api/integrations/oauth/callback?code=...&state=...
6. Backend exchanges the `code` for access_token + refresh_token.
7. Backend encrypts & stores them as an AppConnection row.
8. User is sent back to your app; the connection now appears, ready to use.

Later, when a node runs:
9. Engine loads the AppConnection, checks token expiry.
10. If expired, uses the refresh_token to get a new access_token (auto-refresh).
11. Injects "Authorization: Bearer <token>" into the connector's HTTP call.
```

You'll register **one OAuth app per provider** (one Google Cloud OAuth client, one Slack app, etc.) with their redirect URI pointing at your `/api/integrations/oauth/callback`. Your platform acts as the OAuth *client*; your users are the resource owners.

### 8.4 Triggers from external apps

Integrations also provide **triggers** (start a workflow when something happens in the app). Two mechanisms:

| Mechanism | How it works | Use when |
|---|---|---|
| **Webhook / push** | The app POSTs to your `/api/webhooks/...` when an event occurs (e.g. Stripe, GitHub, Slack events). Real-time. | The app supports outgoing webhooks. |
| **Polling** | A scheduled job calls the app's API every N minutes and diffs against last-seen state (e.g. "new row in Sheet"). | The app has no webhooks. |

Both reuse infrastructure you already planned: webhooks → the webhook endpoint + queue; polling → the Hangfire/Quartz scheduler.

### 8.5 Architecture additions for integrations

```
┌─────────────────────────────────────────────────────────────┐
│                     ASP.NET Core API                          │
│  IntegrationsController   OAuthController (connect/callback)  │
│  ConnectorRegistry (loads manifests at startup)               │
└───────────┬───────────────────────────┬──────────────────────┘
            │                            │
   ┌────────▼─────────┐        ┌─────────▼──────────────┐
   │ OAuthService      │        │ app_connections table  │
   │ • build consent   │        │ (encrypted tokens,     │
   │   URL             │        │  expiry, provider)     │
   │ • exchange code   │        └────────────────────────┘
   │ • refresh tokens  │
   └───────────────────┘
            │ tokens injected into
   ┌────────▼───────────────────────────────────────────┐
   │ DeclarativeConnectorNode  (generic engine)          │
   │  reads manifest → templates URL/body → HTTP call    │
   └─────────────────────────────────────────────────────┘
```

New building blocks:
- **`ConnectorRegistry`** — loads all connector manifests at startup; serves them to the frontend (so the node palette shows every available app) and to the engine.
- **`IOAuthProvider`** — per-provider config (Google, Slack, Microsoft…): authorize URL, token URL, scopes. Most OAuth2 providers share one generic implementation; the differences are just config.
- **`OAuthService`** — builds consent URLs, exchanges codes, refreshes tokens.
- **`DeclarativeConnectorNode`** — one C# class that executes *any* manifest-defined action.
- **`ITokenStore`** — encrypted read/write of connection tokens, with auto-refresh on read.

### 8.6 Data model addition

Add an `app_connections` table (an evolution of the `credentials` table from §5 — you can either extend `credentials` or add this alongside it):

#### `app_connections`
| Column | Type | Notes |
|---|---|---|
| id | uuid (PK) | |
| user_id | uuid (FK → users) | who owns this connection |
| app_key | text | e.g. `google-sheets`, `slack` |
| display_name | text | "My work Gmail" |
| auth_type | text | `oauth2`, `apiKey` |
| encrypted_access_token | bytea/text | **encrypted** |
| encrypted_refresh_token | bytea/text | **encrypted**, nullable |
| token_expires_at | timestamptz | drives auto-refresh |
| scopes | text | granted scopes |
| metadata | jsonb | provider account id, email, etc. |
| created_at / updated_at | timestamptz | |

A declarative node's parameters then include a **`connectionId`** referencing this table, instead of raw secrets.

### 8.7 Letting *others* add integrations (future "platform" angle)

If you later want third parties ("some company") to publish their own integrations onto your platform:
- Define a **public connector manifest schema** + validation.
- Let developers submit a manifest (and register their OAuth app details) through an admin/partner portal.
- Version and review manifests; load approved ones into the `ConnectorRegistry`.
- This is effectively building an **app marketplace** — a large undertaking, so treat it as a long-term goal *after* your own first-party connectors (Google, Slack) work end-to-end.

### 8.8 New API endpoints for integrations
```
GET  /api/connectors                         # list available apps (from manifests)
GET  /api/connectors/{appKey}                # one app's actions/triggers schema
GET  /api/integrations/{provider}/connect    # returns OAuth consent URL
GET  /api/integrations/oauth/callback        # OAuth redirect target (code → tokens)
GET  /api/connections                        # user's connected accounts
DELETE /api/connections/{id}                 # disconnect / revoke
```

> **Build order recommendation:** get *one* provider working fully — Google is a good first choice — before generalizing. Concretely: Google OAuth connect → store tokens → one declarative action (Sheets "Append Row") → prove it runs inside a workflow. Once that vertical slice works, every other OAuth app is mostly a new manifest + provider config.

---

## 9. Frontend Design (React)

> **You said you don't know frontend yet — this section is written to be followed step by step.** The golden rule: **don't hand-build buttons, inputs, dropdowns, modals, and toggles.** Use a ready-made component library so everything looks consistent and professional from day one, and you spend your energy on *layout* and *behavior*, not pixels.

### 9.1 Design system & tooling (decide once, reuse everywhere)

| Concern | Use | Why it helps a beginner |
|---|---|---|
| Styling | **Tailwind CSS** | You style with utility classes in your JSX — no separate CSS files to manage. |
| Components | **shadcn/ui** | Copy-paste, accessible, good-looking components (Button, Dialog, Tabs, Dropdown, Avatar…). You own the code and can tweak it. |
| Icons | **lucide-react** | One clean, consistent icon set. |
| Forms | **React Hook Form + Zod** | Handles validation and error messages for you. |
| Notifications | **sonner** (toast) | "Saved!" / "Error" popups in one line. |

**The design principles to follow (you don't need to be a designer):**
1. **Spacing & alignment beat decoration.** Generous padding, consistent gaps, left-aligned text → looks professional.
2. **One accent color.** Pick a single brand color (say indigo/violet) for primary buttons and active states. Everything else is grey.
3. **A clear hierarchy.** Page title large/bold, section headers medium, body small-grey. Limit yourself to ~3 text sizes.
4. **Reuse layout shells.** Every signed-in page sits inside the same frame (sidebar + top bar). Build it once as `<AppLayout>`.

### 9.2 Theming — light & dark mode

This is simpler than it looks if you set it up correctly *from the start*:

1. **Define colors as CSS variables (design tokens),** not hard-coded hex values. e.g. `--color-bg`, `--color-surface`, `--color-text`, `--color-border`, `--color-primary`.
2. Give light values on `:root` and dark values under a `.dark` class (Tailwind's `darkMode: 'class'` strategy).
3. A **theme toggle** flips the `dark` class on `<html>`.
4. **Persist the choice** in `localStorage`, and default to the OS preference (`prefers-color-scheme`) on first visit.

```css
:root {
  --color-bg: #ffffff;        --color-surface: #f8fafc;
  --color-text: #0f172a;      --color-border: #e2e8f0;
  --color-primary: #6366f1;
}
.dark {
  --color-bg: #0b0f17;        --color-surface: #141a24;
  --color-text: #e5e7eb;      --color-border: #1f2937;
  --color-primary: #818cf8;
}
```

```tsx
// Minimal theme store (Zustand) — apply on load + toggle
function applyTheme(mode: 'light' | 'dark' | 'system') {
  const dark = mode === 'dark'
    || (mode === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.classList.toggle('dark', dark);
  localStorage.setItem('theme', mode);
}
```

> Because you used **tokens**, dark mode "just works" everywhere and you'll never fight hard-coded colors later. The React Flow canvas also reads these variables so nodes look right in both themes.

### 9.3 App layout & navigation

Every authenticated page shares one shell. Keep it calm and predictable:

```
┌──────────────────────────────────────────────────────────────┐
│  ☰  Logo        Workflows   Executions   Integrations   🔔 🌓 👤│  ← top bar
├───────────┬──────────────────────────────────────────────────┤
│ Sidebar   │                                                    │
│ • Home    │              Page content                          │
│ • Flows   │              (cards, tables, canvas…)              │
│ • Runs    │                                                    │
│ • Apps    │                                                    │
│ • Settings│                                                    │
└───────────┴──────────────────────────────────────────────────┘
```
- **Top bar:** global nav, notifications, the **theme toggle (🌓)**, and the **user avatar menu (👤 → Profile, Settings, Log out)**.
- **Sidebar:** primary navigation; collapsible on small screens.
- **Responsive:** on mobile the sidebar becomes a slide-over drawer. Build the layout mobile-first (it's easier than retrofitting).

### 9.4 Page / route map
```
/login, /register, /forgot-password        → auth (no shell)
/                                           → dashboard (recent runs, quick stats)
/workflows                                  → list (cards/table), create new
/workflows/:id/editor                       → the canvas (React Flow) ← the star
/executions                                 → run history
/executions/:id                             → run detail (per-node input/output)
/integrations                               → connected apps + "Connect" buttons
/settings/profile                           → name, email, avatar, password
/settings/appearance                        → theme (light/dark/system)
/settings/connections                       → OAuth connections (see §8)
/settings/security                          → password, active sessions, sign-out-all
/workflows/:id/share                         → manage collaborators & permissions
```

### 9.5 The editor (React Flow) — the most important screen

- **Left panel:** node palette, grouped (Triggers / Logic / Apps). Fetched from `GET /api/node-types` + `GET /api/connectors`. Drag a node onto the canvas (or search).
- **Canvas:** React Flow with custom node components (icon, label, status color, input/output handles). Pan/zoom, minimap, snap-to-grid.
- **Right panel:** parameter form for the selected node, **auto-generated** from the node/connector definition via React Hook Form + Zod.
- **Top bar:** Save, Run, Activate toggle, version indicator, and (new) a **collaborators avatar stack** + **Share** button.

```
┌──────────┬────────────────────────────────┬──────────────┐
│ Palette  │          Canvas (React Flow)   │  Node Config │
│ Triggers │   [Trigger]──▶[HTTP]──▶[IF]     │  method: GET │
│ Logic    │                     │          │  url: ...    │
│ Apps     │                     ▼          │              │
│  Gmail   │                 [Gmail]        │  [Save]      │
│  Sheets  │                                │              │
└──────────┴────────────────────────────────┴──────────────┘
```

### 9.6 State management
- **TanStack Query** for all server data (workflows, executions, node types, connectors) — handles caching & refetch.
- **Zustand** for editor-local + UI state (selected node, unsaved canvas changes, dirty flag, theme).
- The React Flow graph (nodes/edges) is serialized to the `graph` jsonb on Save.

### 9.7 Live execution view
- On "Run", call the API, get `executionId`, open a SignalR subscription.
- As `NodeStarted` / `NodeCompleted` events arrive, update the corresponding React Flow node's style (border color, status badge, spinner).

### 9.8 Account Settings

A settings area reachable from the avatar menu, built with **tabs** (shadcn `Tabs`) so each concern has its own panel:

| Tab | What the user can do |
|---|---|
| **Profile** | Edit display name, avatar (upload image), and view email. |
| **Account / Email** | Change email (with re-verification), change password. |
| **Appearance** | Choose theme: Light / Dark / System (the toggle from §9.2). |
| **Connections** | See and manage OAuth-connected apps (from §8); disconnect. |
| **Security** | Change password, see active sessions/devices, "sign out everywhere". |
| **Danger zone** | Delete account (with confirmation dialog). |

Backing endpoints (small additions, no new layering):
```
GET   /api/me                      # current user profile
PUT   /api/me                      # update name / avatar
PUT   /api/me/password             # change password
POST  /api/me/avatar               # upload avatar image
GET   /api/me/sessions             # active sessions
POST  /api/me/sessions/revoke-all  # sign out everywhere
```

### 9.9 Collaboration & granular permissions

Let a workflow owner invite others and control exactly what each can do — your requested **read / write / view / view-specific-parts** model.

**Roles (coarse-grained, pick one per collaborator):**

| Role | Can view | Can edit graph | Can run | Can manage sharing | Notes |
|---|---|---|---|---|---|
| **Owner** | ✅ | ✅ | ✅ | ✅ | creator; only one (transferable) |
| **Editor (write)** | ✅ | ✅ | ✅ | ❌ | full edit, can't change collaborators |
| **Runner** | ✅ | ❌ | ✅ | ❌ | can trigger runs, not edit |
| **Viewer (read)** | ✅ | ❌ | ❌ | ❌ | read-only |

**Granular "view specific parts" (fine-grained flags layered on top of the role):**
Some things are sensitive even for people allowed to see the workflow. So a collaborator's access is `role` **+** a set of visibility flags:

| Flag | Controls | Default for Viewer |
|---|---|---|
| `canViewCredentials` | see which connection/credential a node uses (never the secret itself) | off |
| `canViewExecutionData` | see the actual input/output data flowing through nodes | off |
| `canViewExecutionHistory` | see the list of past runs | on |
| `nodeVisibility` | optionally hide specific nodes/groups from this person | all visible |

So you can share a workflow where someone **sees the shape of the flow but not the data**, or an editor who can change logic but can't see which credentials are wired in. On the canvas, hidden/locked parts render as greyed "🔒 Restricted" placeholders, and the config panel is read-only when the role doesn't allow editing.

**Minimal data model to support this (one table — not heavy architecture):**

#### `workflow_shares`
| Column | Type | Notes |
|---|---|---|
| id | uuid (PK) | |
| workflow_id | uuid (FK) | |
| user_id | uuid (FK) | the collaborator |
| role | text enum | `Owner`, `Editor`, `Runner`, `Viewer` |
| permissions | jsonb | the granular flags above |
| invited_by | uuid (FK) | |
| created_at | timestamptz | |

**How it's enforced (keep it simple):**
- **Backend is the source of truth.** Every workflow endpoint checks the caller's role/flags before acting (a small `IAuthorizationService` / policy — e.g. "EditWorkflow requires Owner or Editor"). The backend also **strips** data the viewer isn't allowed to see *before* sending the response (e.g. omit execution data, mask credential references).
- **Frontend just reflects it.** The API returns the caller's effective permissions with the workflow; the UI hides/disables buttons and greys restricted nodes accordingly. (Frontend hiding is UX only — never the security boundary.)

**The sharing UI (`/workflows/:id/share`):** a dialog listing collaborators (avatar, name, role dropdown), an "Invite by email" field, and an expandable "Advanced permissions" area with the granular toggles per person.

```
┌─────────────── Share "Lead Sync Flow" ───────────────┐
│  Invite:  [ email@company.com        ]  [ Editor ▾ ]  │
│                                                       │
│  👤 Asha      Owner                                   │
│  👤 Ravi      Editor ▾     ⚙ Advanced                │
│  👤 Meera     Viewer ▾     ⚙ Advanced                │
│        └ ☐ View execution data   ☑ View history       │
│          ☐ View credentials                           │
│                                              [ Done ] │
└───────────────────────────────────────────────────────┘
```

---

## 10. Backend Design (ASP.NET Core)

### 10.1 Suggested solution structure (Clean-ish Architecture)

```
WorkflowPlatform.sln
├── src/
│   ├── WorkflowPlatform.Api            # Controllers, SignalR hubs, DI, startup
│   ├── WorkflowPlatform.Application    # Use cases, DTOs, interfaces, validators
│   ├── WorkflowPlatform.Domain         # Entities, enums, core logic (no deps)
│   ├── WorkflowPlatform.Engine         # Execution engine + node implementations
│   ├── WorkflowPlatform.Infrastructure # EF Core, repositories, queue, encryption
│   └── WorkflowPlatform.Worker         # (Phase 4) standalone execution host
└── tests/
    ├── WorkflowPlatform.UnitTests
    └── WorkflowPlatform.IntegrationTests
```

> Don't over-engineer the layering on day one. It's fine to start with API + a couple of services and refactor into these projects as it grows. The boundary that matters most early is **isolating the Engine** so it's independently testable.

### 10.2 Key cross-cutting pieces
- **Auth:** ASP.NET Core Identity for user storage; issue JWTs on login; `[Authorize]` on controllers.
- **Webhook endpoints:** anonymous but token-protected (`/api/webhooks/{workflowId}/{token}`); look up the workflow, enqueue an execution.
- **Scheduling:** Hangfire/Quartz job that, per active schedule-trigger workflow, enqueues executions on its cron.
- **Validation:** FluentValidation on all incoming DTOs.
- **Error handling:** global exception middleware → consistent problem-details responses.

---

## 11. Development Roadmap

Organized as **phases**, each ending in something runnable. Rough solo-dev estimates — adjust to your pace; the goal is learning, not speed.

### Phase 0 — Foundations (≈1 week)
- Set up repo, solution structure, Docker Compose (Postgres).
- "Hello World" API + React app talking to each other.
- EF Core connected, first migration, Swagger up.
- **Milestone:** React page fetches a value from the API.

### Phase 1 — Core CRUD + Canvas (≈2–3 weeks)
- Auth (register/login, JWT).
- Workflow CRUD + versioning.
- React Flow editor: add/connect/move nodes, save graph to backend, reload it.
- **Milestone:** You can draw a workflow and it persists.

### Phase 2 — The Execution Engine (≈3–4 weeks) ← the big one
- `INode`, `NodeRegistry`, `NodeExecutionContext`.
- Implement `ManualTrigger`, `Set`, `HttpRequest`, `IF` nodes.
- Topological engine; persist `executions` + `node_executions`.
- In-process queue (`Channel<T>` + `IHostedService`).
- "Run" button → execute → show results per node.
- **Milestone:** A 3-node workflow runs end-to-end and you can inspect each node's output.

### Phase 3 — Real-time + Triggers (≈2–3 weeks)
- SignalR: live node status on the canvas during a run.
- Webhook trigger endpoint.
- Scheduled trigger via Hangfire/Quartz.
- Execution history UI.
- **Milestone:** A scheduled workflow runs itself and you watch it live.

### Phase 4 — Hardening & Scale (≈3–4 weeks)
- Credential management (encrypted) + resolution in nodes.
- Extract the Worker into a separate service.
- Swap in-process queue for RabbitMQ/Redis.
- Retries, timeouts, cancellation.
- Expression evaluator.
- **Milestone:** API and Worker run as separate containers; multiple workers.

### Phase 5 — Integrations / App Connectors (≈3–4 weeks)
Depends on the encrypted-credentials work from Phase 4.
- `app_connections` table + `ITokenStore` (encrypted, auto-refresh).
- `OAuthService` + a generic `IOAuthProvider`; wire up **Google** as the first provider.
- `/connect` + `/oauth/callback` endpoints; connect-account UI.
- Connector manifest schema + `ConnectorRegistry` (load at startup).
- `DeclarativeConnectorNode` engine; ship one action end-to-end (Google Sheets "Append Row").
- Frontend: render connector nodes & auto-generated parameter forms from manifests.
- **Milestone:** Connect your Google account in the UI and run a workflow that appends a row to a real spreadsheet — no code written for the node.

### Phase 6 — Frontend polish, Account Settings & Collaboration (≈2–3 weeks)
- Design system baseline: Tailwind + shadcn/ui, `<AppLayout>` shell, light/dark theme tokens + toggle.
- Account Settings area (Profile, Appearance, Connections, Security tabs).
- `workflow_shares` table + role/permission authorization policies on workflow endpoints.
- Share dialog UI with roles (Owner/Editor/Runner/Viewer) + granular visibility flags.
- Backend strips restricted data from responses; frontend greys/locks restricted parts.
- **Milestone:** Invite a second account as a "Viewer who can't see execution data" and confirm the restriction holds on both API and UI.

### Phase 7 — Ongoing polish (continuous)
- More nodes (Email, Slack, database, delay/wait).
- Tests (unit for engine, integration with Testcontainers).
- CI/CD (GitHub Actions), deployment.
- Observability (Serilog + a dashboard).

---

## 12. Step-by-Step Implementation Plan

A concrete task checklist for the first three phases — the ones that get you to a *working engine*.

### Setup
1. `dotnet new sln -n WorkflowPlatform`
2. Create the API project: `dotnet new webapi -n WorkflowPlatform.Api`.
3. `npm create vite@latest` → React + TypeScript for the frontend.
4. Write `docker-compose.yml` with a `postgres` service (and `pgadmin` if you like).
5. Add EF Core + Npgsql packages; create `AppDbContext`; configure connection string in user-secrets.
6. `dotnet ef migrations add Init && dotnet ef database update`.
7. Enable Swagger + CORS (allow the Vite dev origin). Verify React → API call works.

### Auth
8. Add ASP.NET Core Identity + JWT bearer auth.
9. `POST /api/auth/register`, `POST /api/auth/login` returning a JWT.
10. React: login form, store token, attach it to an Axios interceptor, a protected route.

### Workflows & editor
11. `Workflow` + `WorkflowVersion` entities + migration.
12. CRUD endpoints for workflows; `PUT` saves the graph as a new version.
13. Install `@xyflow/react`; build the editor page with drag-from-palette, connectable nodes.
14. Serialize React Flow state → `graph` JSON on Save; deserialize on load.
15. `GET /api/node-types` returning node definitions; render the palette from it.

### Engine
16. Define `INode`, `NodeResult`, `NodeExecutionContext`, `NodeDefinition`.
17. Build `NodeRegistry` (DI-registered dictionary by type string).
18. Implement nodes: `ManualTriggerNode`, `SetNode`, `HttpRequestNode`, `IfNode`.
19. Write `WorkflowExecutor`: parse graph → topological order → run nodes → persist results.
20. Add `Execution` + `NodeExecution` entities + migration.
21. In-process queue: `Channel<Guid>` + a `BackgroundService` that runs the executor.
22. `POST /api/workflows/{id}/run` → create Execution (Queued) → enqueue → return id.
23. `GET /api/executions/{id}` → return execution + node executions.
24. React: "Run" button, poll or (next phase) subscribe; execution-detail page showing per-node input/output JSON.

### Real-time & triggers
25. Add SignalR hub `/hubs/executions`; executor pushes `NodeStarted/Completed`.
26. React SignalR client colors nodes live during a run.
27. Webhook endpoint `POST /api/webhooks/{workflowId}/{token}` → enqueue execution with the request body as trigger data.
28. Add Hangfire; a recurring job scans active schedule triggers and enqueues runs per cron.

> After step 28 you have a **genuinely functional workflow automation platform**. Everything after is refinement and scale.

---

## 13. Scalability Strategy

Design for it, but **don't build it all upfront** — the roadmap introduces each piece when it's needed.

### 12.1 Stateless API → horizontal scale
Keep the API stateless (no in-memory session; JWT auth). Then you can run N API instances behind a load balancer. SignalR across instances needs a **backplane** (Redis) so events reach the right client regardless of which instance holds the connection.

### 12.2 Separate, scalable workers
The API/Worker split (Phase 4) is the core scalability move:
- Execution load is handled by workers, not the API.
- Run more worker instances to process more concurrent executions.
- A durable queue (RabbitMQ/Redis Streams) buffers spikes and survives crashes (at-least-once delivery → design nodes to tolerate retries / be idempotent).

### 12.3 Database scaling
- Start with one Postgres.
- Add **read replicas** for history/reporting queries.
- **Partition** `executions` / `node_executions` by time; add a **retention job** to archive/delete old runs (these tables dominate growth).
- Consider moving large node I/O payloads to object storage (e.g. S3/MinIO) and storing only references.

### 12.4 Caching
- Redis for node-type definitions, hot workflow versions, and rate-limiting webhooks.

### 12.5 Observability (prerequisite for scaling confidently)
- Structured logs (Serilog), metrics (OpenTelemetry → Prometheus/Grafana), and tracing across API → queue → worker so you can find bottlenecks.

### 12.6 Scaling checklist
| Signal | Response |
|---|---|
| API slow under load | Add API instances + Redis SignalR backplane |
| Executions backing up | Add worker instances |
| DB CPU high on reads | Add read replica |
| `executions` table huge | Partition + retention job |
| Large payloads bloating DB | Offload to object storage |
| Webhook abuse | Redis rate limiting + token validation |
| Hitting 3rd-party API rate limits | Per-app/per-connection rate limiting + backoff in the connector engine |

---

## 14. Security Considerations

- **Encrypt credentials at rest** (AES / Data Protection); keep keys out of the DB.
- **Never log secrets** — scrub credential values from node I/O before persisting/logging.
- **SSRF risk:** the `HttpRequest` node lets users hit arbitrary URLs. For a personal project it's fine; if you ever expose it, block internal IP ranges (`169.254.*`, `10.*`, `localhost`, cloud metadata endpoints).
- **Webhook tokens:** long random, per-workflow; validate before executing.
- **OAuth tokens (integrations):** encrypt access *and* refresh tokens at rest; store the `state` parameter and verify it on callback to prevent CSRF; request the **minimum scopes** each connector needs; support disconnect/token revocation; never expose tokens to the frontend.
- **Multi-tenant isolation:** always filter queries by the authenticated `user_id` so users can't read each other's workflows/executions.
- **Input validation** everywhere (FluentValidation).
- **Rate limit** auth and webhook endpoints.

---

## 15. Learning Outcomes

By finishing this you'll have practiced, concretely:

- **Graph algorithms** — topological sort, DAG traversal (the engine).
- **Async & background processing** — `Channel<T>`, `BackgroundService`, queues.
- **Real-time** — SignalR, WebSockets, pub/sub backplanes.
- **Clean architecture & DI** — the registry/plugin pattern for extensible nodes.
- **EF Core & data modeling** — relational + `jsonb` hybrid design.
- **React advanced UI** — React Flow, custom nodes, dynamic forms, live updates.
- **Distributed systems basics** — decoupling via queues, idempotency, horizontal scale.
- **Security** — encryption at rest, JWT auth, SSRF awareness.

### Recommended first slice (do this literally this week)
> Build the smallest possible vertical: an API that, given a hardcoded 2-node graph (`ManualTrigger → HttpRequest`), executes it synchronously and returns the HTTP node's output. No DB, no React, no queue. Once that runs in a console/test, you understand the engine — everything else is wiring around it.

---

### Reference projects to study (read their source/docs for ideas)
- **n8n** — the closest match to your vision; study its node & expression model.
- **Elsa Workflows** — a .NET-native workflow engine; see how it models activities and persistence in C#.
- **React Flow docs & examples** — invest here early.

---

*Good luck — build the engine first, keep the scope tight, and let each phase end in something you can actually run.*
