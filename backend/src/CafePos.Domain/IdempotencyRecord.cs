namespace CafePos.Domain;

public class IdempotencyRecord : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string Key { get; set; } = "";
    public Guid OrderId { get; set; }
    public int StatusCode { get; set; }
    public string ResponseJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
