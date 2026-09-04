using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WmhLms.Data;

namespace WmhLms.Tests;

/// <summary>
/// A real SQLite database held in memory. The in-memory EF provider does not
/// enforce foreign keys, unique indexes or cascades - exactly the behaviour
/// these tests need to trust - so the tests run on the real provider instead.
/// </summary>
public static class TestDb
{
    public static AppDbContext Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
