namespace BulkSharp.Sample.Dashboard;

public record SampleRunRequest(string OperationName);

public record SampleInfo(string OperationName, string FileName, string FileContent, object Metadata, string Description, int RowCount);

public static class SampleDataProvider
{
    private static readonly Dictionary<string, SampleInfo> Samples = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---------------------------------------------------------------
        // 1. Simple CSV operation - no [CsvColumn] attributes, auto-mapped
        // ---------------------------------------------------------------
        ["user-import"] = new SampleInfo(
            OperationName: "user-import",
            FileName: "sample-users.csv",
            FileContent: """
                Name,Email,Department,Role
                Alice Johnson,alice@example.com,Engineering,Developer
                Bob Smith,bob@example.com,Marketing,Manager
                Carol Williams,carol@example.com,Engineering,Lead
                Diana Lee,diana@example.com,Sales,Representative
                Edward Brown,edward@example.com,Finance,Analyst
                Frank Garcia,frank@example.com,Engineering,Developer
                Grace Chen,grace@example.com,HR,Coordinator
                Henry Park,henry@example.com,Engineering,Architect
                """,
            Metadata: new { ImportedBy = "Dashboard Sample", Department = "All", SendWelcomeEmail = false },
            Description: "Simple CSV import with auto-mapped properties (no [CsvColumn] attributes). 8 users across 5 departments.",
            RowCount: 8
        ),

        // ---------------------------------------------------------------
        // 2. CSV with auto-mapped properties and decimal/int types
        // ---------------------------------------------------------------
        ["product-import"] = new SampleInfo(
            OperationName: "product-import",
            FileName: "sample-products.csv",
            FileContent: """
                Name,Description,Price,Category,StockQuantity
                Wireless Mouse,Ergonomic wireless mouse with USB receiver,29.99,Electronics,150
                USB-C Hub,7-port USB-C hub with HDMI output,49.99,Electronics,75
                Standing Desk Mat,Anti-fatigue mat for standing desks,39.99,Office,200
                Mechanical Keyboard,Cherry MX Blue switches with RGB,89.99,Electronics,45
                Monitor Light Bar,LED light bar for screen top mounting,34.99,Lighting,120
                Cable Organizer,Desktop cable management box,14.99,Office,300
                Webcam HD,1080p webcam with built-in microphone,59.99,Electronics,90
                Desk Lamp,Adjustable LED desk lamp with dimmer,44.99,Lighting,110
                Laptop Stand,Aluminum laptop riser adjustable height,32.99,Office,160
                Noise Canceling Headphones,Over-ear Bluetooth headphones,129.99,Electronics,60
                """,
            Metadata: new { Store = "Main Warehouse", Category = "Office & Tech", AutoGenerateSku = true },
            Description: "CSV with decimal and int columns (Price, StockQuantity). 10 products from $14.99 to $129.99.",
            RowCount: 10
        ),

        // ---------------------------------------------------------------
        // 3. Step-based operation - CSV, no attributes, complex validation
        //    4 steps with different retry policies (3, 2, 2, 1)
        // ---------------------------------------------------------------
        ["employee-onboarding"] = new SampleInfo(
            OperationName: "employee-onboarding",
            FileName: "sample-employees.csv",
            FileContent: """
                FirstName,LastName,Email,Phone,Department,JobTitle,ManagerEmail,StartDate,Office,EquipmentTier
                Alice,Johnson,alice.johnson@company.com,+12025551234,Engineering,Senior Software Engineer,bob.smith@company.com,2026-04-01,New York,Enhanced
                Bob,Smith,bob.smith@company.com,+12025555678,Engineering,VP of Engineering,,2026-04-01,New York,Executive
                Carol,Williams,carol.williams@company.com,,Marketing,Marketing Coordinator,diana.lee@company.com,2026-04-15,San Francisco,Standard
                Diana,Lee,diana.lee@company.com,+14155559012,Marketing,Director of Marketing,,2026-04-01,San Francisco,Executive
                Edward,Brown,edward.brown@company.com,+12025553456,Finance,Financial Analyst,frank.garcia@company.com,2026-05-01,New York,Standard
                Frank,Garcia,frank.garcia@company.com,+12025557890,Finance,CFO,,2026-04-15,New York,Executive
                Grace,Chen,grace.chen@company.com,+14155551111,Engineering,DevOps Engineer,bob.smith@company.com,2026-04-15,San Francisco,Enhanced
                Henry,Park,henry.park@company.com,+12025552222,Sales,Account Executive,irene.taylor@company.com,2026-04-01,New York,Standard
                Irene,Taylor,irene.taylor@company.com,+14155553333,Sales,VP of Sales,,2026-04-01,San Francisco,Executive
                James,Wilson,james.wilson@company.com,,Support,Support Specialist,irene.taylor@company.com,2026-05-15,New York,Standard
                """,
            Metadata: new { HrOperator = "Dashboard Sample", Tenant = "us-east", SendImmediateWelcome = false },
            Description: "STEP-BASED: 4 async steps (AD Account [3 retries], Badge [2], Equipment [2], Email [1]). " +
                         "10 employees with cross-field validation (manager required for non-executives). No CSV attributes.",
            RowCount: 10
        ),

        // ---------------------------------------------------------------
        // 4. Step-based operation - CSV with [CsvColumn] and [CsvSchema]
        //    4 steps with retry policies (0, 3, 2, 1)
        // ---------------------------------------------------------------
        ["order-fulfillment"] = new SampleInfo(
            OperationName: "order-fulfillment",
            FileName: "sample-orders.csv",
            FileContent: """
                OrderId,CustomerEmail,ProductSku,Quantity,UnitPrice,ShippingMethod,ShippingAddress,AddressVerified
                ORD-1001,alice@example.com,SKU-MOUSE-001,2,29.99,standard,123 Main St New York NY,true
                ORD-1002,bob@example.com,SKU-HUB-002,1,49.99,express,456 Oak Ave San Francisco CA,true
                ORD-1003,carol@example.com,SKU-KEYS-003,3,89.99,overnight,789 Pine Rd Chicago IL,true
                ORD-1004,diana@example.com,SKU-LAMP-004,1,44.99,standard,321 Elm St Boston MA,false
                ORD-1005,edward@example.com,SKU-STAND-005,5,32.99,express,654 Maple Dr Seattle WA,true
                ORD-1006,frank@example.com,SKU-CAM-006,2,59.99,standard,987 Cedar Ln Austin TX,true
                ORD-1007,grace@example.com,SKU-HEAD-007,1,129.99,overnight,111 Birch Blvd Denver CO,true
                ORD-1008,henry@example.com,SKU-MAT-008,10,39.99,standard,222 Spruce Way Miami FL,false
                """,
            Metadata: new { WarehouseId = "WH-EAST", ProcessedBy = "Dashboard Sample", PrioritizeExpress = true },
            Description: "STEP-BASED: 4 steps with different retries - Inventory Check (0), Payment Capture (3), " +
                         "Shipment Creation (2), Customer Notification (1). Uses [CsvSchema] and [CsvColumn] attributes. " +
                         "8 orders with cross-field validation (express/overnight require verified address).",
            RowCount: 8
        ),

        // ---------------------------------------------------------------
        // 5. JSON file format - no CSV attributes, System.Text.Json mapping
        // ---------------------------------------------------------------
        ["inventory-update"] = new SampleInfo(
            OperationName: "inventory-update",
            FileName: "sample-inventory.json",
            FileContent: """
                [
                  { "Sku": "SKU-MOUSE-001", "Warehouse": "WH-EAST", "QuantityAdjustment": 200, "ReasonCode": "restock", "Notes": "Q2 restock from supplier" },
                  { "Sku": "SKU-HUB-002", "Warehouse": "WH-WEST", "QuantityAdjustment": -5, "ReasonCode": "damaged", "Notes": "Damaged in transit" },
                  { "Sku": "SKU-KEYS-003", "Warehouse": "WH-EAST", "QuantityAdjustment": 15, "ReasonCode": "return", "Notes": "Customer returns batch" },
                  { "Sku": "SKU-LAMP-004", "Warehouse": "WH-CENTRAL", "QuantityAdjustment": 500, "ReasonCode": "restock", "Notes": "New supplier shipment" },
                  { "Sku": "SKU-STAND-005", "Warehouse": "WH-WEST", "QuantityAdjustment": -30, "ReasonCode": "transfer", "Notes": "Transfer to WH-EAST" },
                  { "Sku": "SKU-STAND-005", "Warehouse": "WH-EAST", "QuantityAdjustment": 30, "ReasonCode": "transfer", "Notes": "Received from WH-WEST" },
                  { "Sku": "SKU-CAM-006", "Warehouse": "WH-CENTRAL", "QuantityAdjustment": -2, "ReasonCode": "audit-correction", "Notes": "Physical count mismatch" },
                  { "Sku": "SKU-HEAD-007", "Warehouse": "WH-EAST", "QuantityAdjustment": 75, "ReasonCode": "restock", "Notes": "Holiday season prep" },
                  { "Sku": "SKU-MAT-008", "Warehouse": "WH-WEST", "QuantityAdjustment": 100, "ReasonCode": "restock", "Notes": "Backorder fulfillment" }
                ]
                """,
            Metadata: new { ApprovedBy = "Dashboard Sample", AdjustmentBatchId = "ADJ-2026-Q2-001", DryRun = false },
            Description: "JSON file format (not CSV). Properties mapped via System.Text.Json - no attributes needed. " +
                         "9 inventory adjustments across 3 warehouses with restocks, damages, returns, transfers, and audit corrections.",
            RowCount: 9
        ),

        // ---------------------------------------------------------------
        // 6. CSV with [CsvColumn] attribute mapping (column names differ
        //    from property names) + intentional errors in sample data
        // ---------------------------------------------------------------
        ["payment-processing"] = new SampleInfo(
            OperationName: "payment-processing",
            FileName: "sample-payments.csv",
            FileContent: """
                txn_id,payer_email,payee_email,amount_usd,currency,payment_method,reference_note
                TXN-50001,alice@example.com,vendor-a@payments.com,1500.00,USD,ach,Monthly subscription
                TXN-50002,bob@example.com,vendor-b@payments.com,3200.50,EUR,wire,Q2 invoice payment
                TXN-50003,carol@example.com,vendor-c@payments.com,750.00,GBP,ach,Consulting fee
                TXN-50004,diana@example.com,vendor-d@payments.com,-200.00,USD,ach,INVALID - negative amount
                TXN-50005,edward@example.com,vendor-e@payments.com,5000.00,JPY,wire,INVALID - unsupported currency
                TXN-50006,frank@example.com,vendor-f@payments.com,12000.00,CAD,check,Annual license renewal
                TXN-50007,grace@example.com,grace@example.com,800.00,USD,ach,INVALID - same payer and payee
                TXN-50008,henry@example.com,vendor-h@payments.com,4500.00,USD,ach,Hardware procurement
                TXN-50009,irene@example.com,vendor-i@payments.com,2100.00,EUR,wire,SaaS platform annual
                TXN-50010,james@example.com,vendor-j@payments.com,950.00,GBP,bitcoin,INVALID - unsupported payment method
                """,
            Metadata: new { BatchApprover = "Dashboard Sample", SettlementDate = DateTime.UtcNow.AddDays(3).Date, Region = "US" },
            Description: "CSV with [CsvColumn] attribute mapping (txn_id -> TransactionId, payer_email -> PayerEmail, etc.). " +
                         "Includes 4 INTENTIONAL ERRORS to demo per-row error tracking: negative amount, unsupported currency (JPY), " +
                         "same payer/payee, unsupported payment method (bitcoin). 10 transactions, expect 6 success + 4 failures.",
            RowCount: 10
        )
    };

    public static SampleInfo? GetSample(string operationName) =>
        Samples.GetValueOrDefault(operationName);

    public static IEnumerable<object> GetAvailableSamples() =>
        Samples.Values.Select(s => new
        {
            s.OperationName,
            s.Description,
            s.RowCount,
            s.FileName
        });
}
