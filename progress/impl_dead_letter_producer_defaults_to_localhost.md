# impl: dead_letter_producer_defaults_to_localhost (backlog id 104)

## What was built

`DeadLetterKafkaOptions.BootstrapServers` in Orders, Projector and
Notifications used to default independently to `"localhost:9092"`, so any
caller (chiefly test hosts) that configured only `Kafka.BootstrapServers`
still dead-lettered to whatever broker happened to be listening on
`localhost:9092` — a developer's persistent Kafka, or nothing at all in CI.

The default is now `string.Empty` (meaning "not set explicitly"), and each
service's `Add*` registration resolves it to that service's own
`Kafka.BootstrapServers` when it is empty, immediately before
`Options.Create(options.DeadLetter)`:

```csharp
if (string.IsNullOrEmpty(options.DeadLetter.BootstrapServers))
{
    options.DeadLetter.BootstrapServers = options.Kafka.BootstrapServers;
}
```

Production behaviour is unchanged in effect: `OrdersProgramConfiguration.
ConfigureSaga`, `ProjectorProgramConfiguration.Configure` and
`NotificationsProgramConfiguration.Configure` all already set
`options.DeadLetter.BootstrapServers` explicitly to the same value as
`Kafka.BootstrapServers`, so the fallback never fires there — it only fires
for a caller (test or otherwise) that leaves `DeadLetter.BootstrapServers`
untouched.

## Files touched

Source (three services, options + registration):
- `src/Orders/Infrastructure/Messaging/DeadLetter/DeadLetterKafkaOptions.cs` — default `"localhost:9092"` → `string.Empty`, doc comment explains the fallback
- `src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs` — fallback resolved before `Options.Create(options.DeadLetter)`
- `src/Projector/Infrastructure/Messaging/DeadLetter/DeadLetterKafkaOptions.cs` — same change
- `src/Projector/Infrastructure/ProjectorServiceCollectionExtensions.cs` — same fallback
- `src/Notifications/Infrastructure/Messaging/DeadLetter/DeadLetterKafkaOptions.cs` — same change
- `src/Notifications/Infrastructure/NotificationsServiceCollectionExtensions.cs` — same fallback

New tests (one per service, acceptance bullet 2):
- `tests/Orders.UnitTests/DeadLetterBootstrapServersFallbackTests.cs`
- `tests/Projector.UnitTests/DeadLetterBootstrapServersFallbackTests.cs`
- `tests/Notifications.UnitTests/DeadLetterBootstrapServersFallbackTests.cs`

Each builds the REAL host via `*Host.CreateBuilder`, configures **only**
`Kafka.BootstrapServers` to a distinct literal (leaving `DeadLetter.
BootstrapServers` at its own default), calls `Build()`, resolves
`IOptions<DeadLetterKafkaOptions>` from the built `IServiceProvider`, and
asserts its `BootstrapServers` equals the Kafka value.

Comments updated (no behaviour change, stale "defaults to localhost:9092"
claims corrected to describe the fallback and why the explicit line is kept
— acceptance bullet 3):
- `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs`
- `tests/Gateway.IntegrationTests/SagaEndToEndVerificationTests.cs` (both `StartOrdersAsync` and `StartProjectorAsync`)

`feature_list.json` — `status: "pending"` → `"in_review"` for id 104 only (single-line edit, `git diff` shows exactly that one line).

## Acceptance bullet 3 — enumeration and disposition

Enumeration command (call sites, doc-comment `cref`s excluded):

```
grep -rn "OrdersHost\.CreateBuilder(\|ProjectorHost\.CreateBuilder(\|NotificationsHost\.CreateBuilder(" \
  --include="*.cs" tests | grep -v "/bin/\|/obj/" | grep -v "cref="
```

28 call sites, across 20 files. Cross-referenced against:

```
grep -rn "DeadLetter\.BootstrapServers" --include="*.cs" tests | grep -v "/bin/\|/obj/"
```

which found 11 call sites (9 files) that already set `DeadLetter.
BootstrapServers` explicitly as a workaround — all of them assign the exact
same value as the `Kafka.BootstrapServers` set a few lines above/below in
the same `configure` delegate:

- `tests/Orders.IntegrationTests/HealthProbesTests.cs:147`
- `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs:80`
- `tests/Notifications.IntegrationTests/NotificationDeadLetterTests.cs:79,244`
- `tests/Notifications.IntegrationTests/LogCorrelationTests.cs:66`
- `tests/Notifications.IntegrationTests/NotificationDegradesOnPermanentFailureTests.cs:62`
- `tests/Projector.IntegrationTests/LogCorrelationTests.cs:53`
- `tests/Projector.IntegrationTests/ProjectorDeadLetterTests.cs:43,164`
- `tests/Gateway.IntegrationTests/SagaEndToEndVerificationTests.cs:850,964`

These are now redundant with the fallback but harmless (the value they set
equals what the fallback would compute), so **left in place deliberately**
rather than removed — removing 11 lines across passing integration-test
files that exercise real Testcontainers Kafka is added churn with no
behavioural upside, and this is a LIGHT change. The three sites whose
comments made a now-stale claim about the default ("defaults to
localhost:9092") were updated to describe the fallback and why the explicit
line is kept (`SagaIntegrationTestSupport.cs`, and both call sites in
`SagaEndToEndVerificationTests.cs`); the other eight sites carry no such
comment and were left untouched.

The remaining 19 call sites across 19 unit/integration-test files (about
twenty, matching the disclosure's own estimate) never set `DeadLetter.
BootstrapServers` at all — this is the latent class the feature fixes. They
now resolve their dead-letter producer to whatever `Kafka.BootstrapServers`
was configured (a Testcontainers broker, or the plain `"127.0.0.1:1"`/
`"localhost:9092"` unit-test probes use), rather than an independent,
possibly-wrong `localhost:9092`. No further edit was needed at any of these
sites — the fix is in the three registration methods, not per call site:

`tests/Orders.UnitTests/OrdersDispatcherRegistrationTests.cs`,
`tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs`,
`tests/Orders.IntegrationTests/SagaConsumptionTests.cs` (both call sites),
`tests/Orders.IntegrationTests/SagaCommandRetryTests.cs`,
`tests/Notifications.UnitTests/NotificationsDispatcherRegistrationTests.cs`,
`tests/Notifications.UnitTests/NotificationSenderBindingTests.cs`,
`tests/Notifications.IntegrationTests/NotificationConsumptionTests.cs`,
`tests/Notifications.IntegrationTests/NotificationConsumptionTestSupport.cs`,
`tests/Notifications.IntegrationTests/HealthProbesTests.cs`,
`tests/Projector.UnitTests/ProjectorHostTests.cs`,
`tests/Projector.UnitTests/ProjectorDispatcherRegistrationTests.cs`,
`tests/Projector.IntegrationTests/OffsetContractTests.cs`,
`tests/Projector.IntegrationTests/ProjectorBootTests.cs`,
`tests/Projector.IntegrationTests/HealthProbesTests.cs`,
`tests/Projector.IntegrationTests/TestSupport/ProjectorTestHost.cs`,
`tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs` (both call sites),
`tests/Gateway.IntegrationTests/StreamProjectorEndToEndTests.cs`.

## #7 question (acceptance bullet 4)

**No — #7 has no separate dead-letter bootstrap at all**, so the class of
bug this feature fixes cannot occur there. `KafkaDlqPublisher`'s constructor
takes a `client: KafkaClientLike` (`../order-to-cash-nestjs/apps/orders/src/
infrastructure/messaging/kafka-dlq-publisher.ts:20-25`) rather than its own
bootstrap-servers config, and every composition root constructs it from the
SAME `loadKafkaConfig()` call the service's other Kafka client uses:
`new KafkaDlqPublisher(createKafkaClient(loadKafkaConfig()))` —
`apps/orders/src/app.module.ts:205`, `apps/projector/src/app.module.ts:104`,
`apps/notifications/src/app.module.ts:67`. One config source, one client
factory, no second variable to drift out of sync — the independent default
#8 carried (`DeadLetterKafkaOptions.BootstrapServers = "localhost:9092"`)
has no #7 analogue to port from; it was introduced by #8's own decision to
give the DLQ producer a dedicated `IOptions<DeadLetterKafkaOptions>`
(`design.md §3.3`, cited in the class's own doc comment) rather than reusing
the service's existing Kafka client instance.

## Arming (one arm per service, per the brief's LIGHT-change budget)

Protocol: `cp` backup → mutate (restore the independent `"localhost:9092"`
default) → `dotnet build --no-incremental` on the affected test project →
run the new test, record the FAIL verbatim → restore from backup → `cmp`
confirms byte-identical → `touch` the restored file → `dotnet build
--no-incremental` (confirmed via a full solution `dotnet build
--no-incremental`, once, after all three restores) → re-run, confirm green.

Never two builds/tests run concurrently — checked with
`pgrep -fl "dotnet (build|test|format)"` before each build/test invocation.

| Service | Test | Mutation | Failure (verbatim) |
|---|---|---|---|
| Orders | `DeadLetterBootstrapServersFallbackTests.RealHostComposition_ConfiguredWithOnlySagaKafkaBootstrapServers_ResolvesTheDeadLetterProducerToTheSameBroker` | `DeadLetterKafkaOptions.BootstrapServers` default restored to `"localhost:9092"` | `Assert.Equal() Failure: Strings differ` / `Expected: "saga-kafka-only.example:9092"` / `Actual: "localhost:9092"` |
| Projector | `DeadLetterBootstrapServersFallbackTests.RealHostComposition_ConfiguredWithOnlyKafkaBootstrapServers_ResolvesTheDeadLetterProducerToTheSameBroker` | same | `Assert.Equal() Failure: Strings differ` / `Expected: "projector-kafka-only.example:9092"` / `Actual: "localhost:9092"` |
| Notifications | `DeadLetterBootstrapServersFallbackTests.RealHostComposition_ConfiguredWithOnlyKafkaBootstrapServers_ResolvesTheDeadLetterProducerToTheSameBroker` | same | `Assert.Equal() Failure: Strings differ` / `Expected: "notifications-kafka-only.example:9092"` / `Actual: "localhost:9092"` |

Each restore confirmed with `cmp` against the backup (byte-identical),
`touch`ed, then re-proven green in the full post-restore verification below.

## Verification (final state, after all three restores and one full solution rebuild)

- `dotnet build --no-incremental` (solution-wide): **0 Warning(s), 0 Error(s)**.
- `dotnet test tests/Orders.UnitTests --no-build`: **503/503**.
- `dotnet test tests/Projector.UnitTests --no-build`: **121/121**.
- `dotnet test tests/Notifications.UnitTests --no-build`: **114/114**.
- `dotnet test tests/Architecture.Tests --no-build`: **50/50**.
- `dotnet test tests/Projector.IntegrationTests --no-build`, with nothing
  listening on `9092` on the host (`ss -ltn | grep 9092` empty; confirmed
  immediately before the run): **68/68**. This is the acceptance-relevant
  regression check — before this fix, a `Projector.IntegrationTests` run
  with no local Kafka is exactly the scenario the phase-16 wrap-up's
  disclosure describes (`ProjectorTestHost.StartAsync`'s own `configure`
  callback is one of the 19 latent call sites above), and it now resolves
  its dead-letter producer to the Testcontainers broker rather than a
  dead `localhost:9092`.
- `./quality.sh` was **not** run, per the brief.

## What was not done, and why

- `./quality.sh` was not run (out of scope per the brief).
- The 11 now-redundant explicit `DeadLetter.BootstrapServers = ...` test
  workarounds were left in place rather than removed (see "Acceptance
  bullet 3" above) — a deliberate choice recorded there, not an omission.
- No defeat-list walk was run (LIGHT change, per the brief).

## Nothing surprising

The three `*ProgramConfiguration.cs` files already set `DeadLetter.
BootstrapServers` explicitly, so production was never affected by the bug —
confirmed by reading all three before making any change, which is also why
"production configuration is unchanged in effect" needed no edit to those
files.
