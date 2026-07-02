using Microsoft.Extensions.Logging;
using TBJ.Persistence.EfCore.Implementation;

namespace TBJ.Persistence.EfCore.IntegrationTests.Helpers;

/// <summary>
/// Sample UnitOfWork for integration tests.
/// Inherits from <see cref="GenericUnitOfWork{TDbContext}"/> — this is the only class
/// that consumers need to create to wire up the persistence layer.
/// </summary>
public class SampleUnitOfWork : GenericUnitOfWork<SampleDbContext>
{
    /// <summary>Initializes the UoW with the supplied context and optional logger factory.</summary>
    public SampleUnitOfWork(SampleDbContext dbContext, ILoggerFactory? loggerFactory = null)
        : base(dbContext, loggerFactory) { }
}
