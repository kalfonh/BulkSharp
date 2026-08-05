# BulkSharp.Dashboard

Blazor Server dashboard for monitoring and managing BulkSharp bulk data operations.

## Features

- Operation list with filtering, sorting, and pagination
- Real-time progress tracking with status badges
- Error drill-down with per-row step detail
- File upload and operation creation
- CSV template download for registered operations
- Signal endpoint for async step completion

## Usage

```csharp
services.AddBulkSharp(builder => { /* ... */ });
services.AddBulkSharpDashboard();

app.UseBulkSharpDashboard();   // UI plus the BulkSharp API endpoints
```

The dashboard is a Razor Class Library (RCL) that mounts at the application root. Configure
authentication and authorization in your host application.

## Relationship to BulkSharp.Api

The HTTP API lives in `BulkSharp.Api`; this package depends on it and adds the UI.
`UseBulkSharpDashboard()` maps both, so existing hosts need no change.

**If you are building your own front end, reference `BulkSharp.Api` instead.** Nothing
Razor or Blazor is then published with your application. See
[Building a custom dashboard](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/building-a-custom-dashboard.md).

When the API is served from somewhere else — for example an aggregated gateway across
several backends — mount the UI alone:

```csharp
app.UseBulkSharpGateway();      // aggregated API
app.UseBulkSharpDashboardUi();  // UI only
```

The dashboard's own pages consume these endpoints over HTTP, so anything the built-in UI
does is reproducible from an external client.

## Links

- [Documentation](https://github.com/kalfonh/BulkSharp)
- [Dashboard Guide](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/dashboard.md)
