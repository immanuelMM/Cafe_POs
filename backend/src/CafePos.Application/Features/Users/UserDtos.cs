using CafePos.Application.Features.Locations;

namespace CafePos.Application.Features.Users;

public record UserDto(Guid Id, string Email, string DisplayName, bool Active, bool IsOwner, IReadOnlyList<LocationRoleDto> Locations);
