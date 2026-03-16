# Dashboard Service Filter & Template-Based Play Button

## Problem

1. The Operations list page (`/bulks-list`) shows operations from all backends with no way to filter by service. In multi-backend deployments, users need to focus on a specific service's operations.
2. The gateway dashboard doesn't show the play button for running operations because it depends on custom `/api/samples` and `/api/bulks/sample` endpoints that are host-specific and not proxied by the gateway.

## Goals

1. Add a service filter to the Operations list that uses the `?source=` routing optimization.
2. Make the play button work through standard BulkSharp endpoints (template download + create) so it works in both standalone and gateway modes.
3. Remove the custom sample endpoints from the Production sample — they're no longer needed.

## Non-Goals

- Building a full sample data management system. The template-based approach is a starting point; richer sample data can be added later.
- Adding service filter to the OperationDetails page or other pages.

## Design

### 1. Service Filter on Operations List

**File:** `src/BulkSharp.Dashboard/Pages/Operations.razor`

Add a "Service" dropdown filter in the existing filter bar (alongside operationName, createdBy, status, date range filters).

**Data source:** On page load, fetch `GET /api/operations`. Extract distinct `sourceService` values from the response (the gateway aggregator already tags each operation with `sourceService`). In standalone mode, operations don't have `sourceService` — the filter shows only "All Services" and is effectively a no-op.

**Behavior:**
- Default: "All Services" (no `source` param — fan-out as before)
- Selected: appends `&source=svc-name` to the list query string
- Debounced with the existing 300ms filter debounce

**Display:** Add a `Source` column to the operations table, after the Operation column. Shows the `Source` property from `BulkOperation`. In standalone mode this column shows the service name (auto-defaulted from assembly name).

**Invariant:** The filter dropdown options come from `sourceService` on the discovery response (`GET /api/operations`); the table column comes from `BulkOperation.Source`. Both are the backend's configured `BulkSharpOptions.ServiceName`, so they match. In standalone mode, `GET /api/operations` returns no `sourceService` tag (the standalone discovery doesn't add it), so the filter correctly shows only "All Services".

### 2. Template-Based Play Button

**File:** `src/BulkSharp.Dashboard/Pages/Index.razor`

Change the play button's `RunSample` method to use the template-based approach instead of the custom `/api/bulks/sample` endpoint.

**Flow:**
1. `GET /api/operations/{name}/template` — download the template CSV for the operation
2. `POST /api/bulks` — upload the template as a new operation. **Must use `multipart/form-data`** (not JSON), with these form fields:
   - `operationName`: the operation name
   - `file`: the downloaded template bytes (as `template.csv`)
   - `createdBy`: `"sample"`
   - `metadata`: `{}` (empty, uses defaults)
   The endpoint rejects non-form requests and blank `createdBy` with 400.
3. On success, show a toast notification with a link to the operation details page
4. On failure, show an error toast

**Removed:** The `/api/samples` fetch on page load and the samples display logic. The play button is now shown for all discovered operations, not just those with sample data.

### 3. Production Sample Cleanup

**File:** `samples/BulkSharp.Sample.Production/Program.cs`

Remove the `configureAdditionalEndpoints` callback that registers `/api/samples` and `/api/bulks/sample`. The standard BulkSharp endpoints are sufficient.

**File:** `samples/BulkSharp.Sample.Production/SampleDataProvider.cs`

Remove this file — it provided sample data metadata for the custom endpoints.

### 4. Dashboard Sample Alignment

**File:** `samples/BulkSharp.Sample.Dashboard/Program.cs`

The standalone Dashboard sample also uses custom sample endpoints. Update it to match — remove the custom endpoints and rely on the template-based play button. Remove `SampleDataProvider.cs` from this sample as well.

## Testing

**Dashboard component tests** (`tests/BulkSharp.Dashboard.Tests/`):
- Service filter dropdown renders with discovered services
- Play button triggers template download then create

**Manual verification:**
- Aspire AppHost: gateway dashboard shows operations with play buttons, clicking play creates an operation through the gateway

## Affected Files

| File | Change |
|------|--------|
| `src/BulkSharp.Dashboard/Pages/Operations.razor` | Add service filter dropdown and Source column |
| `src/BulkSharp.Dashboard/Pages/Index.razor` | Template-based play button, remove sample endpoints dependency |
| `samples/BulkSharp.Sample.Production/Program.cs` | Remove custom sample endpoints callback |
| `samples/BulkSharp.Sample.Production/SampleDataProvider.cs` | Delete file |
| `samples/BulkSharp.Sample.Dashboard/Program.cs` | Remove custom sample endpoints callback |
| `samples/BulkSharp.Sample.Dashboard/SampleDataProvider.cs` | Delete file |

## Tradeoffs

- **Template as sample data:** The template CSV contains column headers and possibly one example row, not realistic sample data. This is a starting point — richer sample data support can be layered on later (e.g., operations could expose a `GetSampleData()` method).
- **JSON operations get CSV templates:** Operations using JSON format (e.g., `inventory-update`) will receive a CSV template that their parser will reject. The error toast will surface this. This is a known limitation — filtering the play button to CSV-format operations only, or generating format-appropriate templates, is deferred.
- **Empty metadata on play:** Submitting `{}` works for operations with optional metadata or sensible defaults. Operations with required metadata validation will fail — the error toast handles this gracefully, and the user can use the full CreateBulk page instead.
