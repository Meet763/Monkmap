# Monkmap — Workflow Automation Platform (MVP)

A working slice of the platform described in [WORKFLOW_PLATFORM_PLAN.md](WORKFLOW_PLATFORM_PLAN.md):
a visual workflow builder with an execution engine, built with **ASP.NET Core** (backend) and
**React + TypeScript** (frontend).

> **What it includes.** Auth, workflow CRUD and versioning, a visual React Flow editor, an execution
> engine with live per-node status over SignalR, webhook triggers, AI agent nodes (Claude and
> OpenAI-compatible models), Google integrations (Calendar, Gmail, Sheets), team accounts with
> invitations and permissions, and workflow locks. Scheduling and more providers are on the roadmap.
>
> Pragmatic choices:
> - **SQL Server** is the default database. PostgreSQL, MySQL and SQLite are also supported (see below).
> - **One backend project** instead of multiple layered projects.

---

## Prerequisites

- .NET SDK 10
- Node.js 20+ and npm

## Run the backend

```bash
cd backend/WorkflowPlatform.Api
dotnet run
```

The API starts on **http://localhost:5080**. Complete the **Configuration and secrets** section
below first. The database is created and migrated on first run.

## Configuration and secrets

The repository contains **no real passwords, keys, or server names**. Placeholders are marked
`__SET_IN_USER_SECRETS__` or `REPLACE_WITH_...` in
[appsettings.json](backend/WorkflowPlatform.Api/appsettings.json). Put your real values in
.NET user secrets, which are stored outside the project folder and are never committed.

Run these once from `backend/WorkflowPlatform.Api` (replace the values in angle brackets):

```bash
dotnet user-secrets init
dotnet user-secrets set "Database:ConnectionString" "Data Source=<server>\<instance>;Initial Catalog=Monkmap;User ID=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True"
dotnet user-secrets set "Jwt:Key" "<a long random string, at least 32 characters>"

# Optional: Google integrations (see TESTING_GUIDE.md, Test 6)
dotnet user-secrets set "Integrations:Google:ClientId" "<client id>"
dotnet user-secrets set "Integrations:Google:ClientSecret" "<client secret>"
```

- **Jwt:Key:** in Development a local-only fallback key is used, so you can start without setting it.
  Outside Development the app refuses to start until a real key is configured.
- **AI keys** (Anthropic, OpenAI) are entered per node in the editor. They are stored in the
  workflow, so treat workflows as sensitive data (see the known limitations in TESTING_GUIDE.md).
- To use a different database, change `Database:Provider` and the connection string. Supported
  values: `SqlServer`, `Postgres`, `Sqlite`. Migrations are generated for SQL Server, so the other
  providers need their own migration set before they can be used. MySQL is only a template for now.

The same values can be set as environment variables with a double underscore, for example
`Database__ConnectionString` and `Jwt__Key`. This is how a server or CI system should provide them.
OpenAPI is available at `http://localhost:5080/openapi/v1.json` in Development.

## Run the frontend

In a second terminal:

```bash
cd frontend
npm install     # first time only
npm run dev
```

Open **http://localhost:5173**. The Vite dev server proxies `/api` and `/hubs` to the backend
on port 5080, so no CORS or URL config is needed.

---

## Try it out

1. **Register** an account.
2. Go to **Workflows → New workflow**.
3. In the editor, add a **Manual Trigger**, a **Set / Edit Fields**, and an **HTTP Request** node
   from the left palette.
4. Connect them: Trigger → Set → HTTP (drag from a node's right handle to the next node's left handle).
5. Configure the **Set** node, e.g. fields:
   ```json
   { "url": "https://jsonplaceholder.typicode.com/todos/1" }
   ```
6. Configure the **HTTP Request** node: method `GET`, URL `{{$json.url}}`.
7. Click **Run**. Watch nodes light up live, then open **View run** to inspect each node's
   input/output.

### Expression syntax
- `{{$json.field}}` — a field from the current node's input.
- `{{$node.NodeId.field}}` — a field from a previous node's output.

---

## Project layout

```
backend/WorkflowPlatform.Api
├── Controllers/     Auth, Workflows, Executions, NodeTypes
├── Data/            EF Core DbContext (SQLite)
├── Dtos/            request/response records
├── Engine/          INode, NodeRegistry, WorkflowExecutor, ExpressionResolver
│   └── Nodes/       ManualTrigger, Set, HttpRequest, If
├── Entities/        User, Workflow, WorkflowVersion, Execution, NodeExecution
├── Hubs/            SignalR ExecutionHub + notifier
└── Services/        JWT, password hashing, in-process execution queue + worker

frontend/src
├── api/             axios client + shared types
├── components/      AppLayout, ThemeToggle, UI primitives
├── pages/           Login, Register, Dashboard, Workflows, Executions, Settings
│   └── editor/      React Flow canvas, custom node, palette, config panel
├── store/           Zustand stores (auth, theme)
└── lib/             SignalR connection factory
```

## Databases

### Platform storage — pick your provider

The platform stores its own data (users, workflows, runs) in a database **you choose**. It ships
with **SQLite** (default, zero-setup), **PostgreSQL** and **SQL Server**, and is designed so you
can add any other database (see "Add your own provider" below).

Configure it in [`appsettings.json`](backend/WorkflowPlatform.Api/appsettings.json):

```json
"Database": {
  "Provider": "Sqlite",
  "ConnectionString": "Data Source=workflowplatform.db"
}
```

Built-in `Provider` values (case-insensitive, aliases accepted):

| Provider | Values | Example connection string |
|---|---|---|
| SQLite | `sqlite` | `Data Source=workflowplatform.db` |
| PostgreSQL | `postgres`, `postgresql` | `Host=localhost;Database=monkmap;Username=postgres;Password=...` |
| SQL Server | `sqlserver`, `mssql` | `Server=(localdb)\\MSSQLLocalDB;Database=Monkmap;Trusted_Connection=True;TrustServerCertificate=True` |

> JSON requires the **doubled backslash** `\\` in SQL Server instance names.

You don't have to edit the file — any config source works, which is handy for deployments and for
keeping secrets out of git:

```bash
# environment variables (note the double underscore)
export Database__Provider=Postgres
export Database__ConnectionString="Host=localhost;Database=monkmap;Username=postgres;Password=secret"

# or user-secrets for local dev
dotnet user-secrets set "Database:Provider" "SqlServer"
```

The schema is created automatically on startup via `EnsureCreated()`. For production, switch to
EF Core migrations (`dotnet ef migrations add Init` → `dotnet ef database update`).

### Add your own provider (e.g. MySQL, Oracle, CockroachDB)

The database layer is a small registry — adding one is three steps and touches nothing else:

1. Add the EF Core provider package, e.g. `dotnet add package Pomelo.EntityFrameworkCore.MySql`.
2. Implement `IDatabaseProvider` in
   [`Data/DatabaseProviders.cs`](backend/WorkflowPlatform.Api/Data/DatabaseProviders.cs)
   (there's a copy-paste MySQL template in that file):
   ```csharp
   public sealed class MySqlDatabaseProvider : IDatabaseProvider
   {
       public string Name => "MySQL";
       public IReadOnlyCollection<string> Keys => new[] { "mysql", "mariadb" };
       public void Configure(DbContextOptionsBuilder options, string cs) =>
           options.UseMySql(cs, ServerVersion.AutoDetect(cs));
   }
   ```
3. Register it in `DatabaseSetup.KnownProviders`
   ([`Data/DatabaseSetup.cs`](backend/WorkflowPlatform.Api/Data/DatabaseSetup.cs)), then set
   `"Provider": "MySql"` in config.

That's it — the DbContext, engine and API are provider-agnostic. If someone sets an unknown
provider, startup fails with a message listing the ones that are available.

### Database workflow node — connect to any database in a flow

Drag the **Database (SQL)** node onto the canvas to read from or write to an external database as a
workflow step. It supports **PostgreSQL, MySQL, SQL Server and SQLite**. Parameters:

- **Provider** — PostgreSQL / MySQL / SqlServer / Sqlite
- **Connection string** — supports `{{ }}` expressions
- **Operation** — `Query` (returns `{ count, rows }`) or `Execute` (returns `{ affectedRows }`)
- **SQL** — raw SQL; supports `{{$json.field}}` / `{{$node.NodeId.field}}` expressions

> The connection string is currently stored in the workflow graph (MVP). Before exposing this to
> other users, move secrets into the encrypted credentials store (plan §5 / §13), and be aware the
> node runs raw SQL by design.
