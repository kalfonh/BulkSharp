using BulkSharp.Core.Abstractions.Operations;
using BulkSharp.Core.Abstractions.Processing;
using BulkSharp.Core.Abstractions.Storage;
using BulkSharp.Core.Attributes;
using BulkSharp.Core.Exceptions;
using System.Text.RegularExpressions;

namespace BulkSharp.Sample.Dashboard.Examples;

/// <summary>
/// Step-based bulk operation with complex validation and 4 async processing steps.
/// Demonstrates IBulkPipelineOperation with real-world onboarding workflow.
///
/// Steps (each with its own retry policy):
///   1. Create AD Account   (MaxRetries = 3) - AD API can be flaky
///   2. Provision Badge      (MaxRetries = 2) - Badge system moderately reliable
///   3. Submit Equipment Req (MaxRetries = 2) - IT ticketing system
///   4. Send Welcome Email   (MaxRetries = 1) - Email delivery reliable
///
/// Also showcases:
///   - Complex cross-field validation (manager required for non-executives)
///   - Regex validation (email format, phone E.164)
///   - Date validation (start date must be future)
///   - Enum-like string validation (equipment tier)
///   - No [CsvColumn] attributes - auto-mapped by property name matching CSV headers
/// </summary>
[BulkOperation("employee-onboarding",
    Description = "Step-based employee onboarding with 4 async steps: AD Account Creation (3 retries), " +
                  "Badge Provisioning (2 retries), Equipment Request (2 retries), Welcome Email (1 retry). " +
                  "Each step has independent retry with exponential backoff. Validates corporate email, " +
                  "E.164 phone format, future start dates, equipment tiers, and cross-field rules " +
                  "(manager required for non-executive roles). CSV: FirstName, LastName, Email, Phone, " +
                  "Department, JobTitle, ManagerEmail, StartDate, Office, EquipmentTier.")]
public partial class EmployeeOnboardingOperation : IBulkPipelineOperation<OnboardingMetadata, OnboardingRow>
{
    public Task ValidateMetadataAsync(OnboardingMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.HrOperator))
            throw new BulkValidationException("HrOperator is required - who is running this onboarding batch?");

        if (string.IsNullOrWhiteSpace(metadata.Tenant))
            throw new BulkValidationException("Tenant is required - which organizational tenant are these employees joining?");

        var validTenants = new[] { "us-east", "us-west", "eu-central", "ap-southeast" };
        if (!validTenants.Contains(metadata.Tenant, StringComparer.OrdinalIgnoreCase))
            throw new BulkValidationException(
                $"Invalid tenant '{metadata.Tenant}'. Must be one of: {string.Join(", ", validTenants)}");

        return Task.CompletedTask;
    }

    public Task ValidateRowAsync(OnboardingRow row, OnboardingMetadata metadata, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(row.FirstName))
            errors.Add("FirstName is required");

        if (string.IsNullOrWhiteSpace(row.LastName))
            errors.Add("LastName is required");

        if (string.IsNullOrWhiteSpace(row.Email))
            errors.Add("Email is required");
        else if (!row.Email.EndsWith("@company.com", StringComparison.OrdinalIgnoreCase))
            errors.Add($"Email must be a @company.com address, got '{row.Email}'");
        else if (!EmailPattern().IsMatch(row.Email))
            errors.Add($"Email format is invalid: '{row.Email}'");

        if (!string.IsNullOrWhiteSpace(row.Phone) && !PhonePattern().IsMatch(row.Phone))
            errors.Add($"Phone must be in format +1234567890 (10-15 digits), got '{row.Phone}'");

        if (string.IsNullOrWhiteSpace(row.Department))
            errors.Add("Department is required");

        if (string.IsNullOrWhiteSpace(row.JobTitle))
            errors.Add("JobTitle is required");

        var validTiers = new[] { "Standard", "Enhanced", "Executive" };
        if (string.IsNullOrWhiteSpace(row.EquipmentTier))
            errors.Add("EquipmentTier is required (Standard, Enhanced, or Executive)");
        else if (!validTiers.Contains(row.EquipmentTier, StringComparer.OrdinalIgnoreCase))
            errors.Add($"EquipmentTier must be Standard, Enhanced, or Executive - got '{row.EquipmentTier}'");

        if (row.StartDate == default)
            errors.Add("StartDate is required");
        else if (row.StartDate.Date < DateTime.UtcNow.Date)
            errors.Add($"StartDate must be today or later, got {row.StartDate:yyyy-MM-dd}");

        // Cross-field: non-executive titles require a manager
        var isExecutive = row.JobTitle?.Contains("VP", StringComparison.OrdinalIgnoreCase) == true
                       || row.JobTitle?.Contains("Director", StringComparison.OrdinalIgnoreCase) == true
                       || row.JobTitle?.Contains("Chief", StringComparison.OrdinalIgnoreCase) == true
                       || row.JobTitle?.Contains("President", StringComparison.OrdinalIgnoreCase) == true;

        if (!isExecutive && string.IsNullOrWhiteSpace(row.ManagerEmail))
            errors.Add("ManagerEmail is required for non-executive positions");

        if (!string.IsNullOrWhiteSpace(row.ManagerEmail) &&
            !row.ManagerEmail.EndsWith("@company.com", StringComparison.OrdinalIgnoreCase))
            errors.Add($"ManagerEmail must be a @company.com address, got '{row.ManagerEmail}'");

        if (errors.Count > 0)
            throw new BulkValidationException(
                $"Validation failed for {row.FirstName} {row.LastName}: {string.Join("; ", errors)}");

        row.RowId ??= row.Email;

        return Task.CompletedTask;
    }

    [BulkStep("Create AD Account", Order = 0, MaxRetries = 3)]
    public async Task CreateAdAccountAsync(OnboardingRow row, OnboardingMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(80, ct);
        var username = $"{row.FirstName.ToLowerInvariant()}.{row.LastName.ToLowerInvariant()}";
        Console.WriteLine($"[onboarding] AD account created: {username} | dept: {row.Department} | tenant: {metadata.Tenant}");
    }

    [BulkStep("Provision Badge", Order = 1, MaxRetries = 2)]
    public async Task ProvisionBadgeAsync(OnboardingRow row, OnboardingMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(40, ct);
        var badgeId = $"BDG-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        Console.WriteLine($"[onboarding] Badge {badgeId} provisioned for {row.FirstName} {row.LastName} | office: {row.Office}");
    }

    [BulkStep("Submit Equipment Request", Order = 2, MaxRetries = 2)]
    public async Task SubmitEquipmentRequestAsync(OnboardingRow row, OnboardingMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(60, ct);
        var ticketId = $"IT-{Random.Shared.Next(10000, 99999)}";
        Console.WriteLine($"[onboarding] Equipment ticket {ticketId}: {row.EquipmentTier} tier for {row.FirstName} {row.LastName} | start: {row.StartDate:yyyy-MM-dd}");
    }

    [BulkStep("Send Welcome Email", Order = 3, MaxRetries = 1)]
    public async Task SendWelcomeEmailAsync(OnboardingRow row, OnboardingMetadata metadata, CancellationToken ct = default)
    {
        // Simulate a long Network call
        await Task.Delay(1000, ct);
        Console.WriteLine($"[onboarding] Welcome email sent to {row.Email} | operator: {metadata.HrOperator}");
    }

    [GeneratedRegex(@"^[a-zA-Z0-9._%+-]+@company\.com$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+\d{10,15}$")]
    private static partial Regex PhonePattern();
}

public class OnboardingMetadata : IBulkMetadata
{
    public string HrOperator { get; set; } = string.Empty;
    public string Tenant { get; set; } = string.Empty;
    public bool SendImmediateWelcome { get; set; } = true;
}

public class OnboardingRow : IBulkRow
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string ManagerEmail { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public string Office { get; set; } = string.Empty;
    public string EquipmentTier { get; set; } = string.Empty;
    public string? RowId { get; set; }
}
