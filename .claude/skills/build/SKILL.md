---
name: build
description: Implement slices from docs/PLAN.md test-first, each on its own branch from the latest base. One slice in the main session, or two LEAF slices in parallel git worktrees via slice-worker subagents. Every slice gets an independent reviewer-agent pass before its commit; TRUNK also needs my explicit approval. Then /pr.
argument-hint: [slice ids, e.g. "S2 S3"; empty = next slice]
disable-model-invocation: true
---

Slices: $ARGUMENTS

Follow `CLAUDE.md` (Lanes, Branching) and `.claude/rules/`. Test command: `.claude/test-cmd`.
Base: line 1 of `.claude/base-ref`, else `main`.

## Pick

- First run `git status --short` and `git branch --show-current`. On a slice branch with
  uncommitted changes: that slice was not committed. Show the files, set it back to `[~]`
  and resume it on that branch. On the base with uncommitted changes: stop and show them
  (they are not a slice's; I stash or move them). Never start a new slice on a dirty tree.
- A worktree left from an earlier parallel run (`git worktree list` shows
  `.claude/worktrees/`): finish that slice first (Parallel step 3 for it), or ask me.
- **Sync the base.** `git switch <base>`; with a remote (`git remote get-url origin`):
  `git fetch origin` and `git pull --ff-only origin <base>`. If the pull is not a
  fast-forward, stop and show why (someone committed on the local base).
- Branch switches and commits are separate Bash calls: the test-gate hook reads the branch
  before the call runs, so `git switch -c … && git commit …` is blocked as a commit on the base.
- Open slice branches: `git branch --list '*/s*'` (and `gh pr list --state open` with a
  remote). A slice that already has a branch is in review, not todo.
- Empty: take the first `[ ]` slice in the base's PLAN.md whose Depends on are all `[x]`
  there (that is, merged) and that has no branch yet. A dependency not merged: stop and name
  the PR I must merge first.
- `on <branch> <what to fix>` (red CI, red tests after a base sync, or PR review comments on
  a branch whose PR is still open): no base sync. `git switch <branch>` (clean tree), then
  Sequential steps 2-9 on that branch with the same lane; commit message
  `<id>: fix <what>`. The open PR picks it up on the next `/pr <branch>`.
- `fix <finding>` (code already merged into the base, e.g. from `/post-review`): a new slice
  with the next id used neither in the base's PLAN.md nor by an open branch
  (`git branch --list '*/s*'`, `gh pr list --state open`), lane by blast radius, Branch
  `bugfix/<id>-<slug>`. Its block (`- [ ] **S<n> <title>** - <lane>` with the usual fields)
  goes under `### Fixes` at the end of Slices, after a blank line, on its own branch in the
  same commit as the fix. TRUNK or ONE-WAY: show the block and wait for my OK.
- One slice, or any TRUNK slice: one at a time.
- Two slices: check each condition and print it with OK / FAIL:
  1. both LEAF, same wave, Depends on all merged (`[x]` on the base),
  2. Files disjoint, and none matches `.claude/trunk-paths.txt`,
  3. on the base, synced, `git status --short` clean (worktrees start from this commit),
  4. the parallel math in PLAN.md says it saves time.
  Any FAIL: say why and do them sequentially. All OK: go to Parallel.

## Sequential (main session)

1. `git switch -c <Branch field of the slice>` from the synced base. Mark the slice `[~]`.
   Read the slice's block in PLAN.md, its Read first files, and docs/TASK.md Architecture,
   Code structure and the Contract rows it covers. Restate it and its Done when in 2 lines. ONE-WAY without an approved Decision: stop and ask.
2. Write the tests first, run them, show them failing for the expected reason.
3. Make the smallest change that passes. Deviation from the plan: stop and ask.
4. Run the full test command and show the result.
5. Self-review the diff: dead code, debug output, unused usings, only this slice's files.
6. **Agent review (every slice, every lane).** `git add -A`, then spawn the `reviewer`
   subagent with only: "slice <id>, lane <LEAF|TRUNK>, review the staged diff
   (`git diff --cached`)" and the paths docs/TASK.md, docs/PLAN.md, .claude/trunk-paths.txt.
   No summary or rationale of yours. Stop rule:
   - Must: fix, rerun the tests, review again. Re-review only after fixing a Must; at most
     2 rounds, then stop and show me what is left.
   - Should / Nice: never fix on your own. List them in the report; I pick. The ones I do
     not pick go into this slice's PR body (Not verified / risks), not into PLAN.md.
   - A review round with no Must ends the review: go on to the report.
7. Report: branch, `git diff --cached --stat`; reviewer verdict and any open Should;
   **Read first** (file:line where this touches TRUNK or existing code); one decision (also
   in this slice's Decision line in PLAN.md); one risk or edge case not covered; anything
   not verified.
8. **Human gate by lane.**
   - TRUNK: stop here. I read the diff line by line and approve in chat before you commit.
   - LEAF: go on. Agent review PASS is enough to commit; I review LEAF work in the PR.
9. Mark the slice `[x]` (only its own line in PLAN.md), `git add -A`, then commit with
   `-m "<id>: <title>" -m "Review: agent <PASS|n Should open>; lane <lane>; human <approved|PR>"`.
   The test-gate hook runs the tests first (and blocks a commit on the base branch). If it
   blocks, set the slice back to `[~]`, fix the code (never the test), then stop and wait.
   In a parallel run, also report the other slice's worktree path: its patch is not applied
   yet and the next `/build` finishes it.

Then stop and say: "Next: `/pr` for <branch>." Do not start the next slice.

## Parallel (two LEAF slices)

1. On the synced, clean base: record `base = git rev-parse HEAD`.
2. Dispatch two `slice-worker` subagents **in one message** so they run concurrently.
   Each prompt contains: the slice text from PLAN.md, its Files list, the base commit,
   the test command, and "never commit; edit only these files".
3. When both report, handle them one at a time, in plan order:
   a. `(cd "<wt>" && git add -A && git diff --cached --stat)`. Any file outside
      the slice's Files list: stop, show it, ask me.
   b. `git switch -c <Branch field> <base>`, then
      `(cd "<wt>" && git diff --cached --binary) | git apply --index`. If it does not apply,
      the lanes were wrong (the slices were not independent): say so, `git switch <base>`,
      delete the empty branch, and run that slice sequentially later. Do not force it.
   c. Mark the slice `[~]`. Run the full test command. Red: stop and show me.
   d. Sequential steps 6-9 for this slice on its branch: agent review of the staged diff,
      report, commit with the Review trailer (LEAF: no human gate here; I review in the PR).
   e. `git switch <base>` before the next slice, so each branch starts from the base.
4. Clean up each worktree: note its branch (`(cd "<wt>" && git branch --show-current)`), then
   `git worktree remove --force "<wt>"` and `git branch -d <worker branch>` (the worker's
   own branch; it has no commits, so `-d` is enough; never a slice branch). Show `git worktree list` and
   `git branch --list '*/s*'`.

A worker that reports "wrong base", a blocked TRUNK edit, or a failure: do that slice
sequentially and tell me why. If subagent worktrees do not work at all, tell me and fall
back to sequential.

Then stop and say: "Next: `/pr <branch> <branch>`."
