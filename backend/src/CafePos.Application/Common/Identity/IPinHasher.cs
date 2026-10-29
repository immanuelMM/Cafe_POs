namespace CafePos.Application.Common.Identity;

/// <summary>Hashes and verifies time clock PINs (same algorithm as passwords: ASP.NET Core Identity password hasher).</summary>
public interface IPinHasher
{
    string Hash(string pin);

    bool Verify(string hash, string pin);
}
