# 0007. Keep 4-digit time clock PINs

- **Status:** Accepted (user decision)
- **Date:** 2026-10-29

## Context
Staff clock in and out on a shared kiosk with a personal numeric PIN. A 4-digit PIN has only 10,000 values, so offline guessing is trivial if the hash
leaks and online guessing needs throttling. Longer PINs were considered; the user chose to keep 4 digits for usability on the till.

## Decision
PINs stay 4 to 6 digits (`^[0-9]{4,6}$`, `UserValidators`). The mitigation is online throttling:
- the punch endpoint requires a signed-in session with an active location, so it is not reachable anonymously;
- `TimeclockPunch` rate limit of 10 per 60 s per client IP + staff member (ADR 0006);
- an atomic per-user failure counter (`RecordClockPinFailureAsync`): after 5 wrong PINs the PIN is locked for 5 minutes (`423`), and success resets the counter;
- PINs are stored hashed with the Identity password hasher and never returned.

## Consequences
- Online guessing is bounded to a few attempts per window; a stolen database still exposes 4-digit PINs to offline brute force.
- A PIN only identifies a person at the till for time tracking; it does not grant API access.
- Revisit if the PIN ever protects more than time tracking.
