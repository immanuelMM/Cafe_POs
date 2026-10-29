namespace CafePos.Application.Features.TimeClock;

public record TimeClockStaffDto(Guid UserId, string DisplayName, string Role, bool HasPin, bool ClockedIn, DateTime? OpenSince);

public record TimeEntryDto(Guid Id, Guid UserId, string DisplayName, Guid LocationId, DateTime ClockInAt, DateTime? ClockOutAt, decimal? Hours);

public record PunchResultDto(string Action, TimeEntryDto Entry);
