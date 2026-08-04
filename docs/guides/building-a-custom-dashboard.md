# Building a custom dashboard

BulkSharp ships a Blazor dashboard, but it is optional. The HTTP API lives in its own
package with no UI dependency, so you can build a front end in React, Angular, Vue, a CLI,
or anything else that speaks HTTP.

This guide covers what you need to drive the API from outside .NET.

## Reference the API, not the dashboard

```bash
dotnet add package BulkSharp
dotnet add package BulkSharp.Api
```

`BulkSharp.Api` contains endpoint mappings and the JSON contract. It does not reference
Razor or Blazor, so nothing UI-related is published with your application.

```csharp
builder.Services.AddBulkSharp(b => b
    .UseFileStorage(fs => fs.UseFileSystem())
    .UseMetadataStorage(ms => ms.UseInMemory())
    .UseScheduler(s => s.UseChannels()));

builder.Services.AddBulkSharpEndpoints();

var app = builder.Build();
app.MapBulkSharpEndpoints();
app.Run();
```

To confirm nothing UI-related came along:

```bash
dotnet publish -c Release -o ./out
ls ./out | grep -iE 'razor|blazor|components'   # expect no matches
```

> `AddBulkSharpEndpoints()` registers the HTTP contract. Do not confuse it with
> `AddBulkSharpApi()` in the `BulkSharp` package, which selects API-only mode for the
> processing services — the two are unrelated and are commonly used together.

## Enable CORS

A front end served from another origin cannot call the API without it.

```csharp
builder.Services.AddBulkSharpCors("https://admin.example.com");

app.UseCors(BulkSharpCorsExtensions.PolicyName);
app.MapBulkSharpEndpoints();
```

`Content-Disposition` is exposed by this policy. Without it the browser cannot read the
filename from the file, export and template download endpoints, and your downloads will
be named after the URL.

Wildcard origins are rejected: the policy allows credentials, and the two cannot be
combined.

## Generate a client

Serve the OpenAPI document outside production:

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapBulkSharpOpenApi();   // /openapi/v1.json
}
```

Then generate:

```bash
npx @openapitools/openapi-generator-cli generate \
  -i http://localhost:5000/openapi/v1.json \
  -g typescript-angular \
  -o ./src/api
```

Every endpoint carries a stable `operationId` — `getBulks`, `getBulkStatus`, `createBulk`,
`retryBulk` and so on — so regenerating does not churn your method names.

### Enums are strings

Responses serialize enums as their names, and the document says so:

```json
{ "status": "CompletedWithErrors", "processedRows": 42, "totalRows": 42 }
```

Property names are camelCase and null properties are omitted.

**.NET clients must opt in.** `JsonSerializerDefaults.Web` does not include a string enum
converter, so deserializing with default options fails:

```csharp
var operation = await http.GetFromJsonAsync<BulkOperation>(
    $"/api/bulks/{id}", BulkSharpJsonSerialization.Options);
```

Clients in other languages parse the JSON directly and need no special handling.

## Build the submission form from the API

`GET /api/operations` describes each operation's metadata fields and file columns, so a
form can be rendered without compile-time knowledge of the operation types:

```json
{
  "name": "user-import",
  "description": "Import users from a CSV file.",
  "isStepBased": false,
  "metadataFields": [
    { "name": "ImportedBy", "type": "string", "required": true },
    { "name": "Department", "type": "string", "required": false },
    { "name": "BatchSize",  "type": "int?",   "required": false }
  ],
  "fileColumns": [
    { "name": "Email", "type": "string", "required": true }
  ]
}
```

`type` is a friendly name — `string`, `int`, `decimal`, `bool`, `datetime`, `guid` — with a
trailing `?` for nullable types. Map it to an input type and a validator.

### Always validate before creating

`required` is derived from `[Required]` attributes. An operation can also enforce rules
imperatively in `ValidateMetadataAsync`, and those are invisible to the descriptor.

**`POST /api/bulks/validate` is therefore a mandatory pre-flight for any generated form**,
not an optimization. It takes the same multipart payload as create and returns:

```json
{ "valid": false, "metadataErrors": ["ImportedBy is required"], "fileErrors": [] }
```

Surface those errors rather than submitting an operation that will fail.

## Create an operation

`POST /api/bulks`, multipart form data:

| Field | Required | Notes |
|---|---|---|
| `operationName` | yes | From the discovery endpoint |
| `file` | yes | CSV or JSON |
| `metadata` | yes | JSON object matching `metadataFields` |
| `notifications` | no | JSON notification options |
| `createdBy` | conditional | **Ignored when the request is authenticated** |

Attribution comes from the authenticated principal via `IBulkUserResolver`. The form value
is only honoured for anonymous self-hosting. Register your own resolver to change how the
identity is derived:

```csharp
services.AddSingleton<IBulkUserResolver, MyResolver>();
```

Response:

```json
{ "operationId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301" }
```

## Track progress

Poll `GET /api/bulks/{id}/status` — the built-in dashboard polls every 2 seconds, and that
cadence is a reasonable default:

```json
{
  "status": "Running",
  "processedRows": 120,
  "totalRows": 500,
  "errorCount": 3,
  "completedAt": null,
  "progress": 24
}
```

Stop polling on a terminal status: `Completed`, `CompletedWithErrors`, `Failed`,
`Cancelled`. Leaving an interval running against a finished operation is the most common
bug in a hand-written client.

For detail, `GET /api/bulks/{id}/errors` returns failed rows and
`GET /api/bulks/{id}/rows` returns per-row pipeline progress with per-step state. Both are
paged with the same `items` / `totalCount` / `page` / `pageSize` / `hasNextPage` envelope.

## Authorization

Reads and writes are governed separately, so viewers can be prevented from mutating:

```csharp
app.MapBulkSharpEndpoints(new BulkSharpAuthorizationOptions
{
    ReadPolicy    = "bulk:read",
    OperatePolicy = "bulk:operate"
});
```

BulkSharp only knows the policy names; your host defines what they require. Passing
nothing leaves the endpoints unauthorized — appropriate only when your own middleware
enforces access, or for local development.

## Aggregating several services

If bulk operations run in more than one service, `BulkSharp.Gateway` exposes the **same
routes and response shapes** over all of them, so the client you generated works unchanged.
Two additions, both additive:

- `sourceService` on each operation descriptor, naming the owning backend
- `?source={name}` on `GET /api/bulks`, routing to one backend and skipping the fan-out

See the [gateway guide](gateway.md).

## Keeping the built-in dashboard

The Blazor dashboard is not deprecated. It remains the zero-effort option — one line to
mount a working UI in-process — and it consumes exactly these endpoints, so anything it
does is reproducible from outside.

```csharp
builder.Services.AddBulkSharpDashboard();
app.UseBulkSharpDashboard();          // UI and API
app.UseBulkSharpDashboardUi();        // UI only, when the API comes from elsewhere
```
