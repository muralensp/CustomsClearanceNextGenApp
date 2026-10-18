# Day 37 Execution Log — Notification Base

Phase 0, Week 8 (Days 36-40), Day 37. Deliverable: "Sample notification fires on a test event."

---

## Step 1 — Looked at what existed, then settled the design with the user

Found reusable pieces (`IEventHandler<T>`, `EventTypeRegistry`, the RabbitMQ bus) and no
email/notification infrastructure. Four questions put to the user, all answered with the
recommended option: placement (`Platform.Notifications` in the Api host), channels (in-app in
Postgres + email behind one `INotificationChannel` contract), email dev proof (Mailpit container),
trigger (new `NotificationRequested` event over RabbitMQ). Recorded in
`adr/0031-notification-base.md`.

## Step 2 — Built the library, API and wiring

`src/Platform/Notifications/`: `Notification`/`NotificationDelivery` entities,
`NotificationsDbContext`, `INotificationChannel` + `InAppNotificationChannel` +
`EmailNotificationChannel`, `IEmailSender` + `SmtpEmailSender` + `DisabledEmailSender`,
`NotificationDispatcher`, `NotificationRequestedHandler`, `AddCustomsClearanceNotifications`.
`src/Api/Notifications/NotificationEndpoints.cs` (mine / mark-read / admin list). Wired into
`Program.cs` (registration, event type in the registry, startup table creation with retry, route
mapping, dev-only `POST /diagnostics/notifications/test-event` — all behind
`ConnectionStrings:NotificationsDb`), `Api.csproj`, `Dockerfile`, `docker-compose.yml` (new
`mailpit` service, Email__* and NotificationsDb env on `api`), `appsettings.Development.json`,
`TestApiFactory`, `CustomsClearance.sln`.

Small fix en route: the IDE flagged NU1605 package downgrades
(`Microsoft.Extensions.DependencyInjection.Abstractions` / `Logging.Abstractions` 8.0.1 vs the 8.0.2
that EF Core 8.0.10 pulls in) — bumped my explicit references to 8.0.2.

## Step 3 — Tests and static checks

`Platform.Notifications.Tests`: 19 tests, 99.34% line / 100% branch (80% gate), hand-written fakes
(no mocking library in this codebase). Full-solution build (0 errors, only the documented NU1902),
`dotnet format --verify-no-changes` exit 0, `dotnet test` exit 0 (11 projects).

## Step 4 — Environment notes

- Host port 5432 is still held by the unrelated `saferte_postgres` container (see Day 36 log);
  same temporary compose override (host port 55432, kept outside the repo, nothing stopped).
- First `docker compose up` hit the known RabbitMQ first-boot race (`dependency failed to start:
  rabbitmq is unhealthy`, ADR-0028); `restart: on-failure:5` self-healed and a plain second `up`
  succeeded with no manual intervention.
- Two shell-heredoc failures while patching files (the shell mis-parsed a quoted heredoc); worked
  around by writing the patch script with the file-write tool and running it. No effect on the repo.

## Step 5 — Live verification through the Gateway

- `psql \d`: `Notifications` (unique `IX_Notifications_SourceEventId`) and `NotificationDeliveries`
  (cascade FK) exist.
- Fired 3 real `NotificationRequested` events (2 with email addresses, 1 in-app only) over
  RabbitMQ: 3 notifications persisted, `SourceEventId` == each published `eventId`; Mailpit's API
  shows exactly the 2 expected emails from `noreply@customsclearance.local`; Api log shows
  `InApp=Delivered, Email=Delivered` / `InApp=Delivered`.
- Auth: broker `/mine` → own 2 only; broker on admin list → 403; no token → 401; admin marking
  broker's notification read → 404; owner → 200 `isRead:true`.
- Failure isolation: stopped Mailpit, fired an event with an email address → notification still
  saved, `Email=Failed ("Failure sending mail.")`, `InApp=Delivered`. Mailpit restarted.

## Step 6 — CI smoke script

Extended `build/scripts/smoke-test.sh` with an event → notification → SMTP check (fires an event,
polls Mailpit's HTTP API for the message). Ran it unpiped with the same temporary port override
from a fresh-volume cycle: exit 0, all three round trips (Postgres, RabbitMQ, notification) passed.

## Not done / deferred

- Retrying `Failed` deliveries, templates, user preferences, real recipient resolution from a user
  profile, a real SMTP relay (Staging/Production), SMS/push channels.
- No real event producer yet besides the dev-only test endpoint (Day 38/40 will add real ones).
- Not asserted live: redelivery idempotency (covered by a unit test, not reproduced against the
  real broker).
- Nothing committed or pushed (awaiting explicit instruction).
