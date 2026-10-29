namespace CafePos.Application.Features.TimeClock;

public record PunchRequest
{
    public Guid? UserId { get; init; }
    public string Pin { get; init; } = "";
}
