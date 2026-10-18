# ADR-0031: Notification Base (Day 37)

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 8 (Days 36-40), Day 37. Task: "Build basic notification service (email/in-app
event-driven notification skeleton)." Deliverable: "Sample notification fires on a test event."
Independent of Day 36 (configuration). Day 40's end-to-end smoke test ends with "... audit ->
notification", so the skeleton must already be reachable from the event bus.

## Decision

Four design questions were settled with the user before any code was written:

1. **Placement - `Platform.Notifications` library inside the `Api` host**, like
   `Platform.Configuration`/`Platform.RulesEngine` (ADR-0003). A separate worker container was
   rejected as heavier than the Modular Monolith calls for.
2. **Channels - in-app (PostgreSQL) plus email, behind one `INotificationChannel` contract.**
   `NotificationDispatcher` persists one `Notification` (the in-app record, with `ReadAt` for read
   state) and runs every channel whose `AppliesTo` is true, recording one `NotificationDelivery`
   row per channel (`Delivered` / `Failed` / `Skipped` + error). A throwing channel is caught and
   recorded as `Failed`; it never stops other channels or loses the in-app record. The in-app
   channel is deliberately thin - the persisted row is the delivery - and exists so every channel's
   outcome is recorded uniformly. SMS/push later means one new `INotificationChannel`.
3. **Email proven with a Mailpit container** (real SMTP, dev inbox). `IEmailSender` is the port;
   `SmtpEmailSender` uses the BCL `SmtpClient` (no new package; MailKit is the documented upgrade
   path). Email is real SMTP when `Email:Host` is configured; otherwise `DisabledEmailSender`
   makes the email channel record `Skipped` ("not configured") instead of failing. No mail leaves
   the machine in dev; a real relay is a Staging/Production decision deferred with hosting
   (ADR-0008).
4. **Trigger - a new `NotificationRequested` domain event over RabbitMQ.** Any module publishes it
   (payload: recipient, subject, body, type, optional email address); `NotificationRequestedHandler`
   (an `IEventHandler`, registered in `EventTypeRegistry`) turns it into a notification. A dev-only
   `POST /diagnostics/notifications/test-event` publishes one, following the Day 18/24 diagnostics
   pattern. Reacting to `DiagnosticsWidgetCreated` was rejected (couples notifications to a
   diagnostics event); hooking Day 36 config changes was rejected as overlapping Day 38's audit work.

Additional decisions:
- **Idempotency**: `Notification.SourceEventId` (the event's `EventId`) has a unique index and the
  dispatcher checks it first, so RabbitMQ's at-least-once redelivery cannot create a duplicate
  notification or send a second email.
- **Read API** (`src/Api/Notifications/`): `GET /api/notifications/mine` and `POST
  /api/notifications/{id}/read` for any authenticated user, scoped to the JWT's name claim (someone
  else's id returns 404, so ids cannot be probed); `GET /api/notifications` (all, with delivery
  outcomes) is `RequireAdmin`. There is intentionally **no create endpoint** - notifications are
  created by events only.
- Registration/route mapping/diagnostics endpoint all sit behind `ConnectionStrings:NotificationsDb`
  presence (map-time bindability lesson); `TestApiFactory` blanks it for infrastructure-free
  integration tests (ADR-0009).

## Verification

Live against the real docker-compose stack through the Gateway, unpiped exit codes:

- **Schema**: `psql \d` shows `Notifications` (unique index on `SourceEventId`, index on
  `Recipient`) and `NotificationDeliveries` (FK with `ON DELETE CASCADE`).
- **Event to inbox**: three real events published over RabbitMQ produced three notifications whose
  `SourceEventId` equals the published `eventId`; the two with an email address produced two
  messages in Mailpit's HTTP API (`noreply@customsclearance.local` -> the right recipients); the
  in-app-only event produced no email. Api log: `dispatched: InApp=Delivered, Email=Delivered`.
- **Auth/ownership**: broker sees only their own 2 notifications via `/mine`; broker gets 403 on
  the admin list; no token gets 401; another user marking a notification read gets 404; the owner
  gets 200 with `isRead: true`.
- **Failure isolation**: with the Mailpit container stopped, an event with an email address still
  produced a saved notification with `InApp=Delivered` and `Email=Failed ("Failure sending mail.")`.
- **Tests**: new `Platform.Notifications.Tests` (19 tests: dispatcher, channels, handler, DI,
  model) - 99.34% line / 100% branch vs the 80% gate (ADR-0007); `SmtpEmailSender` is
  `[ExcludeFromCodeCoverage]` with justification (network I/O, proven live above).
- **Regression**: full-solution build (only the documented NU1902), `dotnet format
  --verify-no-changes`, `dotnet test` (11 projects) all exit 0. `build/scripts/smoke-test.sh`
  extended with an event -> notification -> SMTP check (polls Mailpit); exit 0 from a fresh-volume
  cycle.

## Consequences

- Any module can notify a user by publishing `NotificationRequested`; nothing else in the codebase
  does so yet - real producers (e.g. Day 38 audit/config flow, Day 40 smoke test) come later.
- `Recipient` is a plain username matched against the JWT name claim; user preferences, templates,
  localisation, retries of failed deliveries and digesting are out of scope for a skeleton. A
  `Failed` delivery stays `Failed` (visible to admins) - no automatic retry yet.
- The email address travels in the event payload and is stored on the notification. It is personal
  data under DPDPA 2023 (ADR-0004): fine for dev, but real recipient addresses should be resolved
  from a user profile (a later Administration/PartyManagement concern) rather than carried by
  publishers.
- Dev now has one more container (`mailpit`, host ports 1025/8025) and the `api` service depends on
  it starting.

## Related

[[ADR-0003]] (placement), [[ADR-0007]] (80% gate), [[ADR-0009]] (infrastructure-free integration
tests), [[ADR-0012]] (event envelope), [[ADR-0018]] (the RabbitMQ bus this rides on), [[ADR-0024]]
(consumer tracing applies automatically), [[ADR-0030]] (same placement/wiring pattern), [[ADR-0004]]
(personal-data caveat).
