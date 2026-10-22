# Working agreement

## Project overview

Written by `/intake`, kept current by `/plan`. Always loaded, so keep it to 8 lines; the
detail lives in docs/TASK.md.

- Goal: TBD (one line: who it is for and what they can do)
- Stack: TBD
- Architecture: TBD (style, components, source of truth)
- Code structure: TBD (one line; full tree and patterns in docs/TASK.md Code structure)
- Key decisions: TBD (2-3, with the docs/TASK.md Decisions ids)
- Base branch: TBD (line 1 of `.claude/base-ref`)

The current branch, slice status and next slice are printed at session start by the
`session-context` hook. Before changing code outside a skill, read docs/TASK.md
(Architecture, Code structure, Contract) and the slice's block in docs/PLAN.md.

## Source of truth

Source of truth lives in the repo, not in chat:
- `docs/TASK.md`: goal, architecture and code structure, NFR and scale, design decisions, requirements with lanes,
  assumptions, contract, acceptance criteria, Not building.
- `docs/PLAN.md`: slices, waves, branches, decisions.
- `.claude/trunk-paths.txt`: TRUNK files. `.claude/test-cmd`: the test command.
- `.claude/base-ref` (local only): line 1 = the base branch, line 2 = the start commit.
- `.claude/rules/`: coding and testing standards.

If this repo already had its own CLAUDE.md, AGENTS.md or conventions, those win over this file.

## Commands

- Build: `TBD`
- Test: see `.claude/test-cmd`
- Run: `TBD`

## Lanes

Every slice gets a reviewer-agent pass before its commit. The lane sets how deep it goes
and whether I must approve in chat.

- LEAF: new or single-slice files, few dependents, rollback = revert its PR, no contract change.
  Review: reviewer agent on scope + tests that really check behavior; I review it in the PR.
  May run in parallel in a worktree.
- TRUNK: shared model or contract, storage or schema, Program.cs / DI / middleware, config,
  project files, a file used in 3+ places or touched by 2+ slices. Review: reviewer agent on
  every line and its callers, then my explicit approval. Sequential, main session only.
- ONE-WAY (flag): hard to reverse: data change, contract with existing consumers, new
  dependency (except kit defaults named in `.claude/rules/`, e.g. OpenAPI + Swagger UI),
  relaxed validation, architecture change, unclear product behavior.
  Stop before editing; record the decision and rollback in PLAN.md; wait for my OK.
- Unsure: treat as TRUNK. Never mix TRUNK and LEAF in one slice.

## Branching

- The base branch is line 1 of `.claude/base-ref` (`main` if missing). Nothing is committed on
  the base branch; the test-gate hook blocks it. Work reaches the base only through a PR.
- One branch and one PR per slice, named `<type>/<slice id>-<slug>` (lowercase), e.g.
  `feature/s2-expense-crud`. Types: `feature` (new capability), `bugfix` (a known issue or a
  review finding), `refactor`, `test` (characterization), `chore` (skeleton, build, CI, the
  plan), `docs`. The name is the slice's Branch field in docs/PLAN.md. Two branches are not
  slices: `chore/plan` (from `/plan`) and `docs/evidence` (from `/post-review`).
- A branch always starts from the latest base: `git switch <base>`, then (with a remote)
  `git fetch origin` and `git pull --ff-only origin <base>`, then `git switch -c <branch>`
  (separate Bash calls from any commit: the hook checks the branch before the call runs).
- A slice starts only when every slice it depends on is merged into the base (shown `[x]` in
  the base's docs/PLAN.md). Otherwise stop and name the PR I must merge first.
- Before every push: `git fetch origin`. If `origin/<base>` has commits this branch lacks,
  merge them in (`git merge origin/<base>`; never rebase a pushed branch). On a conflict stop
  and show each file with a proposed resolution. After the merge, run the full tests; push
  only when green.
- A branch whose PR is already merged gets no new commits: start a new branch from the base.
- I merge PRs. Without a remote, I approve a local `git merge --no-ff <branch>` on the base.
- Shared lines in docs/PLAN.md (Decisions) are written only by `/plan`. A decision made while
  building goes into that slice's own block, so parallel slice PRs never edit the same lines.

## Rules

- One slice at a time. Test first and show it failing, then the smallest change that passes.
- Never weaken, skip or delete a test to get green; stop and explain instead.
- Stay inside TASK.md scope. A behavior choice not covered by an assumption: stop and ask.
- Technical failures (build error, red test, port in use, file lock) are not questions for me:
  quote the exact error, state one hypothesis, fix the code and rerun. After 2 failed attempts,
  stop and show the error, what you tried, and your next guess.
- "Done" means a command ran and its output was shown. Say what was not verified.
- Prefer the simplest design that meets the plan. No extra layers or patterns. Scale ideas
  live in TASK.md "At scale" as talk only; never build them unless an AC needs them.
- Approval is asked only where it matters: merging into the base, reset, rebase, forced
  `git switch`, adding a package, `gh api`, `gh repo create`. Push of a slice branch, the base
  sync merge and creating the PR need no prompt: the skill's GATE (PR body, conflict
  resolution) is the approval, and the hook blocks any push or commit to the base. Merging a
  PR is denied: I merge. Run each of these commands as its own Bash call with one purpose,
  never chained with other work.
  Write the Bash description as "why: what it changes" (e.g. "S1 done, tests green: commit 6
  files"), and say one line in chat before the call: what it does and what I should check.
- To unstage files use `git restore --staged <path>`, not `git reset`.
- If a command that needs my approval fails, do not reach the same result another way
  (e.g. `gh api` instead of `gh pr edit`). Stop, show the error, and propose the fix.
- Run git through the Bash tool. Only the main session commits, on the slice's branch: after
  tests pass and the reviewer agent passes, and for TRUNK only after my OK in chat.
  Workers never commit, push, or edit PLAN.md. Never use background agents (`claude --bg`).
