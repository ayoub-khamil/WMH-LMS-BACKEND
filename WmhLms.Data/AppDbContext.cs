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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();

        b.Entity<Section>().HasOne<Course>().WithMany(c => c.Sections)
            .HasForeignKey(s => s.CourseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Item>().HasOne<Section>().WithMany(s => s.Items)
            .HasForeignKey(i => i.SectionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Question>().HasOne<Item>().WithMany(i => i.Questions)
            .HasForeignKey(q => q.ItemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Option>().HasOne<Question>().WithMany(q => q.Options)
            .HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}
