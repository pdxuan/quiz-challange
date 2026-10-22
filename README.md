# Live vocabulary quiz

A Kahoot-style live quiz for vocabulary practice: an organizer opens a session, players join with
the quiz ID, everyone answers the same timed question, and results and the leaderboard appear
when each question closes. Real-time .NET 10 server (Minimal API + SignalR + EF Core SQLite) and
a small React client.

- Design: [docs/DESIGN.md](docs/DESIGN.md)
- Spec and acceptance criteria: [docs/TASK.md](docs/TASK.md); plan: [docs/PLAN.md](docs/PLAN.md)
- How the AI was used: [docs/AI-COLLABORATION.md](docs/AI-COLLABORATION.md)
- How the agent works in this repo: `CLAUDE.md` and `.claude/`

## How to run

Prerequisites: .NET SDK 10, Node 24 (npm 11).

### Server

```
dotnet run --project src/Quiz.Server --launch-profile http
```

Listens on http://localhost:5080 (Development). On first start it creates
`src/Quiz.Server/App_Data/quiz.db`, applies the migrations and, because the database is empty,
seeds 2 quiz sets ("Everyday words", "Travel words") and 3 dev users:

| Username | Password (dev mock data only) |
|----------|-------------------------------|
| alice | alice-dev-pass |
| bob | bob-dev-pass |
| carol | carol-dev-pass |

Swagger UI: http://localhost:5080/swagger. Health: http://localhost:5080/health.
To start from scratch, stop the server and delete `src/Quiz.Server/App_Data/quiz.db`.

### Web client

Either run the Vite dev server (proxies `/api` and `/hubs` to the server on port 5080):

```
npm --prefix web install
npm --prefix web run dev
```

or build it into `src/Quiz.Server/wwwroot` so one `dotnet run` serves everything on port 5080:

```
npm --prefix web install
npm --prefix web run build
dotnet run --project src/Quiz.Server --launch-profile http
```

Try it: log in as `alice` in one browser and create a session from a quiz set; log in as `bob`
and `carol` in two other browsers (or private windows), join with the quiz ID, then start.

### Tests

The test command (also in `.claude/test-cmd`):

```
dotnet test Quiz.slnx && npm --prefix web test -- --run
```

Server tests run the real server in memory, each on its own SQLite file with a fake clock,
including the end-to-end tests in `tests/Quiz.Server.Tests/EndToEnd` (20 players in one quiz;
3 sessions of 20 players at once). Their run times are printed with
`dotnet test Quiz.slnx --filter FullyQualifiedName~EndToEnd --logger "console;verbosity=detailed"`.

### Metrics

```
dotnet tool install --global dotnet-counters
dotnet-counters monitor -n Quiz.Server --counters Quiz.Server
```

Shows live sessions, hub connections, answers accepted and rejected (by error code), actor
message latency (including the database) and question close-to-push latency.

## Video outline (5-10 min)

1. **The problem (1 min).** A live quiz where many players answer the same timed question;
   the hard part is one session's state changing from answers, the organizer and the clock at once.
2. **Demo (2-3 min).** Start the server; log in as alice (organizer), create a session, bob and
   carol join by quiz ID; start; answer, one late; the reveal shows correct option, points,
   total, rank and top 10; disconnect and rejoin a player; the final board. Show
   `dotnet-counters` alongside.
3. **Architecture (2 min).** DESIGN.md diagrams: one process, SQLite as the only state, one
   actor per session (load-apply-save per message, push after commit); the three options
   considered and why the actor won; the join -> answer -> leaderboard sequence.
4. **Correctness (1-2 min).** Pure `Session` state machine with explicit `now`; server-stamped
   receive time; results only at close; restart resume. The end-to-end tests: 20 players with
   late answers and a rejoin, totals and ranks recomputed from the `Answers` rows; 3 sessions at
   once with no cross-session events; run times.
5. **AI collaboration (1-2 min).** Intake -> plan -> slices with tests first, reviewer agent,
   human gate for TRUNK, parallel worktrees for LEAF slices, mutations; two examples of what the
   AI got wrong and how it was caught (AI-COLLABORATION.md).
6. **At scale (30 s).** What changes first: PostgreSQL, broadcast top 10 plus a small personal
   result, a SignalR backplane, routing each quiz to one node.
