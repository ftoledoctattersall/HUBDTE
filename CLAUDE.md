# CLAUDE.md — HUBDTE

AI assistant context for the HUBDTE solution. Read this before making any changes.

---

## What this project does

HUBDTE is a .NET 8 solution that reliably processes Chilean electronic tax documents (DTE — Documentos Tributarios Electrónicos) received from SAP. It decouples HTTP ingestion from async processing via the Outbox pattern and RabbitMQ, ultimately generating fixed-width TXT files and sending them to the Azurian SOAP service.

**Core flow:** `POST /documents` → persist SapDocument + OutboxMessage (atomic) → publish to RabbitMQ → consume per DTE type → generate TXT → send to Azurian SOAP → mark Processed.

---

## Solution structure

```
HUBDTE.sln
├── HUBDTE.Domain          # Entities, enums, domain behavior (no dependencies)
├── HUBDTE.Application     # Use cases, interfaces, DTOs (depends on Domain only)
├── HUBDTE.Infrastructure  # EF Core, RabbitMQ, Azurian SOAP, file writing (implements Application interfaces)
├── HUBDTE.Api             # ASP.NET Core Web API — ingestion endpoint only
└── HUBDTE.WorkerHost      # .NET Generic Host — background processing workers
```

**Dependency rule:** Domain ← Application ← Infrastructure ← Api/WorkerHost. Never reference Infrastructure from Domain or Application directly.

Both `HUBDTE.Api` and `HUBDTE.WorkerHost` must run simultaneously for the system to function end-to-end.

---

## Key packages

- `Microsoft.EntityFrameworkCore` 8.0.24 + SQL Server provider
- `RabbitMQ.Client` 6.8.1
- `System.ServiceModel.*` 8.x (WCF SOAP client for Azurian)
- Target framework: **net8.0** across all projects
- Nullable reference types enabled everywhere

---

## Domain model

### `SapDocument` (`HUBDTE.Domain/Entities/SapDocument.cs`)

Represents a DTE document received from SAP.

| Property | Purpose |
|---|---|
| `FilialCode` | Company/branch identifier (maps to Azurian empresa) |
| `DocEntry` | SAP document ID |
| `TipoDte` | DTE type (33, 34, 39, 52, 56, 61, 110, 111, 112) |
| `QueueName` | Target RabbitMQ queue |
| `PayloadJson` | Full raw JSON payload from SAP |
| `Status` | `Pending → Processing → Processed` (terminal) or `Failed` |
| `AttemptCount` | Incremented on each `MarkFailed` call |
| `ErrorReason` | Appended log of failures; capped at 3500 chars |

**State machine** (transitions enforced in domain methods):
```
Pending ──► Processing ──► Processed  (terminal, can never go back)
   ▲              │
   │              ▼
   └──────── Failed ◄──── (can return to Pending for reprocessing)
```

Use `MarkPending()`, `MarkProcessing()`, `MarkProcessed()`, `MarkFailed()` — never set `Status` directly from outside the entity except in `DocumentProcessor` (which does `sapDoc.Status = SapDocumentStatus.Processing` as an optimistic claim step before saving).

### `OutboxMessage` (`HUBDTE.Domain/Entities/OutboxMessage.cs`)

Tracks publication of each document to RabbitMQ.

**States:** `Pending → Processing → Published` (terminal) or `Failed` (terminal).

Key behavior:
- `Claim(lockId, utcNow)` — acquires an optimistic lock before processing
- `MarkProcessing()` — transitions and releases lock
- `MarkPublished()` — terminal success
- `MarkRetryableFailure()` — returns to `Pending` (clears lock, increments `PublishAttempts`)
- `MarkFailed()` — terminal failure
- `IsStuckProcessing()` / `RescueStuckProcessing()` — used by the publisher to rescue stuck messages

**Unique constraint:** `(FilialCode, DocEntry, TipoDte)` on `SapDocuments` table — enforced at DB level.

---

## Supported DTE types

| TipoDte | Queue key | Queue name |
|---------|-----------|------------|
| 33 | `Dte33` | `documents.dte33.queue` |
| 34 | `Dte34` | `documents.dte34.queue` |
| 39 | `Dte39` | `documents.dte39.queue` |
| 52 | `Dte52` | `documents.dte52.queue` |
| 56 | `Dte56` | `documents.dte56.queue` |
| 61 | `Dte61` | `documents.dte61.queue` |
| 110 | `Dte110` | `documents.dte110.queue` |
| 111 | `Dte111` | `documents.dte111.queue` |
| 112 | `Dte112` | `documents.dte112.queue` |

Adding a new type requires changes in **5 places** — see "Extending" section below.

---

## Data flow (end-to-end)

### 1. Ingestion (`HUBDTE.Api`)

`POST /documents` → `DocumentsController.Ingest()` → `DocumentIngestionService.IngestAsync()`

The service:
1. Validates JSON structure: requires `source.company.{filialCode|empresa|filial}`, `document.docEntry`, `document.tipoDte`, `detalle[]`
2. Checks if document already exists (by key)
3. If new: creates `SapDocument` + `OutboxMessage` in a single transaction
4. If `Failed` with no pending outbox: creates new outbox and resets to `Pending`
5. Returns 202 (accepted/in-progress) or 200 (already processed/idempotent collision)

**Token auth:** Middleware checks `X-Client-Token` header against `Security:ClientToken` config. If config is empty, token is not required.

### 2. Outbox publishing (`OutboxPublisherHostedService`)

Background service that:
1. Rescues stuck outbox messages (Processing but `ProcessingStartedAt` too old)
2. Claims a batch of `Pending` outbox messages using `LockId`
3. Resolves routing key via `MessageRoutingResolver` (MessageType → queue name)
4. Publishes to `documents.exchange` with `PublishConfirmed()` (publisher confirms)
5. Marks outbox `Published` or returns to `Pending` on failure

### 3. Consumer (`RabbitConsumerWorker`)

One `BackgroundService` per queue (9 total). Each:
1. Consumes with `prefetchCount=1` (`BasicQos`)
2. Parses `{filialCode, docEntry, tipoDte}` from message body
3. Reads `x-attempt` header; increments to get current `attempt`
4. Calls `IDocumentProcessor.ProcessAsync()`
5. On failure: publishes to retry queue or DLQ based on retry policy
6. Emits metrics every 30 seconds

### 4. Document processing (`DocumentProcessor`)

1. Loads `SapDocument` from DB by key
2. Checks idempotence (already `Processed` → skip)
3. Calls `TryClaimForProcessingAsync()` — atomic DB claim
4. Builds TXT via `IAzurianTxtBuilder`
5. Optionally writes TXT to disk (`AzurianDev:ForceWriteTxt`)
6. Sends TXT to Azurian SOAP via `IAzurianClient`
7. Calls `MarkProcessed()` or `MarkFailed()` + saves

### 5. DLQ reprocessor (`RabbitDlqReprocessorWorker`)

Consumes from `documents.dteXX.dlq`, resets document to `Pending`, clears attempt counter, republishes to main queue.

---

## RabbitMQ topology

**Exchanges:**
- `documents.exchange` — main direct exchange
- `documents.dlq.exchange` — DLQ exchange

**Queue naming convention:**
- Main: `documents.dteXX.queue`
- DLQ: `documents.dteXX.dlq`
- Retry: `documents.dteXX.retry.01`, `.02`, `.03`, etc.

Retry queues use `x-message-ttl` + `x-dead-letter-exchange` → main queue. The topology is initialized at startup by `RabbitTopologyInitializerHostedService`.

**Retry policy** (configured in `RetryPolicy` section):
```json
{ "MaxAttempts": 5, "DelaysSeconds": [10, 30, 120, 600], "JitterSeconds": 3 }
```
Attempt 5 → DLQ. `IRetryPolicyService` handles queue selection.

**Key headers:**
- `x-attempt` — current attempt number (byte in headers, use `RabbitHeaders.GetAttempt()`)
- `x-message-type` — message type string
- `CorrelationId` — for tracing

---

## Azurian TXT generation

### Layout resolution (cascading merge)

For `tipoDte=33`, `empresa=TTMQ`, the system merges in order:
1. `base.json` — shared fields/maps across all types
2. `tipo.33.json` — overrides for type 33
3. `tipo.33.emp.TTMQ.json` — company-specific overrides

Layouts live in `HUBDTE.WorkerHost/AzurianLayouts/`. The repository caches merged results in a `ConcurrentDictionary` (keyed by `tipoDte:empresa`).

### Layout JSON structure

Each file has two top-level sections:
- `layout` — defines `headerLineName`, `detailLineName`, `headerFields[]`, `detailFields[]`, `headerMap[]`, `detailMap[]`, `glosas[]`
- `constants` — key/value string constants referenced by `ValueConstantKey` in maps

**Field rendering order in TXT:**
1. Header line (H prefix)
2. Recargo line (R prefix) — only if `totales.recargo > 0`
3. Referencia lines (F prefix) — from `referencias[]` array
4. Detail lines (D prefix) — one per item in `detalle[]`
5. Glosa lines (G prefix) — from `glosas` in layout, ordered by `seq`

### Key classes

| Class | Location | Role |
|-------|----------|------|
| `AzurianTxtBuilder` | Infrastructure/Azurian | Dispatches to correct `IAzurianTipoDteTxtBuilder` by `tipoDte` |
| `AzurianTipoDteFixedWidthBuilder` | Infrastructure/Azurian/Builders | Concrete builder, delegates to base |
| `BaseAzurianFixedWidthBuilder` | Infrastructure/Azurian/Builders | All TXT generation logic |
| `AzurianLayoutRepository` | Infrastructure/Azurian/Layouts | Loads + merges JSON layout files |
| `FixedWidthRenderer` | Infrastructure/Azurian | Renders a single fixed-width line |
| `FieldMapValueProvider` | Infrastructure/Azurian | Resolves field values from JSON payload |
| `CompositeValueProvider` | Infrastructure/Azurian | Combines overrides dict + base provider |
| `JsonPathValueResolver` | Infrastructure/Azurian | Case-insensitive JSON path navigation |

---

## Persistence

**DbContext:** `AppDbContext` with two `DbSet`s: `SapDocuments`, `OutboxMessages`.

`SaveChangesAsync` automatically sets `CreatedAt`/`UpdatedAt` via change tracker interceptors.

**EF Configurations** (Fluent API, in `Infrastructure/Persistence/Configurations/`):
- `SapDocumentConfiguration` — unique index on `(FilialCode, DocEntry, TipoDte)`, status index
- `OutboxMessageConfiguration` — locking columns, indexes on status + lock

**Migrations:** Located in `HUBDTE.Infrastructure/Migrations/`. Startup project for EF commands is `HUBDTE.Api`.

```bash
# Apply migrations
dotnet ef database update --project HUBDTE.Infrastructure --startup-project HUBDTE.Api

# Add a migration
dotnet ef migrations add <Name> --project HUBDTE.Infrastructure --startup-project HUBDTE.Api
```

---

## Dependency injection

### `AddInfrastructure(IConfiguration)` — called from both Api and WorkerHost

Registers: `AppDbContext`, `SapDocumentRepository`, `OutboxMessageRepository`, `EfUnitOfWork`, `AzurianFileWriterAdapter`, `RabbitConnectionFactoryProvider`, `RabbitChannelFactory`, `RabbitMqPublisher`, `RetryPolicyService`, `RabbitTopologyService`, `MessageRoutingResolver`.

### `AddApplicationIngestion()` — Api only

Registers ingestion use case services.

### `AddApplicationProcessing()` — WorkerHost only

Registers `DocumentProcessor` and processing-related services.

### WorkerHost DI patterns

- One `RabbitConsumerWorker` per queue — registered as `IHostedService` via factory lambda (not generic `AddHostedService<T>()`)
- One `RabbitDlqReprocessorWorker` per queue — same pattern
- One `AzurianTipoDteFixedWidthBuilder` per `tipoDte` — registered as `IAzurianTipoDteTxtBuilder` singleton; `AzurianTxtBuilder` receives them all via `IEnumerable<IAzurianTipoDteTxtBuilder>`

---

## Configuration reference

All configuration lives in `appsettings.json` (and environment-specific overrides).

```jsonc
{
  "ConnectionStrings": {
    "SqlServer": "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;"
  },
  "Security": {
    "ClientToken": ""           // Empty = no token required
  },
  "RabbitMq": {
    "HostName": "localhost",
    "Port": 5672,
    "VirtualHost": "/",
    "UserName": "guest",
    "Password": "guest",
    "Exchange": "documents.exchange"
  },
  "RetryPolicy": {
    "MaxAttempts": 5,
    "DelaysSeconds": [10, 30, 120, 600],
    "JitterSeconds": 3
  },
  "Queues": {
    "Dte39": "documents.dte39.queue",
    "Dte33": "documents.dte33.queue",
    "Dte34": "documents.dte34.queue",
    "Dte52": "documents.dte52.queue",
    "Dte56": "documents.dte56.queue",
    "Dte61": "documents.dte61.queue",
    "Dte110": "documents.dte110.queue",
    "Dte111": "documents.dte111.queue",
    "Dte112": "documents.dte112.queue"
  },
  "AzurianSoap": {
    "ApiKey": "",
    "RutEmpresa": 0,
    "ResolucionSii": 0,
    "Soap12Endpoint": ""
  },
  "AzurianDev": {
    "ForceWriteTxt": false,        // true = write TXT to disk, skip Azurian error on failure
    "OutputPath": "C:\\AzurianDev\\"
  },
  "AzurianLayoutFiles": {
    "LayoutsPath": "AzurianLayouts",
    "Constants": {
      "IndicadorE": "E",
      "CincoCeros": "00000"
      // ...
    }
  },
  "FailureSimulation": {
    "Enabled": false,
    "FailTipoDte": 33,
    "FailAlways": true
  }
}
```

Options classes with validation-on-start: `RabbitMqOptions`, `RetryPolicyOptions`, `QueuesOptions`.

---

## Build and run

### Prerequisites

- .NET 8 SDK
- SQL Server accessible via connection string
- RabbitMQ on `localhost:5672` (or configured host)
- Azurian SOAP endpoint (or set `AzurianDev:ForceWriteTxt=true` to skip it in dev)
- `HUBDTE.WorkerHost/AzurianLayouts/` folder with layout JSON files

### Commands

```bash
# Restore
dotnet restore

# Build
dotnet build

# Run both projects simultaneously (use two terminals or Visual Studio multi-startup)
dotnet run --project HUBDTE.Api
dotnet run --project HUBDTE.WorkerHost

# Swagger UI (development)
# https://localhost:7139/swagger
# http://localhost:5179/swagger
```

---

## How to add a new TipoDte

When adding support for a new DTE type (e.g., type `99`), update **all five** of these places:

1. **`DocumentIngestionService`** (`Application/DocumentIngestion/DocumentIngestionService.cs`) — add `99 => "Dte99"` to `MessageTypeFromTipoDte()` switch.

2. **`QueuesOptions`** (`Infrastructure/Messaging/Options/QueuesOptions.cs`) — add `public string Dte99 { get; set; } = default!;`

3. **`MessageRoutingResolver`** (`Infrastructure/Messaging/MessageRoutingResolver.cs`) — add `["Dte99"] = q.Dte99` to the routes dictionary.

4. **`WorkerHost/Program.cs`** — add `"Dte99"` to `queueKeys` array and `99` to `tiposDteSoportados` array.

5. **`appsettings.json`** (both Api and WorkerHost) — add `"Dte99": "documents.dte99.queue"` under `Queues`.

6. **Layout files** — create `AzurianLayouts/tipo.99.json` (and optionally `tipo.99.emp.{empresa}.json`).

7. **Validate** `QueuesOptions` in `WorkerHost/Program.cs` — add `!string.IsNullOrWhiteSpace(o.Dte99)` to the validation predicate.

---

## How to add a company-specific layout override

Create a file in `HUBDTE.WorkerHost/AzurianLayouts/` named:
```
tipo.{tipoDte}.emp.{empresaCode}.json
```

Only include the sections you want to override. The merge is additive: override fields/maps replace base ones by name; new ones are appended.

---

## Key conventions

- **No direct status assignment** on domain entities from outside the entity — use the domain methods (`MarkPending`, `MarkProcessing`, `MarkProcessed`, `MarkFailed`). The one exception is `DocumentProcessor` setting `sapDoc.Status = SapDocumentStatus.Processing` immediately after `TryClaimForProcessingAsync` succeeds (the DB already reflects Processing via the claim).

- **Transactions:** Always use `IUnitOfWork.BeginTransactionAsync()` / `CommitAsync()` / `RollbackAsync()` when writing both `SapDocument` and `OutboxMessage` together. Single-entity saves use `SaveChangesAsync()` without explicit transaction.

- **Idempotence:** The `(FilialCode, DocEntry, TipoDte)` unique key + status checks prevent duplicate processing. `DocumentProcessor` double-checks via `TryClaimForProcessingAsync` even if the consumer already checked status.

- **Error handling in consumers:** On processing failure, always publish to retry/DLQ before acking. On publish failure, nack with requeue=true to avoid message loss.

- **Charset for Azurian:** TXT content is normalized to ISO-8859-1 encoding before sending (`AzurianSoapClient.NormalizeTxt`).

- **Layout caching:** Layouts are merged and cached in memory at first access. Restart required to pick up layout file changes.

- **No tests project** exists in this solution — when making changes, validate manually using `FailureSimulation` config and dev mode (`ForceWriteTxt=true`).

- **Swagger** is enabled for `Development` and `QA` environments only.

- **No comments** should be added unless the reason is genuinely non-obvious. Existing emoji-style comments in hosted services are acceptable — do not add more.

---

## Tracing and observability

| Field | Where stored |
|-------|-------------|
| `CorrelationId` | RabbitMQ message properties + `OutboxMessage.CorrelationId` |
| `x-attempt` | RabbitMQ header (byte) |
| `x-message-type` | RabbitMQ header |
| `ErrorReason` | `SapDocument.ErrorReason` — appended on each failure |
| `OutboxMessage.Error` | Last publish error |

The consumer logs a structured scope with `filialCode`, `docEntry`, `tipoDte`, `queue`, `attempt`, `correlationId`, `messageType` for every message processed.
