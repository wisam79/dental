using DentalID.Core.Entities;
using DentalID.Core.Enums;
using DentalID.Infrastructure.Data.Converters;
using DentalID.Core.Interfaces; // For IEncryptionService
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DentalID.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<DentalImage> DentalImages => Set<DentalImage>();
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<AIModel> AIModels => Set<AIModel>();

    private readonly IEncryptionService _encryptionService;

    public AppDbContext(DbContextOptions<AppDbContext> options, IEncryptionService encryptionService) : base(options) 
    {
        _encryptionService = encryptionService;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Converter
        var encryptedConverter = new EncryptedValueConverter(_encryptionService);

        // ── User ──
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).IsRequired().HasMaxLength(100); 
            // Username is often used for lookups, encryption might break unique index or simple search if not deterministic. 
            // For now, let's encrypt Email and FullName.
            e.Property(u => u.FullName).HasConversion(encryptedConverter);
            e.Property(u => u.Email).HasConversion(encryptedConverter);

            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(30);
            e.Property(u => u.RowVersion).IsConcurrencyToken();
        });

        // ── Subject ──
        modelBuilder.Entity<Subject>(e =>
        {
            e.HasIndex(s => s.SubjectId).IsUnique();
            // Bug #52: Keep index for structural purposes only — with random-IV encryption, equality search
            // must be done client-side (see SubjectRepository.SearchAsync). The index helps EF/SQLite scans.
            e.HasIndex(s => s.NationalId);
            e.Property(s => s.SubjectId).IsRequired().HasMaxLength(50);
            e.Property(s => s.FullName).HasConversion(encryptedConverter); // Encrypt Patient Name
            // Bug #52: NationalId contains PII — must be encrypted at rest like FullName
            e.Property(s => s.NationalId).HasConversion(encryptedConverter);
            e.Property(s => s.RowVersion).IsConcurrencyToken(); // Optimistic Concurrency (Manual)
            
            e.HasOne(s => s.CreatedBy)
             .WithMany(u => u.CreatedSubjects)
             .HasForeignKey(s => s.CreatedById)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── DentalImage ──
        modelBuilder.Entity<DentalImage>(e =>
        {
            e.HasIndex(d => d.IsProcessed);
            e.Property(d => d.ImagePath).IsRequired();
            e.Property(d => d.ImageType).HasConversion<string>().HasMaxLength(30);
            e.Property(d => d.JawType).HasConversion<string>().HasMaxLength(10);
            e.Property(d => d.DigitalSeal).HasMaxLength(100);
            e.HasOne(d => d.Subject)
             .WithMany(s => s.DentalImages)
             .HasForeignKey(d => d.SubjectId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Case ──
        modelBuilder.Entity<Case>(e =>
        {
            e.HasIndex(c => c.CaseNumber).IsUnique();
            e.Property(c => c.CaseNumber).IsRequired().HasMaxLength(50);
            e.Property(c => c.Title).IsRequired().HasMaxLength(300).HasConversion(encryptedConverter); // Encrypt Case Title
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(c => c.Priority).HasConversion<string>().HasMaxLength(20);
            e.HasOne(c => c.AssignedTo)
             .WithMany(u => u.AssignedCases)
             .HasForeignKey(c => c.AssignedToId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(c => c.CreatedBy)
             .WithMany()
             .HasForeignKey(c => c.CreatedById)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Match ──
        modelBuilder.Entity<Match>(e =>
        {
            e.HasOne(m => m.Case)
             .WithMany(c => c.Matches)
             .HasForeignKey(m => m.CaseId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.QueryImage)
             .WithMany(d => d.QueryMatches)
             .HasForeignKey(m => m.QueryImageId)
             .OnDelete(DeleteBehavior.Cascade);
            // Bug #53/54: MatchedImage relationship was missing — asymmetric config caused EF shadow property issues
            e.HasOne(m => m.MatchedImage)
             .WithMany()
             .HasForeignKey(m => m.MatchedImageId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.MatchedSubject)
             .WithMany(s => s.Matches)
             .HasForeignKey(m => m.MatchedSubjectId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.ConfirmedBy)
             .WithMany()
             .HasForeignKey(m => m.ConfirmedById)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── AuditLogEntry ──
        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.HasIndex(a => a.Timestamp);
            e.Property(a => a.Action).IsRequired().HasMaxLength(200);
            e.HasOne(a => a.User)
             .WithMany(u => u.AuditLogs)
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── AIModel ──
        modelBuilder.Entity<AIModel>(e =>
        {
            e.Property(m => m.Name).IsRequired().HasMaxLength(100);
            e.Property(m => m.Type).IsRequired().HasMaxLength(50);
            e.Property(m => m.FilePath).IsRequired();
        });
    }
}



