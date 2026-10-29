namespace CafePos.Application.Common;

/// <summary>
/// Raised by the unit of work when saving violates a unique constraint (a concurrent request won the race). Use cases that expect
/// such races catch it and turn it into an idempotent result or a 409.
/// </summary>
public sealed class UniqueConstraintViolationException(Exception inner)
    : Exception("A unique constraint was violated.", inner);
