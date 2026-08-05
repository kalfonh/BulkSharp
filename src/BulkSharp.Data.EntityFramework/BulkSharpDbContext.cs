using BulkSharp.Core.Domain.Files;

namespace BulkSharp.Data.EntityFramework;

public class BulkSharpDbContext : DbContext
{
    public BulkSharpDbContext(DbContextOptions<BulkSharpDbContext> options) : base(options) { }

    /// <summary>
    /// Constructor for derived DbContext types that pass their own typed options.
    /// </summary>
    protected BulkSharpDbContext(DbContextOptions options) : base(options) { }

    public DbSet<BulkOperation> BulkOperations { get; set; }
    public DbSet<BulkFile> BulkFiles { get; set; }
    public DbSet<BulkRowRecord> BulkRowRecords { get; set; }
    public DbSet<BulkRowRetryHistory> BulkRowRetryHistory { get; set; }
    public DbSet<BulkOperationEventRecord> BulkOperationEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BulkOperation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OperationName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(100).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.MetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.NotificationOptionsJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.Source).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.OperationName);
            entity.HasIndex(e => e.CreatedBy);
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.Property(e => e.RetryCount).HasDefaultValue(0);
        });

        modelBuilder.Entity<BulkFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalFileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.StorageKey).IsRequired();
            entity.Property(e => e.StorageProvider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.UploadedBy).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => e.UploadedAt);
            entity.HasIndex(e => e.IsDeleted);
            entity.HasIndex(e => new { e.StorageProvider, e.StorageKey });
        });

        modelBuilder.Entity<BulkRowRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RowId).HasMaxLength(200);
            entity.Property(e => e.StepName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.SignalKey).HasMaxLength(500);
            entity.Property(e => e.RowData).HasColumnType("nvarchar(max)");
            entity.HasIndex(e => e.BulkOperationId);
            entity.HasIndex(e => new { e.BulkOperationId, e.RowNumber, e.StepIndex }).IsUnique();
            entity.HasIndex(e => e.SignalKey).HasFilter("[SignalKey] IS NOT NULL");
            entity.HasIndex(e => e.State);
            entity.HasIndex(e => new { e.BulkOperationId, e.ErrorType }).HasFilter("[ErrorType] IS NOT NULL");
            entity.Property(e => e.RetryAttempt).HasDefaultValue(0);
            entity.Property(e => e.RetryFromStepIndex);
        });

        modelBuilder.Entity<BulkRowRetryHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ErrorMessage).HasColumnType("nvarchar(max)");
            entity.Property(e => e.RowData).HasColumnType("nvarchar(max)");
            entity.HasIndex(e => new { e.BulkOperationId, e.RowNumber, e.StepIndex, e.Attempt }).IsUnique();
            entity.HasIndex(e => e.BulkOperationId);
            entity.HasIndex(e => new { e.BulkOperationId, e.RowNumber });
        });

        modelBuilder.Entity<BulkOperationEventRecord>(entity =>
        {
            // Sequence is the key and is database-generated. The identity column is what
            // makes it monotonic across every instance of a scaled-out service, which is
            // the whole reason a durable event store exists.
            entity.HasKey(e => e.Sequence);
            entity.Property(e => e.Sequence).ValueGeneratedOnAdd();

            entity.Property(e => e.OperationName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(2000).IsRequired();

            // Clients poll "events for this operation after sequence N" and
            // "all events after sequence N"; both are covered here.
            entity.HasIndex(e => new { e.OperationId, e.Sequence });
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
