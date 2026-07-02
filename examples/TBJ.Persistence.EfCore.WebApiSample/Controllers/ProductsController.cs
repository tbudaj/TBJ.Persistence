using Microsoft.AspNetCore.Mvc;
using System.Linq.Expressions;
using TBJ.Persistence.EfCore.Abstractions;
using TBJ.Persistence.EfCore.WebApiSample.Domain;

namespace TBJ.Persistence.EfCore.WebApiSample.Controllers;

/// <summary>
/// Kontroler produktów — demonstracja pełnego CRUD oraz operacji masowych (bulk).
/// Każde żądanie wymaga nagłówka X-Tenant-Id wskazującego bazę danych tenanta.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly IGenericUnitOfWork uow;
    private readonly ILogger<ProductsController> logger;

    /// <summary>Inicjalizuje kontroler z jednostką pracy i loggerem.</summary>
    public ProductsController(IGenericUnitOfWork uow, ILogger<ProductsController> logger)
    {
        this.uow = uow;
        this.logger = logger;
    }

    /// <summary>
    /// Zwraca listę produktów z opcjonalnym filtrem kategorii i stronicowaniem.
    /// </summary>
    /// <param name="category">Opcjonalny filtr kategorii (case-sensitive).</param>
    /// <param name="page">Numer strony (od 1). Domyślnie 1.</param>
    /// <param name="pageSize">Liczba elementów na stronie. Domyślnie 20, maksymalnie 100.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Product>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<Product>>> GetAll(
        [FromQuery] string? category = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) return BadRequest("Numer strony musi być >= 1.");
        pageSize = Math.Clamp(pageSize, 1, 100);

        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            Expression<Func<Product, bool>>? filter = category is not null
                ? p => p.Category == category
                : null;

            var skip = (page - 1) * pageSize;
            var products = await uow.Repository<Product>().GetAsync(
                filter: filter,
                orderBy: q => q.OrderBy(p => p.Id),
                skip: skip,
                take: pageSize,
                cancellationToken: cancellationToken);

            logger.LogInformation("Pobrano {Count} produktów (strona {Page}, rozmiar {PageSize}, kategoria: {Category}).",
                products.Count, page, pageSize, category ?? "wszystkie");

            return Ok(products);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Zwraca produkt o podanym identyfikatorze.
    /// </summary>
    /// <param name="id">Identyfikator produktu.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> GetById(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            var product = await uow.Repository<Product>().FindAsync(id);
            if (product is null)
                return NotFound($"Produkt o Id={id} nie istnieje.");

            logger.LogInformation("Pobrano produkt Id={Id}.", id);
            return Ok(product);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Tworzy nowy produkt.
    /// </summary>
    /// <param name="product">Dane nowego produktu.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpPost]
    [ProducesResponseType(typeof(Product), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Product>> Create([FromBody] Product product, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            product.Id = 0;
            uow.Repository<Product>().Insert(product);
            await uow.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Utworzono produkt Id={Id}, Nazwa='{Name}'.", product.Id, product.Name);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Aktualizuje istniejący produkt.
    /// </summary>
    /// <param name="id">Identyfikator produktu do zaktualizowania.</param>
    /// <param name="updated">Nowe dane produktu.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Product>> Update(int id, [FromBody] Product updated, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            var existing = await uow.Repository<Product>().FindAsync(id);
            if (existing is null)
                return NotFound($"Produkt o Id={id} nie istnieje.");

            existing.Name = updated.Name;
            existing.Price = updated.Price;
            existing.Category = updated.Category;
            existing.Stock = updated.Stock;
            existing.IsActive = updated.IsActive;

            uow.Repository<Product>().Update(existing);
            await uow.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Zaktualizowano produkt Id={Id}.", id);
            return Ok(existing);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Usuwa produkt o podanym identyfikatorze.
    /// </summary>
    /// <param name="id">Identyfikator produktu do usunięcia.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            var existing = await uow.Repository<Product>().FindAsync(id);
            if (existing is null)
                return NotFound($"Produkt o Id={id} nie istnieje.");

            uow.Repository<Product>().Delete(existing);
            await uow.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Usunięto produkt Id={Id}.", id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Wstawia wiele produktów jednocześnie przy użyciu operacji masowej InsertRangeAsync.
    /// Operacja pomija change tracker — efektywna dla dużych zbiorów danych.
    /// </summary>
    /// <param name="products">Lista produktów do wstawienia.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpPost("bulk")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<int>> BulkInsert([FromBody] IEnumerable<Product> products, CancellationToken cancellationToken = default)
    {
        var list = products?.ToList();
        if (list is null || list.Count == 0)
            return BadRequest("Lista produktów jest pusta.");

        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            foreach (var p in list)
                p.Id = 0;

            await uow.Repository<Product>().InsertRangeAsync(list, cancellationToken: cancellationToken);

            logger.LogInformation("Wstawiono masowo {Count} produktów.", list.Count);
            return Ok(list.Count);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Dezaktywuje wszystkie produkty w podanej kategorii przy użyciu masowej aktualizacji UpdateRangeAsync.
    /// Operacja wykonywana bezpośrednio w bazie bez wczytywania encji do pamięci.
    /// </summary>
    /// <param name="category">Nazwa kategorii do dezaktywacji.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpPatch("deactivate-category/{category}")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<int>> DeactivateCategory(string category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            return BadRequest("Nazwa kategorii jest wymagana.");

        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            var affected = await uow.Repository<Product>().UpdateRangeAsync(
                whereExpression: p => p.Category == category && p.IsActive,
                setPropertyCalls: s => s.SetProperty(p => p.IsActive, false),
                cancellationToken: cancellationToken);

            logger.LogInformation("Dezaktywowano {Count} produktów w kategorii '{Category}'.", affected, category);
            return Ok(affected);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Usuwa wszystkie nieaktywne produkty przy użyciu masowej operacji DeleteRangeAsync.
    /// Operacja wykonywana bezpośrednio w bazie bez wczytywania encji do pamięci.
    /// </summary>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpDelete("inactive")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<int>> DeleteInactive(CancellationToken cancellationToken = default)
    {
        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            var affected = await uow.Repository<Product>().DeleteRangeAsync(
                p => !p.IsActive,
                cancellationToken);

            logger.LogInformation("Usunięto {Count} nieaktywnych produktów.", affected);
            return Ok(affected);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }
}
