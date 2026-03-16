namespace BulkSharp.Sample.UserImport.RegularBulk;

[CsvSchema("1.0")]
public class CreateUserRow : IBulkRow
{
    [CsvColumn(nameof(FirstName))]
    public string FirstName { get; set; } = string.Empty;
    
    [CsvColumn(nameof(LastName))]
    public string LastName { get; set; } = string.Empty;
    
    [CsvColumn(nameof(Email))]
    public string Email { get; set; } = string.Empty;

    public string? RowId { get; set; }
}
