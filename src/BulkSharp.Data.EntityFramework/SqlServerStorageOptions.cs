namespace BulkSharp.Data.EntityFramework;

/// <summary>
/// Configuration options for SQL Server metadata storage.
/// Bindable to IConfiguration via services.Configure&lt;SqlServerStorageOptions&gt;(section).
/// </summary>
public sealed class SqlServerStorageOptions
{
    /// <summary>SQL Server connection string. Required.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Maximum number of retry attempts for transient failures. Default: 3.</summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>Maximum delay between retry attempts. Default: 5 seconds.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("ConnectionString is required.", nameof(ConnectionString));
        if (MaxRetryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRetryCount), "MaxRetryCount must be >= 0.");
    }
}
