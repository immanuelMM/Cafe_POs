namespace CafePos.Application.Features.Inventory;

/// <summary>
/// The location a read targets. <see cref="CrossLocation"/> is true when it is not the caller's active location (an Owner reading
/// another location); repositories then bypass the active-location query filter and restrict to <see cref="LocationId"/> explicitly.
/// </summary>
public readonly record struct LocationScope(Guid LocationId, bool CrossLocation);
