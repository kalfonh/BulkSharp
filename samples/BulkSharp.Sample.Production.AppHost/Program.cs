var builder = DistributedApplication.CreateBuilder(args);

// SQL Server for metadata storage — ephemeral, recreated each run (sample app only)
var sqlPassword = builder.AddParameter("sql-password", "BulkSharp_Dev1!");
var sqlServer = builder.AddSqlServer("sql", password: sqlPassword);
var database = sqlServer.AddDatabase("bulksharp");

// LocalStack for S3-compatible file storage — ephemeral, recreated each run
var localstack = builder.AddContainer("localstack", "localstack/localstack", "latest")
    .WithEndpoint(targetPort: 4566, name: "gateway", scheme: "http")
    .WithEnvironment("SERVICES", "s3")
    .WithEnvironment("DEFAULT_REGION", "us-east-1");

var localstackEndpoint = localstack.GetEndpoint("gateway");

// Backend service (processes operations)
var webapp = builder.AddProject<Projects.BulkSharp_Sample_Production>("webapp")
    .WithReference(database)
    .WaitFor(database)
    .WaitFor(localstack)
    .WithEnvironment("S3__ServiceUrl", localstackEndpoint);

// Gateway service (aggregates backends, serves Dashboard UI)
builder.AddProject<Projects.BulkSharp_Sample_Gateway>("bulksharp-gateway")
    .WithReference(webapp)
    .WaitFor(webapp);

builder.Build().Run();
