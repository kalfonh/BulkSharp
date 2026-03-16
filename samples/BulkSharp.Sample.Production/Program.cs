using BulkSharp;
using BulkSharp.Dashboard;
using BulkSharp.Sample.Production;
using BulkSharp.Sample.Production.Services;
using BulkSharp.Data.EntityFramework;
using BulkSharp.Files.S3;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("bulksharp")
    ?? throw new InvalidOperationException("Connection string 'bulksharp' not found.");

// SQL Server container uses a self-signed certificate
if (!connectionString.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
    connectionString += ";TrustServerCertificate=True";

builder.Services.AddBulkSharp(bulk => bulk
    .ConfigureOptions(opts =>
    {
        opts.MaxRowConcurrency = 100;
        opts.IncludeRowDataInErrors = true;
        opts.EnableOrphanedStepRecovery = false;
        opts.ServiceName = "production-service";
    })
    .UseFileStorage(fs => fs.UseS3(s3 =>
    {
        s3.BucketName = builder.Configuration["S3:BucketName"] ?? "bulksharp-files";
        s3.ServiceUrl = builder.Configuration["S3:ServiceUrl"];
        s3.ForcePathStyle = bool.Parse(builder.Configuration["S3:ForcePathStyle"] ?? "true");
        s3.Region = builder.Configuration["S3:Region"] ?? "us-east-1";
    }))
    .UseMetadataStorage(ms => ms.UseSqlServer(sql =>
    {
        sql.ConnectionString = connectionString;
        sql.MaxRetryCount = 3;
        sql.MaxRetryDelay = TimeSpan.FromSeconds(5);
    }))
    .UseScheduler(s => s.UseChannels(opts => opts.WorkerCount = 100))
);

builder.Services.AddBulkSharpDashboard();

builder.Services.AddSingleton<DatabaseReadySignal>();
builder.Services.AddHostedService<S3BucketInitializer>();
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddHostedService<DemoSignalService>();

var app = builder.Build();
app.MapDefaultEndpoints();

// Short-circuit DB-dependent API calls until DatabaseInitializer finishes.
// Dashboard pages load immediately with empty state instead of hanging.
var dbReady = app.Services.GetRequiredService<DatabaseReadySignal>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/bulks") && !dbReady.IsReady)
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"items\":[],\"totalCount\":0,\"page\":1,\"pageSize\":100}");
        return;
    }
    await next();
});

app.UseBulkSharpDashboard();

app.Run();
