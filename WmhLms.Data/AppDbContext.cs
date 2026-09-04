using Microsoft.EntityFrameworkCore;
using WmhLms.Data.Entities;

namespace WmhLms.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Option> Options => Set<Option>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<QuizLock> QuizLocks => Set<QuizLock>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
            e.Property(u => u.LastName).IsRequired().HasMaxLength(100);
            e.Property(u => u.Email).IsRequired().HasMaxLength(256);
            e.Property(u => u.Role).IsRequired().HasMaxLength(20);
            e.Property(u => u.Status).IsRequired().HasMaxLength(20);
            // SQLite NOCASE keeps the uniqueness guarantee case-insensitive, so
            // "A@b.com" and "a@b.com" cannot both exist. Postgres: swap for citext.
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).UseCollation("NOCASE");
        });

        // ── Course tree: Course > Section > Item > Question > Option ──
        b.Entity<Course>(e =>
        {
            e.Property(c => c.Title).IsRequired().HasMaxLength(300);
            e.Property(c => c.Description).IsRequired().HasMaxLength(4000);
            e.Property(c => c.Status).IsRequired().HasMaxLength(20);
        });

        b.Entity<Section>(e =>
        {
            e.Property(s => s.Title).IsRequired().HasMaxLength(300);
            e.HasOne<Course>().WithMany(c => c.Sections)
                .HasForeignKey(s => s.CourseId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.CourseId);
        });

        b.Entity<Item>(e =>
        {
            e.Property(i => i.Title).IsRequired().HasMaxLength(300);
            e.Property(i => i.Type).IsRequired().HasMaxLength(20);
            e.HasOne<Section>().WithMany(s => s.Items)
                .HasForeignKey(i => i.SectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(i => i.SectionId);
        });

        b.Entity<Question>(e =>
        {
            e.Property(q => q.Type).IsRequired().HasMaxLength(30);
            e.Property(q => q.Prompt).IsRequired().HasMaxLength(2000);
            e.HasOne<Item>().WithMany(i => i.Questions)
                .HasForeignKey(q => q.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(q => q.ItemId);
        });

        b.Entity<Option>(e =>
        {
            e.Property(o => o.Text).IsRequired().HasMaxLength(1000);
            e.HasOne<Question>().WithMany(q => q.Options)
                .HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(o => o.QuestionId);
        });

        // ── Progress ──
        // Real foreign keys: deleting a course or a user now takes its
        // assignments and quiz locks with it instead of orphaning rows.
        b.Entity<Assignment>(e =>
        {
            e.Property(a => a.CompletedItemIdsJson).IsRequired();
            e.Property(a => a.Status).IsRequired().HasMaxLength(20);
            e.HasOne<Course>().WithMany()
                .HasForeignKey(a => a.CourseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany()
                .HasForeignKey(a => a.AgentId).OnDelete(DeleteBehavior.Cascade);
            // One assignment per (course, agent) — closes the check-then-insert race.
            e.HasIndex(a => new { a.CourseId, a.AgentId }).IsUnique();
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.ActorEmail).IsRequired().HasMaxLength(256);
            e.Property(a => a.Action).IsRequired().HasMaxLength(60);
            e.Property(a => a.TargetType).IsRequired().HasMaxLength(40);
            e.Property(a => a.TargetLabel).IsRequired().HasMaxLength(256);
            e.Property(a => a.Detail).IsRequired().HasMaxLength(1000);
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => new { a.TargetType, a.TargetId });
            // Deliberately no FK to Users: the trail must survive the account.
        });

        b.Entity<QuizLock>(e =>
        {
            e.Property(l => l.ReviewedItemIdsJson).IsRequired();
            e.HasOne<User>().WithMany()
                .HasForeignKey(l => l.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Course>().WithMany()
                .HasForeignKey(l => l.CourseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Item>().WithMany()
                .HasForeignKey(l => l.QuizItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.AgentId, l.QuizItemId }).IsUnique();
        });
    }
}
