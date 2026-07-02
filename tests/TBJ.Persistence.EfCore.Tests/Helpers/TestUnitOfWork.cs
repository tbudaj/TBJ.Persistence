using Microsoft.Extensions.Logging;
using TBJ.Persistence.EfCore.Implementation;

namespace TBJ.Persistence.EfCore.Tests.Helpers;

/// <summary>Concrete UoW used in unit tests — wraps <see cref="TestDbContext"/>.</summary>
public class TestUnitOfWork : GenericUnitOfWork<TestDbContext>
{
    /// <summary>Initializes the test UoW with the supplied context and optional logger factory.</summary>
    public TestUnitOfWork(TestDbContext dbContext, ILoggerFactory? loggerFactory = null)
        : base(dbContext, loggerFactory) { }
}
