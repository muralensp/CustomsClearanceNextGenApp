# ADR-0041: Security Items That Needed No New Design - `/metrics`, Token Lifetime, Login Throttling

**Status:** Accepted
**Date:** 2026-10-19
**Deciders:** Product Owner (took the recommendations), Claude (Day 45, item 3)

## Context

The Day 41 security checklist (`docs/security/PHASE0_SECURITY_CHECKLIST.md`) left three items that could be settled
without designing anything new: the Gateway exposed `/metrics` without authentication (7.5), the access-token
lifetime was a hard-coded 8 hours against a Day 10 standard of 15 minutes plus refresh (1.6), and `/auth/login` had no
per-account throttling (1.7). The Product Owner accepted the recommendation for each.

## Decisions

1. **`/metrics` is internal-only.** The Gateway serves it only on an internal port (`Metrics:InternalPort`, default
   9100, which `docker-compose.yml` never publishes; the Gateway image listens on 8080 and 9100). On the public port
   every request for `/metrics` is a 404, which also stops the proxy catch-all forwarding it to the Api host's own
   endpoint. The test is the connection's real local port, **not the Host header**: a client controls the Host header,
   so a Host-based rule (`RequireHost`) could be bypassed by sending `Host: x:9100`. Prometheus scrapes
   `gateway:9100` and `api:8080` inside the compose network. Authentication on `/metrics` was not chosen: it would
   need a token for Prometheus and a credential to manage, for no gain over keeping the endpoint off the edge.
2. **Token lifetime is a setting.** `Jwt:AccessTokenMinutes`, default **60** (was 8 hours), accepted range 1 to 1440,
   checked at start-up and again by the issuer. 60 rather than 15 because there is no refresh-token flow: every
   expiry is a manual re-login, and 15 minutes would make Swagger and the dev stack tedious. A deployment that wants
   15 sets it. Real refresh needs the identity provider (OIDC), a Phase 1 decision.
3. **Login throttling is documented, not built.** A lockout needs decisions that are not mechanical: where failures
   are counted when there is more than one Api instance, how and when an account unlocks, and how to avoid letting an
   attacker lock legitimate users out. They belong with the real identity provider. Recorded as an accepted gap
   (checklist 1.7) that must be closed before any non-development exposure.

## Verification

Live on the compose stack: `/metrics`, `/Metrics`, `/metrics/`, `/metrics?x=1`, a POST and a spoofed
`Host: localhost:9100` request all return 404 on port 8080; port 9100 is not reachable from the host; Prometheus shows
`customsclearance-gateway` (`gateway:9100`) and `customsclearance-api` (`api:8080`) both `up`; `/health` and
`/__gateway/health` still return 200; a login returns an expiry 60.0 minutes ahead. `Platform.Security.Tests`: 28
tests (up from 18) cover the default, a configured value, out-of-range values at the issuer and at registration.

## Consequences

- Anything that scraped the Gateway's `/metrics` on port 8080 must move to 9100 (only Prometheus did).
- The Gateway now has two listeners; the image's `ASPNETCORE_URLS` carries both. A deployment that overrides the URLs
  must keep an internal metrics listener or the Gateway will serve no metrics.
- Tokens issued before an upgrade keep their original expiry; only new logins get 60 minutes.
- Users re-login hourly until refresh exists.
- The Gateway has no unit-test project, so the middleware is covered by the live checks above, not a unit test.

## Alternatives considered

- **Authenticate `/metrics`**: see decision 1.
- **`RequireHost` on the metrics endpoint**: spoofable, rejected.
- **15-minute default without refresh**: too disruptive; configurable instead.
- **Build lockout now**: would hard-code design decisions best made with the identity provider.

## Related

ADR-0004 (security standards), ADR-0017 (identity), ADR-0021 (metrics), ADR-0036 (Day 41 hardening), the Day 41
checklist items 1.6, 1.7, 7.5.
