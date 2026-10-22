# Plan

## Outcome

The observable result at the end of the session.

## Approach

3-5 bullets: stack, layout, storage, the trade-off that matters most and why.

## Slices

Status: `[ ]` todo, `[~]` in progress, `[x]` committed on its branch (done once its PR is
merged and the base shows `[x]`). One branch and one PR per slice.
Wave 0 is TRUNK, done in order in the main session. Wave 1 slices are LEAF with
non-overlapping files and may run in parallel worktrees.

### Wave 0 (TRUNK)

- [ ] **S0 Walking skeleton** - TRUNK
  - Branch: chore/s0-walking-skeleton
  - Covers: -
  - Files: solution, project files, Program.cs, test project
  - Read first: -
  - Depends on: -
  - Tests: one passing integration test
  - Done when: the test command is green and the app starts.
  - Decision: (written while building, if any)

### Wave 1 (LEAF)

- [ ] **S1 ...** - LEAF
  - Branch: feature/s1-...
  - Covers: R#, AC#
  - Files:
  - Read first:
  - Depends on: S0
  - Tests:
  - Done when:
  - Decision: (written while building, if any)

Parallel math: sequential = sum of slices; parallel = longest slice + 2' setup + 3' per apply.

## Decisions

Written by `/plan` only. A decision made while building goes into that slice's block.

- HH:MM - choice - why. ONE-WAY: rollback = ...

## Risks

- Risk - mitigation.

## Progress

Merged PRs are the progress: `git log --first-parent --oneline <base>`.
