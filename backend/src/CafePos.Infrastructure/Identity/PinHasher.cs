using CafePos.Application.Common.Identity;
using Microsoft.AspNetCore.Identity;

namespace CafePos.Infrastructure.Identity;

/// <summary>Time clock PINs use the same salted PBKDF2 hasher as passwords (the hasher ignores the user argument).</summary>
internal sealed class PinHasher(IPasswordHasher<ApplicationUser> hasher) : IPinHasher
{
    private static readonly ApplicationUser Anonymous = new();

    public string Hash(string pin) => hasher.HashPassword(Anonymous, pin);

    public bool Verify(string hash, string pin) =>
        hasher.VerifyHashedPassword(Anonymous, hash, pin) != PasswordVerificationResult.Failed;
}
