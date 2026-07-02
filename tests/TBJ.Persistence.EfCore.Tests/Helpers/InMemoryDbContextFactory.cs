using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TBJ.Persistence.EfCore.Tests.Helpers;

/// <summary>
/// Fabryka tworząca izolowane instancje <see cref="TestDbContext"/> oparte na dostawcy InMemory EF Core.
/// Każde wywołanie używa unikalnej nazwy bazy danych, aby zapobiec interferencji między testami.
/// Ostrzeżenie TransactionIgnoredWarning jest wyciszane — InMemory nie obsługuje transakcji,
/// ale API kontraktu powinno być weryfikowane bez zgłaszania wyjątków.
/// </summary>
public static class InMemoryDbContextFactory
{
    /// <summary>Tworzy nowy <see cref="TestDbContext"/> z izolowaną bazą danych in-memory.</summary>
    public static TestDbContext Create(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            // Wycisz ostrzeżenie o transakcjach — InMemory je ignoruje, co jest oczekiwanym zachowaniem w testach
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new TestDbContext(options);
    }
}
