using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TBJ.Persistence.EfCore.Abstractions;
using TBJ.Persistence.EfCore.WebApiSample.Domain;

namespace TBJ.Persistence.EfCore.WebApiSample.Controllers;

/// <summary>
/// Kontroler zamówień — demonstracja odpornych transakcji (ExecuteResilientTransactionAsync)
/// oraz dostępu do wielu repozytoriów w jednej transakcji.
/// Każde żądanie wymaga nagłówka X-Tenant-Id wskazującego bazę danych tenanta.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly IGenericUnitOfWork uow;
    private readonly ILogger<OrdersController> logger;

    /// <summary>Inicjalizuje kontroler z jednostką pracy i loggerem.</summary>
    public OrdersController(IGenericUnitOfWork uow, ILogger<OrdersController> logger)
    {
        this.uow = uow;
        this.logger = logger;
    }

    /// <summary>
    /// Zwraca listę zamówień bez szczegółów pozycji.
    /// </summary>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<Order>>> GetAll(CancellationToken cancellationToken = default)
    {
        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            var orders = await uow.Repository<Order>().GetAsync(
                orderBy: q => q.OrderByDescending(o => o.CreatedAt),
                cancellationToken: cancellationToken);

            logger.LogInformation("Pobrano {Count} zamówień.", orders.Count);
            return Ok(orders);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Zwraca zamówienie o podanym identyfikatorze wraz z pozycjami (Items).
    /// </summary>
    /// <param name="id">Identyfikator zamówienia.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Order>> GetById(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            var orders = await uow.Repository<Order>().GetAsync(
                filter: o => o.Id == id,
                include: q => q.Include(o => o.Items),
                cancellationToken: cancellationToken);

            var order = orders.FirstOrDefault();
            if (order is null)
                return NotFound($"Zamówienie o Id={id} nie istnieje.");

            logger.LogInformation("Pobrano zamówienie Id={Id} z {ItemCount} pozycjami.", id, order.Items.Count);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Tworzy nowe zamówienie z pozycjami w odpornej transakcji (ExecuteResilientTransactionAsync).
    /// Transakcja jest automatycznie ponawiana w przypadku przejściowych błędów bazy danych.
    /// Akcja musi być idempotentna — może być wywołana wielokrotnie.
    /// </summary>
    /// <param name="request">Dane zamówienia i jego pozycji.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpPost]
    [ProducesResponseType(typeof(Order), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Order>> Create([FromBody] CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (request.Items is null || request.Items.Count == 0)
            return BadRequest("Zamówienie musi zawierać co najmniej jedną pozycję.");

        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            Order? createdOrder = null;

            await uow.ExecuteResilientTransactionAsync(async (unitOfWork, ct) =>
            {
                // Idempotentność: wyczyść change tracker przed ponowieniem
                unitOfWork.ClearChangeTracker();

                var order = new Order
                {
                    CustomerName = request.CustomerName,
                    CustomerEmail = request.CustomerEmail,
                    CreatedAt = DateTime.UtcNow,
                    IsConfirmed = false,
                };

                unitOfWork.Repository<Order>().Insert(order);
                await unitOfWork.SaveChangesAsync(ct);

                var items = request.Items.Select(i => new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                }).ToList();

                unitOfWork.Repository<OrderItem>().Insert(items);
                await unitOfWork.SaveChangesAsync(ct);

                createdOrder = order;
            }, cancellationToken);

            logger.LogInformation("Utworzono zamówienie Id={Id} dla klienta '{Customer}' z {ItemCount} pozycjami.",
                createdOrder!.Id, createdOrder.CustomerName, request.Items.Count);

            return CreatedAtAction(nameof(GetById), new { id = createdOrder.Id }, createdOrder);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta lub walidacji: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Potwierdza zamówienie o podanym identyfikatorze (ustawia IsConfirmed = true).
    /// </summary>
    /// <param name="id">Identyfikator zamówienia do potwierdzenia.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    [HttpPatch("{id:int}/confirm")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Order>> Confirm(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await uow.Context.Database.EnsureCreatedAsync(cancellationToken);

            var order = await uow.Repository<Order>().FindAsync(id);
            if (order is null)
                return NotFound($"Zamówienie o Id={id} nie istnieje.");

            if (order.IsConfirmed)
                return Conflict($"Zamówienie Id={id} jest już potwierdzone.");

            order.IsConfirmed = true;
            uow.Repository<Order>().Update(order);
            await uow.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Potwierdzono zamówienie Id={Id}.", id);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Usuwa zamówienie wraz z jego pozycjami.
    /// </summary>
    /// <param name="id">Identyfikator zamówienia do usunięcia.</param>
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

            var orders = await uow.Repository<Order>().GetAsync(
                filter: o => o.Id == id,
                include: q => q.Include(o => o.Items),
                tracking: true,
                cancellationToken: cancellationToken);

            var order = orders.FirstOrDefault();
            if (order is null)
                return NotFound($"Zamówienie o Id={id} nie istnieje.");

            // Usuń najpierw pozycje, potem zamówienie
            if (order.Items.Count > 0)
                uow.Repository<OrderItem>().Delete(order.Items);

            uow.Repository<Order>().Delete(order);
            await uow.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Usunięto zamówienie Id={Id} wraz z {ItemCount} pozycjami.", id, order.Items.Count);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Błąd konfiguracji tenanta: {Message}", ex.Message);
            return BadRequest(ex.Message);
        }
    }
}

/// <summary>Żądanie utworzenia zamówienia z pozycjami.</summary>
public class CreateOrderRequest
{
    /// <summary>Imię i nazwisko klienta.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Adres e-mail klienta.</summary>
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>Lista pozycji zamówienia.</summary>
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}

/// <summary>Dane pojedynczej pozycji zamówienia w żądaniu tworzenia.</summary>
public class CreateOrderItemRequest
{
    /// <summary>Identyfikator produktu.</summary>
    public int ProductId { get; set; }

    /// <summary>Zamawiana ilość.</summary>
    public int Quantity { get; set; }

    /// <summary>Cena jednostkowa w chwili zamówienia.</summary>
    public decimal UnitPrice { get; set; }
}
