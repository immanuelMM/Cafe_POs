using CafePos.Api.Http;
using CafePos.Application.Common;
using CafePos.Application.Features.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

/// <summary>Cashiers see only their own orders; Managers and Admins see every order at the location.</summary>
[ApiController]
[Route("api/v1/orders")]
[Authorize]
public class OrdersController(OrderService orders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderDto>>> List([FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] Guid? locationId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        (await orders.ListAsync(status, from, to, locationId, page, pageSize, ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct) =>
        (await orders.GetAsync(id, ct)).ToActionResult();

    /// <summary>Create an OPEN order with a client-generated id. 201 when created, 200 if the id already exists (idempotent).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), 201)]
    [ProducesResponseType(typeof(OrderDto), 200)]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest req, CancellationToken ct)
    {
        var result = await orders.CreateAsync(req, ct);
        if (result.IsFailure) return result.Error!.ToProblem();
        var outcome = result.Value;
        return outcome.Created ? Created($"/api/v1/orders/{outcome.Order.Id}", outcome.Order) : Ok(outcome.Order);
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<OrderDto>> AddItem(Guid id, OrderLineRequest line, CancellationToken ct) =>
        (await orders.AddItemAsync(id, line, ct)).ToActionResult();

    /// <summary>Replaces the line; note the line gets a new item id.</summary>
    [HttpPut("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<OrderDto>> UpdateItem(Guid id, Guid itemId, OrderLineRequest line, CancellationToken ct) =>
        (await orders.UpdateItemAsync(id, itemId, line, ct)).ToActionResult();

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<OrderDto>> RemoveItem(Guid id, Guid itemId, CancellationToken ct) =>
        (await orders.RemoveItemAsync(id, itemId, ct)).ToActionResult();

    /// <summary>
    /// Pay and finalize. Requires an Idempotency-Key header (UUID recommended). Repeating the same key for the same order
    /// returns the original response (header Idempotent-Replayed: true) without charging or decrementing stock twice.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<OrderDto>> Complete(Guid id, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CompleteOrderRequest req, CancellationToken ct)
    {
        var result = await orders.CompleteAsync(id, idempotencyKey, req, ct);
        if (result.IsFailure) return result.Error!.ToProblem();
        if (result.Value.Replayed) Response.Headers["Idempotent-Replayed"] = "true";
        return result.Value.Order;
    }

    /// <summary>Cancel an OPEN order (before payment).</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id, CancellationToken ct) =>
        (await orders.CancelAsync(id, ct)).ToActionResult();

    /// <summary>Receipt data for a completed order. The frontend renders/prints it.</summary>
    [HttpGet("{id:guid}/receipt")]
    public async Task<ActionResult<ReceiptDto>> Receipt(Guid id, CancellationToken ct) =>
        (await orders.GetReceiptAsync(id, ct)).ToActionResult();
}
