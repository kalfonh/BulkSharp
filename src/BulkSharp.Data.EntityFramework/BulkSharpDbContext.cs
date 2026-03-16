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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BulkOperation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OperationName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(100).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.MetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.Source).HasMaxLength(200);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.OperationName);
            entity.HasIndex(e => e.CreatedBy);
            entity.Property(e => e.RowVersion).IsRowVersion();
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
        });
    }
}
