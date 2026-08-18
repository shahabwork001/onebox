# CentralChat

CentralChat is a production-oriented modular monolith for centralized WhatsApp customer communication. The first usable milestone is implemented end to end: Meta webhook ingestion → durable RabbitMQ processing → PostgreSQL contact/conversation/ticket/message persistence → targeted SignalR events → agent inbox → concurrency-safe claim → queued outbound Meta send.

## Architecture

The backend follows Clean Architecture dependency direction:

- `CentralChat.Domain` — entities, lifecycle rules, and enums with no infrastructure dependencies.
- `CentralChat.Application` — use-case contracts, DTOs, permissions, validation, and application exceptions.
- `CentralChat.Infrastructure` — EF Core/PostgreSQL, Identity, JWT issuance, RabbitMQ, Redis SignalR backplane, Meta client, outbox/inbox workers, audit and realtime routing.
- `CentralChat.API` — REST controllers, webhook, SignalR hub mapping, rate limiting, CORS, ProblemDetails middleware, health checks, and development seed.
- `frontend` — Next.js App Router/TypeScript agent workspace.

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

Development explicitly disables signature validation and uses `DevelopmentWhatsAppClient`. Production defaults do not silently fall back to fake delivery.

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

GET  /api/tickets?scope=mine|unassigned|all
GET  /api/tickets/mine
GET  /api/tickets/unassigned
POST /api/tickets/{id}/claim
POST /api/tickets/{id}/assign
POST /api/tickets/{id}/reassign
POST /api/tickets/{id}/unassign

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

The implemented verification scenario covers webhook replay deduplication, stable contact/conversation/ticket reuse, assignment ownership, outbound asynchronous provider status, reassignment history, old-agent send denial, and simultaneous ticket claiming.

## Realtime event names

The hub uses targeted `user:{id}`, `conversation:{id}`, and `unassigned` groups. Current events include `message.received`, `message.sent`, `message.failed`, `ticket.created`, `ticket.claimed`, `ticket.removed`, `ticket.assignment.added`, and `ticket.assignment.removed`. Clients reconnect automatically and refresh REST state.
