# Auth baseline (M0, captured on .NET 8 build, Development, seeded admin)

Access token header: {"alg":"HS256","typ":"JWT"}
Raw payload claim names (owner, no location), in order:
sub, http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier,
http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name,
http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress, role, jti, exp, iss, aud
With an active location, `location_id` is added (after jti). No nbf/iat. role = "Owner". iss=CafePos, aud=CafePos.Client.
Lifetime: exp - now = 30 min (AccessTokenMinutes=30).

Refresh cookie (Set-Cookie on login), value redacted:
cafepos_refresh=<value>; expires=<now+7d>; path=/api/v1/auth; samesite=lax; httponly
(no `secure` over http; Secure = Request.IsHttps)
