# Onebox

Onebox is a production-oriented modular monolith for centralized WhatsApp customer communication. The first usable milestone is implemented end to end: Meta webhook ingestion → durable RabbitMQ processing → PostgreSQL contact/conversation/ticket/message persistence → targeted SignalR events → agent inbox → concurrency-safe claim → queued outbound Meta send.

## Architecture

The backend follows Clean Architecture dependency direction:

- `CentralChat.Domain` — entities, lifecycle rules, and enums with no infrastructure dependencies.
- `CentralChat.Application` — use-case contracts, DTOs, permissions, validation, and application exceptions.
- `CentralChat.Infrastructure` — EF Core/PostgreSQL, Identity, JWT issuance, RabbitMQ, Redis SignalR backplane, Meta client, outbox/inbox workers, audit and realtime routing.
- `CentralChat.API` — REST controllers, webhook, SignalR hub mapping, rate limiting, CORS, ProblemDetails middleware, health checks, and development seed.
- `frontend` — Next.js App Router/TypeScript agent workspace. `src/lib` holds the typed API client,
  session storage and formatting helpers, `src/hooks` the SignalR subscription, `src/components` the
  inbox panels, and `src/app/page.tsx` only composes them.

Backend types are one-per-file and named after the type they contain, so a class can be found from its
name alone: `Permissions`, `Dtos`, `Abstractions` and `Exceptions` in Application; `TicketService` and
`ConversationService` in Infrastructure; `CurrentUser`, `ExceptionMiddleware`, `DatabaseInitializer`
and `HealthChecks` in the API.

PostgreSQL is authoritative. RabbitMQ transports durable integration work, Redis provides SignalR scale-out, and SignalR only provides realtime hints; the UI always reloads authoritative REST state after reconnecting.

Reliability controls include:

- unique webhook payload hashes and external message IDs;
- a transactional database outbox;
- durable RabbitMQ queues, publisher confirms, redelivery and a dead-letter queue;
- a consumer inbox plus idempotent aggregate processing;
- a single conditional database update for ticket claiming;
- persisted assignment history and administrative audit logs;
- server-side ownership checks before outbound sends;
- HMAC-SHA256 Meta signature validation outside explicit development configuration.

## Prerequisites

- .NET SDK 8
- Node.js 22+ and npm
- Docker Desktop

## Run locally

Start infrastructure:

```powershell
docker compose up -d postgres redis rabbitmq
docker compose ps
```

PostgreSQL is exposed on `localhost:55432` because the common local PostgreSQL port may already be in use. Redis uses `6379`; RabbitMQ uses `5672`; its management UI is at [http://localhost:15672](http://localhost:15672) with `centralchat` / `centralchat_dev` for local development.

Run the API (it applies migrations and seeds development data in Development):

```powershell
dotnet tool restore
dotnet restore CentralChat.sln
dotnet run --project src/CentralChat.API
```

Run the frontend:

```powershell
cd frontend
npm install
npm run dev
```

Open [http://localhost:3000](http://localhost:3000). Swagger is available in Development at [http://localhost:8080/swagger](http://localhost:8080/swagger).

To run the complete stack in containers:

```powershell
Copy-Item .env.example .env
docker compose up -d --build
```

## Development accounts

All seeded accounts use the development-only password `CentralChat1!`:

| Account | Role |
|---|---|
| `superadmin@example.local` | SuperAdmin |
| `admin@example.local` | Admin |
| `lead@example.local` | TeamLead |
| `agent1@example.local` | Agent |
| `agent2@example.local` | Agent |

The seed also creates a `Sales` team containing both agents. Seed execution is environment-gated and is not intended for production.

## Production accounts

Roles, permission records, and role/permission mappings are seeded in every environment, because JWT
permission claims are read back from those tables at login. The `@example.local` users above are
seeded only in Development.

Every other environment creates its first accounts from `Bootstrap__*` configuration, so no
credentials live in this repository:

| Setting | Purpose |
|---|---|
| `Bootstrap__AdminEmail` | Administrator sign-in address. Bootstrapping is skipped unless this and the password are both set. |
| `Bootstrap__AdminPassword` | Administrator password. Must satisfy the Identity policy: at least 10 characters with an uppercase letter, a lowercase letter, a digit, and a symbol. |
| `Bootstrap__AdminDisplayName` | Defaults to `Administrator`. |
| `Bootstrap__AdminRole` | Defaults to `SuperAdmin`; any of `SuperAdmin`, `Admin`, `TeamLead`, `Agent`. |
| `Bootstrap__AgentEmails` | Comma-separated agent addresses. Display names are derived from the address local part. |
| `Bootstrap__AgentPassword` | Shared password for those agents. |
| `Bootstrap__TeamName` | Team the agents join. Defaults to `Support`. |

The seeder is idempotent: it creates only what is missing and never rewrites an existing account's
password, so it is safe on every restart. Removing the variables after the accounts exist leaves them
in place. A rejected password or address is logged as an error rather than thrown, so a typo cannot
stop the host from starting and ingesting webhooks — check the startup log if an account is missing.

There is no user-management endpoint yet, so accounts can currently only be created this way.

## Configuration and secrets

Configuration uses strongly typed `Jwt`, `RabbitMq`, and `MetaWhatsApp` option sections. Use environment variables (`__` separates nested keys), .NET User Secrets, or a production secret manager. Never place real Meta/JWT/database credentials in `appsettings.json` or frontend variables.

Important settings:

- `ConnectionStrings__PostgreSql`
- `ConnectionStrings__Redis`
- `RabbitMq__Host`, `RabbitMq__UserName`, `RabbitMq__Password`
- `Jwt__SigningKey`, `Jwt__Issuer`, `Jwt__Audience`
- `MetaWhatsApp__VerifyToken`, `MetaWhatsApp__AppSecret`, `MetaWhatsApp__AccessToken`
- `MetaWhatsApp__ValidateSignature`
- `MetaWhatsApp__UseDevelopmentClient`
- `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword`, `Bootstrap__AgentEmails`, `Bootstrap__AgentPassword`

Development explicitly disables signature validation and uses `DevelopmentWhatsAppClient`. Production defaults do not silently fall back to fake delivery.

Outside Development the host logs a warning at startup if `MetaWhatsApp__ValidateSignature` is off or
`MetaWhatsApp__UseDevelopmentClient` is on, because either setting silently breaks a deployment: the
first accepts unsigned webhook payloads from anyone, the second never delivers replies to Meta.

## Serving the frontend behind a reverse proxy

`NEXT_PUBLIC_API_URL` is baked into the frontend bundle at build time, and the paths in the frontend
already carry their own prefixes (`/api/auth/login`, `/hubs/communication`). When the proxy serves the
frontend and the API from one origin and forwards `/api/*`, `/hubs/*`, and `/webhook` to the API
**without stripping the prefix**, build the image with an empty `NEXT_PUBLIC_API_URL` so those paths
resolve same-origin. Setting it to `/api` against such a proxy produces `/api/api/auth/login` and every
request 404s. Local development keeps the `http://localhost:8080` default because the frontend runs on
a separate origin.

## Meta webhook setup

Configure Meta to call:

```text
GET/POST https://<public-host>/webhook
```

The GET handler validates `hub.mode`, `hub.challenge`, and `hub.verify_token`. The POST handler validates `X-Hub-Signature-256`, stores and deduplicates the raw event, inserts an outbox item, and returns quickly. The RabbitMQ consumer safely traverses every entry/change/message, resolves the receiving `phone_number_id`, and processes message/status events asynchronously.

For local Meta testing, expose port 8080 through a trusted HTTPS tunnel and set the callback URL and verification token in Meta Developer configuration.

## Main API routes

```text
POST /api/auth/login
POST /api/auth/refresh
POST /api/auth/logout

GET  /api/tickets?scope=mine|unassigned|all&status=active|new|open|pending|resolved|closed|all
GET  /api/tickets/mine
GET  /api/tickets/unassigned
POST /api/tickets/{id}/claim
POST /api/tickets/{id}/assign
POST /api/tickets/{id}/reassign
POST /api/tickets/{id}/unassign
POST /api/tickets/{id}/resolve
POST /api/tickets/{id}/close
POST /api/tickets/{id}/reopen

GET  /api/conversations/{id}
GET  /api/conversations/{id}/messages?limit=50&before=<messageId>
POST /api/conversations/{id}/messages

GET  /api/contacts
GET  /api/contacts/{id}
GET  /api/users
GET  /api/teams

GET  /webhook
POST /webhook
GET  /health/live
GET  /health/ready
WS   /hubs/communication
```

List routes are bounded; messages use a cursor and tickets/contacts use page/page-size limits.

Enum-valued fields (ticket status, message direction/type/status) are serialised as strings rather than
integers.

## Ticket lifecycle

A ticket moves `New → Open` when it is claimed or assigned, and reaches a terminal state through
`/resolve` or `/close`. `/reopen` returns a terminal ticket to `Open` and clears its resolution and
closure timestamps. Invalid transitions return `409`; only the assigned agent, or a caller holding
`tickets.assign`, may change a ticket's status.

An agent may hand a ticket back with `/unassign`, which clears the assignment but leaves the ticket
`Open` with its history intact, so it returns to the unassigned queue for another agent to claim.
Agents may release only their own tickets; taking a ticket off another agent still requires
`tickets.assign`.

The two terminal states differ in what happens to contact ownership:

- **Resolved** keeps `Contact.CurrentAssignedAgentId`, so a customer who replies gets a fresh ticket
  routed straight back to the same agent.
- **Closed** releases the contact once no other active ticket references it, so the next inbound
  message arrives in the unassigned queue. Reopening restores the ticket's agent as contact owner.

Both transitions write an audit log entry, and any contact-ownership change is additionally recorded
in assignment history. `GET /api/tickets` defaults to `status=active`, which excludes resolved and
closed tickets so queues do not grow without bound.

## Migrations and tests

```powershell
dotnet tool restore
dotnet ef database update --project src/CentralChat.Infrastructure --startup-project src/CentralChat.API
dotnet build CentralChat.sln
dotnet test CentralChat.sln

cd frontend
npm run lint
npm run build
```

`CentralChat.UnitTests` covers domain rules: ticket assignment and lifecycle transitions, contact
ownership, and outbound message state.

`CentralChat.IntegrationTests` runs against a real PostgreSQL, because the behaviour it protects is
enforced by unique indexes and an in-memory provider would pass while the bug was present. It covers
webhook ingestion: several messages from one new contact in a single payload, several contacts, replay
deduplication, and media extraction. Point `TEST_POSTGRES` at a server to run them:

```powershell
$env:TEST_POSTGRES = "Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=postgres"
dotnet test CentralChat.sln
```

Without that variable they are skipped rather than failed, so a checkout with no database still tests
cleanly. CI supplies one as a service container. Each test creates and drops its own database.

Still uncovered: outbox and inbox idempotency, concurrent claiming, and ownership denial on send.

## Realtime event names

The hub uses targeted `user:{id}`, `conversation:{id}`, and `unassigned` groups. Current events include `message.received`, `message.sent`, `message.failed`, `ticket.created`, `ticket.claimed`, `ticket.removed`, `ticket.assignment.added`, `ticket.assignment.removed`, and `ticket.status.changed`. Clients reconnect automatically and refresh REST state.

`ticket.status.changed` carries `{ ticketId, status, previousStatus, conversationId }` and is delivered to the conversation group plus either the assigned agent or the unassigned queue.
