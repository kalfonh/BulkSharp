# BulkSharp.Dashboard

Blazor Server dashboard for monitoring and managing BulkSharp bulk data operations.

## Features

- Operation list with filtering, sorting, and pagination
- Real-time progress tracking with status badges
- Error drill-down with per-row step detail
- File upload and operation creation
- CSV template download for registered operations
- REST API for programmatic access
- Signal endpoint for async step completion

## Usage

```csharp
services.AddBulkSharp(builder => { /* ... */ });
services.AddBulkSharpDashboard();

app.UseBulkSharpDashboard();
```

The dashboard is a Razor Class Library (RCL) that mounts at the application root. Configure authentication and authorization in your host application.

## Links

- [Documentation](https://github.com/kalfonh/BulkSharp)
- [Dashboard Guide](https://github.com/kalfonh/BulkSharp/blob/main/docs/guides/dashboard.md)
