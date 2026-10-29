using CafePos.Application.Common;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using CafePos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

/// <summary>Cashiers see only their own orders; Managers and Admins see every order at the location.</summary>
[ApiController]
[Route("api/v1/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orders;
    public OrdersController(OrderService orders) => _orders = orders;

    private Guid UserId => User.RequireUserId();
    private bool ManagerUp => User.IsManagerUp();

    [HttpGet]
    public Task<PagedResult<OrderDto>> List([FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] Guid? locationId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        _orders.ListAsync(UserId, ManagerUp, status, from, to, locationId, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<OrderDto> Get(Guid id, CancellationToken ct) => _orders.GetAsync(id, UserId, ManagerUp, ct);

    /// <summary>Create an OPEN order with a client-generated id. 201 when created, 200 if the id already exists (idempotent).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), 201)]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest req, CancellationToken ct)
    {
        var (order, created) = await _orders.CreateAsync(req, UserId, User.Identity?.Name ?? "", ct);
        return created ? Created($"/api/v1/orders/{order.Id}", order) : Ok(order);
    }

    [HttpPost("{id:guid}/items")]
    public Task<OrderDto> AddItem(Guid id, OrderLineRequest line, CancellationToken ct) => _orders.AddItemAsync(id, line, UserId, ManagerUp, ct);

    /// <summary>Replaces the line; note the line gets a new item id.</summary>
    [HttpPut("{id:guid}/items/{itemId:guid}")]
    public Task<OrderDto> UpdateItem(Guid id, Guid itemId, OrderLineRequest line, CancellationToken ct) =>
        _orders.UpdateItemAsync(id, itemId, line, UserId, ManagerUp, ct);

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public Task<OrderDto> RemoveItem(Guid id, Guid itemId, CancellationToken ct) => _orders.RemoveItemAsync(id, itemId, UserId, ManagerUp, ct);

    /// <summary>
    /// Pay and finalize. Requires an Idempotency-Key header (UUID recommended). Repeating the same key for the same order
    /// returns the original response (header Idempotent-Replayed: true) without charging or decrementing stock twice.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<OrderDto>> Complete(Guid id, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CompleteOrderRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw ApiException.BadRequest("Idempotency-Key required", "Send an Idempotency-Key header (max 200 chars).");
        var (order, replayed) = await _orders.CompleteAsync(id, idempotencyKey.Trim(), req, UserId, ManagerUp, ct);
        if (replayed) Response.Headers["Idempotent-Replayed"] = "true";
        return order;
    }

    /// <summary>Cancel an OPEN order (before payment).</summary>
    [HttpPost("{id:guid}/cancel")]
    public Task<OrderDto> Cancel(Guid id, CancellationToken ct) => _orders.CancelAsync(id, UserId, ManagerUp, ct);

    /// <summary>Receipt data for a completed order. The frontend renders/prints it.</summary>
    [HttpGet("{id:guid}/receipt")]
    public Task<ReceiptDto> Receipt(Guid id, CancellationToken ct) => _orders.GetReceiptAsync(id, UserId, ManagerUp, ct);
}
