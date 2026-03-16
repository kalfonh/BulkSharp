# Dashboard

BulkSharp includes a drop-in Blazor Server dashboard for monitoring and managing bulk operations.

## Setup

```csharp
builder.Services.AddBulkSharp();
builder.Services.AddBulkSharpDashboard();

var app = builder.Build();
app.UseBulkSharpDashboard();
```

## Features

### Operation List
Filterable by operation name, status, creator, and date range. Supports pagination and sorting. Shows progress bars and row counts.

### Operation Detail
Shows operation status, progress, timing (created, started, completed), and metadata. Includes per-row step status drill-down for step-based operations.

### Error Table
Paginated error list with filtering by row number, row ID, and error type. Sortable by any column.

### Create Form
Upload files, enter metadata, and create operations. Includes real-time validation with debounce - errors display as you type.

### File Download
Download the original uploaded file for any operation.

### Sample Data Runners
Built-in sample operations for testing. Generates sample data and runs operations to verify the pipeline.

## REST API

The dashboard exposes a full REST API alongside the Blazor UI.

### Query Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/operations` | List registered operation types with metadata/row field schemas |
| `GET` | `/api/bulks` | Query operations (supports `operationName`, `status`, `createdBy`, `fromDate`, `toDate`, `page`, `pageSize`, `sortBy`, `sortDescending`) |
| `GET` | `/api/bulks/{id}` | Get operation details |
| `GET` | `/api/bulks/{id}/status` | Get status and progress percentage |
| `GET` | `/api/bulks/{id}/errors` | Query errors (supports `rowNumber`, `rowId`, `errorType`, `page`, `pageSize`, `sortBy`, `sortDescending`) |
| `GET` | `/api/bulks/{id}/rows` | Per-row step statuses grouped by row number |
| `GET` | `/api/bulks/{id}/file` | Download original uploaded file |

### Action Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/bulks/validate` | Pre-submission validation |
| `POST` | `/api/bulks` | Create new operation (multipart form: `operationName`, `metadata`, `file`) |
| `POST` | `/api/bulks/{id}/cancel` | Cancel a running operation |
| `POST` | `/api/bulks/{id}/signal/{key}` | Signal step completion |
| `POST` | `/api/bulks/{id}/signal/{key}/fail` | Signal step failure (body: `{ "errorMessage": "..." }`) |

### Retry Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/bulks/{id}/retry/eligibility` | Check if operation can be retried |
| `POST` | `/api/bulks/{id}/retry` | Retry all failed rows |
| `POST` | `/api/bulks/{id}/retry/rows` | Retry specific rows (body: `{ "rowNumbers": [1, 2, 3] }`) |
| `GET` | `/api/bulks/{id}/retry/history` | Query retry history (supports `rowNumber`, `page`, `pageSize`) |

See [Retry Guide](retry.md) for details on retry eligibility and flow.

### Export Endpoint

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/bulks/{id}/export` | Export operation results as CSV or JSON |

Query parameters: `mode` (report/data), `format` (csv/json), `state`, `errorType`, `stepName`.

See [Export Guide](export.md) for details on export modes and filters.
