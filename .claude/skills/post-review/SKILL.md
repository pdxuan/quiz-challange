---
name: post-review
description: Closing step, after the slice PRs are merged. (a) Whole-session review on the merged base by the fresh-context reviewer agent (across slices, architecture, unproven criteria); I pick the fixes, each becomes /build fix + /pr. (b) Once clean, final evidence: run the app end to end, map every acceptance criterion to its proof, open the docs/evidence PR.
argument-hint: [empty = review then evidence; "evidence" = skip the review]
disable-model-invocation: true
---

Mode: $ARGUMENTS. Base branch: line 1 of `.claude/base-ref`, else `main`. Start commit:
line 2 of `.claude/base-ref`, else the first commit (`git rev-list --max-parents=0 HEAD`).

Each slice was already reviewed by the reviewer agent in `/build` and proved in its PR. This
step looks at what no single slice can show: the session as a whole.

## 0. Sync

`git status --short` must be clean. `git switch <base>`; with a remote `git fetch origin`
and `git pull --ff-only origin <base>`. List the slices not merged yet (not `[x]` in the
base's PLAN.md; this also works when PRs were squash-merged): their work is not in this
review or this evidence. If any are still open, ask me whether to wait.

## A. Whole-session review (skip when the mode is "evidence")

1. Spawn the `reviewer` subagent. Give it only:
   - the start commit, to review `git diff <start>...HEAD` on the base,
   - the paths `docs/TASK.md`, `docs/PLAN.md`, `.claude/trunk-paths.txt`.

   No summary, rationale or claims about tests of yours. It judges from the diff, the spec
   and its own test run.
2. Present its findings as a table ordered by severity (Must / Should / Nice) with lane,
   file:line and a one-line fix, then the acceptance criteria marked NOT PROVEN.

   **GATE:** I pick the findings to fix. Do not change any code here.
3. Picked findings: list one `/build fix <finding>` per finding (each becomes a `bugfix/`
   slice with its own PR) and stop. After they are merged I run `/post-review evidence`.
   Nothing picked: say why each Must is left (my reason) and go on to B.

## B. Final evidence

1. `git switch -c docs/evidence` (or `docs/evidence-2`… if it exists), as its own call.
2. Run the full test command and show the counts.
3. Start the app in the background, wait until it responds, and walk the main flow end to
   end with real requests (a CLI or library: real inputs): the happy path through every
   feature, one error path from the Contract, and (with storage) a read that proves an
   earlier write was kept. Show status and body. For an HTTP API also fetch
   `/openapi/v1.json` and check that every Contract endpoint is in it with its status
   codes. Then stop the app: on Windows a running app locks `bin/` and breaks the next
   build. Stop it with PowerShell (`Stop-Process -Name <AppName>`; `taskkill` flags get
   mangled by Git Bash) and confirm the port now refuses connections.
4. Put the exact commands you ran, and the Swagger UI URL for an API, under "How to run" in
   `README.md` (existing repo: only if its README has no such section). Update docs/TASK.md:
   - Status table: every acceptance criterion -> the slice PR, test name or request that
     proves it -> result. Tick it only if its evidence passed.
   - Not verified, and Next with more time (only what applies).
   - Existing code: compare with Current state: no new red tests, and
     `git diff --stat <start commit>` stays within scope.
5. Stage by name, as its own call: `git add README.md docs/TASK.md` (never `git add -A`; list
   any other change instead). Then, as a separate call, commit with
   `docs: evidence and how to run` and show `git status`.

Next: `/pr` for docs/evidence.
