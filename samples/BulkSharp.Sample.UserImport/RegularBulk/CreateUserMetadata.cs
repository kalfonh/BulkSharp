namespace BulkSharp.Sample.UserImport.RegularBulk;

public class CreateUserMetadata : IBulkMetadata
{
    public bool IsVIP { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;
    public string Department { get; set; } = "General";
    public int BatchSize { get; set; } = 100;
}
