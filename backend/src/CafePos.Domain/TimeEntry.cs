namespace CafePos.Domain;

/// <summary>A shift on the staff time clock. ClockOutAt is null while the shift is open (at most one open entry per user and location).</summary>
public class TimeEntry : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ClockInAt { get; set; }
    public DateTime? ClockOutAt { get; set; }
}
