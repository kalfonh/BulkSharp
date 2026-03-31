namespace BulkSharp.Sample.Production.Configuration;

public sealed class SmtpSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "bulksharp@example.com";
    public string FromName { get; set; } = "BulkSharp";
    public bool UseSsl { get; set; } = true;
}
