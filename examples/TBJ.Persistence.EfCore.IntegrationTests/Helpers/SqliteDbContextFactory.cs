using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace TBJ.Persistence.EfCore.IntegrationTests.Helpers;

/// <summary>
/// Factory that creates <see cref="SampleDbContext"/> instances backed by SQLite in-memory.
/// Each instance opens a shared in-memory connection so the schema persists for the lifetime of the connection.
/// Disposing the factory also disposes the underlying SQLite connection.
/// </summary>
public sealed class SqliteDbContextFactory : IDisposable
{
    // Shared SQLite in-memory connection — keeps the database alive between operations
    private readonly SqliteConnection connection;

    /// <summary>Opens a new shared SQLite in-memory connection and creates the schema.</summary>
    public SqliteDbContextFactory()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        // Create schema using a temporary context
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    /// <summary>Creates a <see cref="SampleDbContext"/> on the shared connection.</summary>
    public SampleDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(connection)
            .Options;

        return new SampleDbContext(options);
    }

    /// <summary>Creates a <see cref="SampleUnitOfWork"/> wrapping a new context on the shared connection.</summary>
    public SampleUnitOfWork CreateUnitOfWork() => new(CreateContext());

    /// <inheritdoc/>
    public void Dispose() => connection.Dispose();
}
