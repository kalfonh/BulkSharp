# BulkSharp.Api

The BulkSharp HTTP API, with no UI dependency.

Reference this package when you want to expose bulk operations over HTTP and build your
own front end — React, Angular, Vue, a CLI, or another service. It contains only endpoint
mappings and the JSON contract, so nothing Razor or Blazor is pulled into your application.

If you want a ready-made UI instead, reference `BulkSharp.Dashboard`, which builds on this
package and adds a Blazor Server dashboard.

## Install

```bash
dotnet add package BulkSharp
dotnet add package BulkSharp.Api
```

> `AddBulkSharpEndpoints()` (this package) registers the HTTP response contract.
> `AddBulkSharpApi()` (the `BulkSharp` package) is unrelated — it selects API-only mode
> for the processing services, leaving operations Pending for a separate worker.

## Usage

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

## Browser clients

A front end served from a different origin needs CORS. `Content-Disposition` must be
exposed or the browser cannot read filenames from the file, export and template
download endpoints.

```csharp
builder.Services.AddBulkSharpCors("https://admin.example.com");

app.UseCors(BulkSharpCorsExtensions.PolicyName);
app.MapBulkSharpEndpoints();
```

## Authorization

The host owns authentication and the policy definitions; BulkSharp only knows the policy
names. Reads and writes are governed separately, so viewers can be prevented from
submitting, cancelling or retrying:

```csharp
app.MapBulkSharpEndpoints(new BulkSharpAuthorizationOptions
{
    ReadPolicy    = "bulk:read",     // list, detail, status, errors, rows, export, download
    OperatePolicy = "bulk:operate"   // create, validate, cancel, retry, signal
});
```

`OperatePolicy` falls back to `ReadPolicy` when omitted. A single policy for everything:

```csharp
app.MapBulkSharpEndpoints(authorizationPolicy: "bulk:access");
```

Passing nothing leaves the endpoints unauthorized — appropriate only for a host that
enforces authorization in its own middleware, or for local self-hosting.

Attribution of an operation's creator comes from the authenticated principal via
`IBulkUserResolver`. Register your own implementation to change how the identity is
derived.

## JSON contract

Responses use camelCase property names and string-valued enums. `AddBulkSharpApi()`
applies this automatically. .NET clients must deserialize with the same options:

```csharp
var operation = await http.GetFromJsonAsync<BulkOperation>(
    $"/api/bulks/{id}", BulkSharpJsonSerialization.Options);
```

Clients in other languages parse the JSON directly and need no special handling.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/operations` | Registered operations, with metadata fields and file columns |
| GET | `/api/operations/{name}/template` | CSV template for an operation |
| GET | `/api/bulks` | Query operations, paged and filtered |
| GET | `/api/bulks/{id}` | A single operation |
| GET | `/api/bulks/{id}/status` | Progress snapshot, suitable for polling |
| GET | `/api/bulks/{id}/errors` | Failed rows, paged |
| GET | `/api/bulks/{id}/rows` | Per-row pipeline progress, paged |
| GET | `/api/bulks/{id}/file` | Download the source file |
| GET | `/api/bulks/{id}/export` | Export a report, errors, or rows |
| GET | `/api/bulks/{id}/retry/eligibility` | Whether the operation can be retried |
| GET | `/api/bulks/{id}/retry/history` | Retry attempts, paged |
| POST | `/api/bulks` | Create an operation from an uploaded file |
| POST | `/api/bulks/validate` | Validate a submission without creating it |
| POST | `/api/bulks/{id}/cancel` | Cancel a running operation |
| POST | `/api/bulks/{id}/retry` | Retry all failed rows |
| POST | `/api/bulks/{id}/retry/rows` | Retry specific rows |
| POST | `/api/bulks/{id}/signal/{key}` | Complete a waiting pipeline step |
| POST | `/api/bulks/{id}/signal/{key}/fail` | Fail a waiting pipeline step |

## Documentation

See the [building a custom dashboard](https://github.com/kalfonh/bulksharp) guide for a
walkthrough of driving these endpoints from a non-.NET front end.
