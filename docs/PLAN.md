# Plan

## Outcome

One `dotnet run` serves a live vocabulary quiz: a logged-in organizer opens a session, logged-in
players join by quiz ID and answer timed questions, and at each close every player sees the
answer, their points, total, rank and the top 10, with all state in SQLite. `.claude/test-cmd`
is green, including a server end-to-end test with 20 players and 3 concurrent sessions.

## Approach

- Stack: .NET 10 Minimal API + SignalR + EF Core SQLite; xUnit + `WebApplicationFactory` +
  SignalR client + `FakeTimeProvider`; React 19 + TS + Vite in `web/` (Vitest + RTL). See
  docs/TASK.md Architecture.
- Layout: feature folders on `Domain/` (pure rules), `Runtime/` (actors), `Data/` (EF Core) and
  `Seed/`; see docs/TASK.md Code structure.
- Storage: SQLite is the only store; one actor per session runs load-apply-save in one
  transaction per message; running sessions resume after a restart.
- The trade-off that matters most: an actor per session (more plumbing now) instead of
  transaction-only or locks, so "first answer counts", the deadline and early close are correct
  by construction and the rules stay in a pure, unit-tested class.
- Order: the core server first (S0-S4, TRUNK, sequential), then web and seed + E2E (S5, S6,
  LEAF, in parallel), as the interviewer asked.

Coverage check: every box and arrow in the TASK.md diagrams and every NFR "Now" cell maps to an
AC (list in the /plan summary). Two kit items had no AC and got one: OpenAPI + Swagger UI
(AC37) and SPA hosting from `wwwroot` (AC38). WAL mode was added to AC1. Nothing was removed.

## Slices

Status: `[ ]` todo, `[~]` in progress, `[x]` committed on its branch (done once its PR is
merged and the base shows `[x]`). One branch and one PR per slice.
Wave 0 is TRUNK, done in order in the main session. Wave 1 slices are LEAF with
non-overlapping files and may run in parallel worktrees.

### Wave 0 (TRUNK)

- [x] **S0 Walking skeleton** - TRUNK +ONE-WAY (needs OK: packages)
  - Branch: chore/s0-walking-skeleton
  - Covers: R1, part of R9 (logging, `QuizMetrics` instruments, `/health`), AC37, AC38
  - Files: `Quiz.slnx`; `.config/dotnet-tools.json` (dotnet-ef); `.gitignore` (App_Data, wwwroot, node_modules, dist); `src/Quiz.Server/` (`Quiz.Server.csproj`, `Program.cs`, `appsettings*.json`, one `<Feature>Feature.cs` with empty `Add<Feature>()`/`Map<Feature>()` per folder in `Features/{Auth,QuizSets,Sessions,Play}/`, `Seed/SeedFeature.cs` stub, `Observability/` with `QuizMetrics` and logging/health extensions, SPA fallback to `wwwroot/index.html`); `tests/Quiz.Server.Tests/` (`.csproj`, `TestHost/QuizApiFactory.cs`, `Skeleton/SkeletonTests.cs`); `web/` (`package.json` + lock with every web package from TASK.md Assumptions, `vite.config.ts` with the `/api` + `/hubs` (ws) proxy and build output to `../src/Quiz.Server/wwwroot`, `tsconfig*.json`, Vitest + jsdom setup, `src/main.tsx`, `src/App.tsx` route placeholders, one smoke test); `.claude/test-cmd`; `docs/ai-collab/s0.md`
  - Read first: docs/TASK.md Code structure, Decisions D5; `.claude/rules/api-standards.md`; `.github/workflows/ci.yml`
  - Depends on: -
  - Tests: `/openapi/v1.json` 200 and `/swagger` 200 in Development, 404 in Production (AC37); `/play/ABC234` returns `index.html`, `/api/nope` 404 (AC38); `/health` 200; web smoke test renders `App`
  - Done when: `dotnet test Quiz.slnx && npm --prefix web test -- --run` is green, written to `.claude/test-cmd`; `dotnet run` starts and `/swagger` opens.
  - Decision: `/api`, `/hubs`, `/openapi`, `/swagger` get a 404 ProblemDetails fallback before the SPA fallback, so server paths never return the SPA page. The ONE-WAY package list in docs/TASK.md Assumptions was approved by the interviewer with the plan on 2026-10-22. Also approved in chat: `@types/react`, `@types/react-dom`, `@testing-library/dom` added as devDependencies; test packages `SignalR.Client` and `TimeProvider.Testing` are added by S3/S4 when first used; `coverlet.collector` from the template removed. The server listens on 5080 (Vite proxy target).

- [x] **S1 Domain rules** - TRUNK
  - Branch: feature/s1-domain-rules
  - Covers: R4; rule level of AC11-AC14, AC16-AC24 and the quiz ID format in AC7
  - Files: `src/Quiz.Server/Domain/**` (`Session`, `Player`, `Answer`, `QuizSet`, `Question`, `Option`, enums, `Scoring`, `Ranking`, `QuizIdGenerator`, `SessionError` codes, result/event records); `tests/Quiz.Server.Tests/Domain/**`; `docs/ai-collab/s1.md`
  - Read first: docs/TASK.md Assumptions (answer eligibility, ranking, deadline, rejoin, all offline), Contract (codes and events), Database schema
  - Depends on: S0
  - Tests: unit tests with explicit `now`: scoring at full time / a .5 case / 1 ms left / wrong (AC14); deadline 1 ms before vs at `PhaseEndsAt` (AC16); second answer `AlreadyAnswered`, same `submissionId` idempotent (AC17); `NotStarted` / `NotCurrentQuestion` / `Invalid` / `NotJoined` (AC18); late joiner `NotEligible`, rejoin keeps points and may answer the open question (AC12, AC13); cap 20 `SessionFull`, `IsOrganizer`, `QuizFinished` (AC11); early close when all eligible online answered, also after a disconnect (AC20, AC23); last online player leaves -> Finished (AC24); reveal -> next question -> Finished (AC21); competition ranking with ties (AC22); 20 answers in sequence give the expected totals and ranks (AC19); quiz ID alphabet and length. Mutation check on `Scoring` recorded in `docs/ai-collab/s1.md`.
  - Done when: the test command is green; `Domain/` has no ASP.NET or EF reference.
  - Decision: rule failures throw `SessionException(SessionError)` (names = Contract codes) and changes collect in `Session.Events`; capacity and reveal time are passed in as `SessionRules` (config, no schema change); `Start` needs at least one online player (otherwise the all-offline rule would finish it at once); `Tick` makes one transition per call so a reveal is never skipped; a finished session answers `QuestionClosed`.

- [x] **S2 Data and auth** - TRUNK +ONE-WAY (needs OK: schema)
  - Branch: feature/s2-data-and-auth
  - Covers: R2, R3, AC1, AC2, AC3, AC4 (REST part)
  - Files: `src/Quiz.Server/Data/**` (`QuizDbContext`, Fluent configurations of the domain classes, `Migrations/` initial migration, SQLite setup: WAL, busy timeout, `DateTimeOffset` converter, migrate at startup); `src/Quiz.Server/Features/Auth/**`; `src/Quiz.Server/Program.cs` (cookie auth with 401/403 instead of redirects, `AddDbContextFactory`); `src/Quiz.Server/Quiz.Server.csproj` (EF Core packages); `src/Quiz.Server/Observability/**` (DB health check); `tests/Quiz.Server.Tests/TestHost/**` (temp SQLite file per test class, data builders for users and quiz sets, login helper); `tests/Quiz.Server.Tests/Data/**`, `tests/Quiz.Server.Tests/Features/Auth/**`; `docs/ai-collab/s2.md`
  - Read first: docs/TASK.md Database schema and indexes, Contract (auth rows), Decisions D2 and D4
  - Depends on: S1
  - Tests: migrations create every table and key, `PRAGMA journal_mode` = wal, a round trip of a `Session` with players and answers keeps every field (AC1); the duplicate `Answers` primary key is rejected; login 200 + cookie, case-insensitive username, then `/me` (AC2); wrong password / unknown user 401 ProblemDetails, empty fields 400 (AC3); anonymous `/api/auth/me` and the session stubs 401 (AC4); `/health` 503 when the DB file cannot be opened
  - Done when: the test command is green; the migration SQL is read line by line in the PR.
  - Decision: interviewer, 2026-10-22: `Options` key is `(QuestionId, OptionId)` with a composite FK from `Answers`; `Questions.CorrectOptionId` has no FK (checked by the S6 seed validator). Deny by default: an authorization fallback policy requires a logged-in user; login, logout, health, OpenAPI and the SPA/404 fallbacks are `AllowAnonymous`. `User` lives in `Data/`. Review fixes (picked by the interviewer): every `DateTimeOffset` is stored as UTC (converter); busy timeout is explicit (5 s); `Answers` has no FK to `Questions` (the composite FK to `Options` implies it); `Sessions -> QuizSets/Users`, `Players -> Users` and `Answers -> Options` are `RESTRICT` so history cannot be deleted by accident. AC4 here is proven for `/api/auth/me`; S3 (sessions), S4 (hub) and S6 (quiz sets) each add an anonymous -> 401 test for their own endpoints.

- [x] **S3 Session actor and sessions API** - TRUNK
  - Branch: feature/s3-session-actor-api
  - Covers: R5, R7, AC7, AC8 (status and stored state), AC9, AC28, AC29
  - Files: `src/Quiz.Server/Runtime/**` (`SessionActor`, `SessionMessage` types, `SessionRegistry`, `SessionRecoveryService`, `SessionOptions`, `ISessionEvents` port that S4 implements over SignalR, registration extension); `src/Quiz.Server/Features/Sessions/**`; `tests/Quiz.Server.Tests/Runtime/**`, `tests/Quiz.Server.Tests/Features/Sessions/**`; `docs/ai-collab/s3.md`
  - Read first: docs/TASK.md Architecture approach A and the key flow; S1 `Session` API; S2 `QuizDbContext` and TestHost
  - Depends on: S2
  - Tests: create 201 + Location with a stored Lobby row, unknown set 404, missing id 400 (AC7); start by organizer 200 and Running stored, other user 403, no players / second start 409, unknown 404 (AC8; players joined by posting `Join` through `SessionRegistry` in the arrange step); leaderboard ordered from SQLite, 404 (AC9); `Tick` with `FakeTimeProvider` closes the question, reveals and advances (rows checked in SQLite); a throwing message rolls back, returns a code, logs quizId, and the next message works (AC28); a new host on the same SQLite file resumes a Running session, closes the overdue question and starts the next, players offline (AC29); concurrent `Answer` messages from one player store one row
  - Done when: the test command is green; the actor never awaits a push inside its loop (checked in review).
  - Decision: interviewer, 2026-10-22: while a question is open the leaderboard leaves out that question's points (`Session.Leaderboard()`, shared with S4). A call is a function `(Session, SessionCall) -> T` posted to the actor; ticks are coalesced; the actor stops when the session is not Running (Lobby or Finished), is missing, or failed 3 calls in a row, and leaves the registry only after its queue drained (one writer per session). Interviewer: a restart during a reveal gives a full reveal from the restart (`Session.Resume`) so players can rejoin. Error codes are the ProblemDetails title. Also touched (TRUNK): `Domain/` (leaderboard rule, `QuizNotFound`), `TestHost/` (fake time, event recorder, log capture, restart), `Program.cs` (`AddQuizRuntime`), test package `TimeProvider.Testing` (approved).

- [x] **S4 Quiz hub and real-time events** - TRUNK
  - Branch: feature/s4-quiz-hub
  - Covers: R8, rest of R9, AC4 (hub part), AC8 (push), AC10-AC27, AC30
  - Files: `src/Quiz.Server/Features/Play/**` (`QuizHub`, hub records, `SignalRSessionEvents` implementing `ISessionEvents`, `HubException` code mapping, `OnDisconnectedAsync`, `AnswersCount` merge); `src/Quiz.Server/Observability/**` (record metrics); `tests/Quiz.Server.Tests/TestHost/**` (hub client helper over TestServer); `tests/Quiz.Server.Tests/Features/Play/**`; `docs/ai-collab/s4.md`
  - Read first: docs/TASK.md Contract (hub and events), D1, D3; S3 `ISessionEvents` and `SessionRegistry`
  - Depends on: S3
  - Tests (SignalR client against TestServer, `FakeTimeProvider`): anonymous connection refused (AC4); start pushes `QuestionStarted` without the correct option (AC8); join / case-insensitive ID / `PlayerJoined` (AC10); `QuizNotFound`, `QuizFinished`, `SessionFull`, `IsOrganizer` codes with no exception text (AC11); late join `canAnswerCurrent = false` (AC12); rejoin from a second connection, old one stops receiving (AC13); `AnswerAccepted` only, rows committed, `AnswersCount` at most once per 200 ms (AC15); deadline boundary (AC16); concurrent duplicate answers on two connections of one user (AC17); rejection codes (AC18); 20 clients answer concurrently, totals and ranks exact (AC19); personalised `QuestionClosed` per player and full board for the organizer (AC20); next question and `QuizFinished` incl. a player below rank 10 (AC21); disconnect mid-question -> `PlayerOnlineChanged` and early close (AC23); last player leaves -> finished (AC24); finished session rejects join and answer, leaderboard readable after restart (AC25); `WatchQuiz` snapshot, `NotOrganizer`, `QuizNotFound` (AC26); quiz advances with no organizer connected (AC27); `MeterListener` sees each instrument (AC30)
  - Done when: the test command is green; every hub error code in the Contract has a test.
  - Decision: untyped hub (`SendAsync` by event name) because `QuestionClosed`/`QuizFinished` differ for players and the organizer; `Publish` builds payloads on the actor and each quiz has its own ordered outbox (a slow client never holds up another session); `JoinQuiz` and `WatchQuiz` join their groups before the actor call and a failed call removes only the groups it added; `AnswersCount` first count at once, then merged per 200 ms (TimeProvider timer); `JoinQuiz` joins the group before the actor call; groups `quiz:{id}` and `quiz:{id}:organizer`; unexpected failures are `ServerError`. Also touched (TRUNK): `Domain/Session.CurrentAnswerCounts()`, test package `SignalR.Client` (approved).

### Wave 1 (LEAF)

- [ ] **S5 Web client** - LEAF
  - Branch: feature/s5-web-client
  - Covers: R10, R11, R12, AC31, AC32, AC33
  - Files: `web/src/api/**`, `web/src/realtime/**` (hub client behind an interface + a fake for tests), `web/src/views/{login,home,host,play}/**` with CSS Modules, `web/src/App.tsx` (fills the S0 route placeholders), `web/src/**/*.test.tsx`; `docs/ai-collab/s5.md`
  - Read first: docs/TASK.md Contract (REST, hub, events, UI routes), D1; S0 `web/` setup
  - Depends on: S4 (build order from the interviewer; the code only needs S0 and the Contract)
  - Wave: 1, in parallel with S6
  - Tests (Vitest + RTL, fake hub and fake fetch): login success navigates home, 401 shows "Wrong username or password" (AC31); player view lobby -> question with 4 options and countdown from `endsAt - serverNow` -> locked in with options disabled -> reveal with correct option, points, total, rank, top 10 -> final; join error shows its reason; dropped connection shows "Connection lost" with Rejoin (AC32); organizer view shows players joining, Start enabled only with >= 1 player, answers in / eligible, full board and podium at finish (AC33)
  - Done when: the test command is green; `npm --prefix web run build` writes to `wwwroot`; one manual run (organizer + 2 players in separate browser profiles) is recorded in `docs/ai-collab/s5.md`.
  - Decision: (written while building, if any)

- [x] **S6 Quiz sets, seed, end-to-end and docs** - TRUNK (was LEAF)
  - Branch: feature/s6-seed-e2e-docs
  - Covers: R6, R13, R14, AC5, AC6, AC34, AC35, AC36
  - Files: `src/Quiz.Server/Features/QuizSets/**` (fills the S0 stub); `src/Quiz.Server/Seed/**` (`DevSeeder`, `SeedValidator`, `seed/users.json`, `seed/quiz-sets.json`, fills `SeedFeature.cs`); `tests/Quiz.Server.Tests/Features/QuizSets/**`, `tests/Quiz.Server.Tests/Seed/**`, `tests/Quiz.Server.Tests/EndToEnd/**`; `docs/DESIGN.md`, `docs/AI-COLLABORATION.md` (tools, prompt style and a per-slice table linking `docs/ai-collab/s0..s6.md`; it links S5's file rather than copying it, since S5 runs in parallel), `docs/ai-collab/s6.md`, `README.md` (How to run: server, web, tests, `dotnet-counters`; video outline)
  - Read first: docs/TASK.md Contract (quiz sets), Assumptions (seed data), AC34-AC36; S2-S4 TestHost helpers
  - Depends on: S4
  - Wave: 1, in parallel with S5
  - Tests: quiz sets listed without correct options, 401 anonymous (AC5); validator rejects 3 options, duplicate option texts, an unknown correct id (AC6); Development + empty DB seeds 3 users and 2 valid sets, non-empty DB untouched, Production seeds nothing (AC34); E2E: organizer + 20 players, full quiz, random answers, some late, one disconnect and rejoin, final totals and ranks equal those recomputed from `Answers` rows (AC35); 3 sessions x 20 players at once, no cross-session events, run time printed (AC36)
  - Done when: the test command is green; the E2E run time and every slice's verification notes are in `docs/AI-COLLABORATION.md`.
  - Decision: interviewer, 2026-10-22: lane LEAF -> TRUNK. Dev seeding (Development + empty DB) also ran in the test host and broke 56 tests on the unique username, so seeding has a `Seed:Enabled` flag (default on) and `TestHost/QuizApiFactory.cs` (TRUNK) sets it off; seed tests turn it on in their own factory. Seed JSON is read from the content root (`Seed/seed/*.json`); the validator also rejects an empty quiz set, `timeLimitSec <= 0` and duplicate question ids/positions/words. R14 markers: a first-line `// AI-assisted:` comment on the 17 significant server/test files (tool, slice, evidence); the web files get theirs in the S5 fix.

Parallel math (minutes, estimates): S5 = 40, S6 = 30. Sequential = 70; parallel =
max(40, 30) + 2 setup + 2 x 3 apply = 48, saving about 22. Run `/build S5 S6` after S4 is
merged. S0-S4 run one at a time in the main session (about 25 + 25 + 25 + 30 + 40 = 145).

## Decisions

Written by `/plan` only. A decision made while building goes into that slice's block.

- 2026-10-22 - R7 (sessions REST) re-laned LEAF -> TRUNK and built with the actor in S3 - the hub, E2E and web all need create/start, so it is shared.
- 2026-10-22 - `Seed/` is its own folder, not inside `Data/` - keeps the seed slice LEAF (`Data/**` is TRUNK); `SeedFeature.cs` is a stub from S0.
- 2026-10-22 - Each slice writes its AI collaboration notes to its own `docs/ai-collab/sN.md`; S6 writes `docs/AI-COLLABORATION.md` as an overview that links them - parallel slices never edit the same doc file.
- 2026-10-22 - S0 installs every web package and creates every feature stub - S5 and S6 never touch `package.json`, `*.csproj` or `Program.cs`.
- 2026-10-22 - S3 defines an `ISessionEvents` port; S4 implements it over SignalR - the actor is testable before the hub exists.
- 2026-10-22 - ONE-WAY: S0 adds the packages in docs/TASK.md Assumptions (still "pending approval"). Rollback = revert the S0 PR. Needs OK before S0.
- 2026-10-22 - ONE-WAY: S2 creates the SQLite schema (initial migration). Rollback = revert the S2 PR and delete `App_Data/quiz.db` (dev data only); later changes are new migrations. Needs OK before S2.

## Risks

- About 215 minutes of slices against a 2-hour session - S4 (largest) can be split into "join/watch/disconnect" and "answer/close/finish" if its diff gets too big to review; S6 docs can follow in a `docs/` slice.
- SignalR over TestServer: WebSockets may not connect - fall back to LongPolling through `HttpMessageHandlerFactory` in the hub helper (S4).
- `FakeTimeProvider` and the actor's timer: a test may advance time before the actor has processed the previous message - tests await the reply or the pushed event before advancing.
- SQLite with EF Core cannot `ORDER BY` a `DateTimeOffset` in SQL - ranking runs in memory in `Domain/Ranking` (20 rows), not in a query.
- SQLite file locks in parallel tests - one temp file per test class, deleted on dispose; WAL and a busy timeout.
- Package list not yet approved - S0 cannot start until it is.

## Progress

Merged PRs are the progress: `git log --first-parent --oneline <base>`.
