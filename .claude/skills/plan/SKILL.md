---
name: plan
description: Turn the reviewed docs/TASK.md into slices and waves in docs/PLAN.md (TRUNK first, parallel-safe LEAF after), update trunk-paths.txt. Handles my "> REVIEW:" notes first. No code.
argument-hint: [optional: extra or changed constraints; the stack comes from docs/TASK.md]
disable-model-invocation: true
---

Extra constraints from me (optional; they override docs/TASK.md where they conflict): $ARGUMENTS

Read `docs/TASK.md`, `CLAUDE.md`, `.claude/rules/` and `.claude/trunk-paths.txt`. No code yet.

1. **Review notes first.** Address every line starting with `> REVIEW:` in docs/TASK.md and
   docs/PLAN.md, remove each note, and list them with the change made. If a note is
   unclear or conflicts with docs/TASK.md, ask instead of guessing.
2. **Hidden-requirements check**, then **Approach**. Map every box and arrow in the
   docs/TASK.md Architecture diagram and every "Now" cell in NFR and scale to an AC, one
   line each. Anything with no AC: remove it from the diagram, add it to "Not building",
   and say so. No slice may build an "At scale" item. Then the Approach in 3-5 bullets:
   stack, layout, storage, the trade-off that matters most; point to TASK.md Architecture
   instead of repeating it. Every layer, package, store or environment must serve an
   acceptance criterion.
3. **Slices**, each small enough to review in one diff, 4-7 in total for a 2-hour session
   (merge slices under ~8 minutes into a neighbour; every slice costs a review):
   - S0 (TRUNK). New project: walking skeleton, test project, one passing integration test,
     the test command written to `.claude/test-cmd`, for an HTTP API the OpenAPI document and
     the Swagger UI in Development (see api-standards; plus a test that `/openapi/v1.json`
     returns 200), and an extension point per planned
     feature in the shape docs/TASK.md Code structure chose (e.g. `app.Map<Feature>()` stubs
     for minimal APIs, one controller per feature, one module registration per feature), so
     LEAF slices never touch the composition root. Existing code: a characterization test that pins today's behavior of the
     area you will change.
   - Other TRUNK slices next (shared model, storage, contract), riskiest rule early.
   - Each Known issue marked "fix now": its own slice that starts with a test reproducing
     the bug (red for the right reason), then the fix; TRUNK if it touches shared code.
   - LEAF slices: one user-visible capability each, with a precise Files list (folders).
   - Files lists follow docs/TASK.md Code structure; a slice that needs a new folder,
     layer or pattern not in that table proposes it as a Decision first.
   - For each slice: lane, Branch (`<type>/<id>-<slug>`, type per CLAUDE.md Branching:
     feature, bugfix, refactor, test, chore, docs), Covers (R#, AC#), Files, Read first,
     Depends on, Wave, Tests, Done when. Never mix TRUNK and LEAF in one slice.
   - Self-check: a file that appears in 2+ slices is TRUNK; move that work into Wave 0.
     Wave 1 slices must have disjoint Files that match nothing in trunk-paths.txt.
   - ONE-WAY: add a Decisions entry with the rollback and mark the slice "needs OK".
4. **Parallel math** for Wave 1, at most 2 slices at a time: pair LEAF slices of similar
   size; per pair, sequential = a + b, parallel = max(a, b) + 2' setup + 2 x 3' apply.
   Say which pair(s) to run with `/build Sx Sy`, and run the rest sequentially.
5. Add globs for the TRUNK files to `.claude/trunk-paths.txt` (keep existing lines).
6. If the review notes changed the stack, architecture, code structure or a key decision,
   update the Project overview lines in the kit agreement to match (it is committed with
   the plan).
7. Write all of it into `docs/PLAN.md` using its sections, with a blank line between slice
   blocks (parallel slice PRs each edit their own block; adjacent lines would conflict),
   then show a short summary.

**GATE:** stop for my review. When I approve, each as its own Bash call (the test-gate hook
checks the branch before a call runs):
1. `git switch <base>` (base: line 1 of `.claude/base-ref`, else `main`; the uncommitted
   docs come along), then `git switch -c chore/plan`, or `chore/plan-2`, `-3`… if that
   branch already exists (a re-plan).
2. Stage only kit and plan paths by name: `docs/`, `.claude/`, and the kit files
   `git status --short` lists among CLAUDE.md, .worktreeinclude, .github/. Never `git add -A`
   here: other changes in the tree are not mine to commit; list them instead.
3. Commit with `docs: requirements and plan` and show `git status --short`.

Say: "Next: `/pr` for chore/plan; merge it, then `/build`." Every slice branch and every
worktree starts from the base, so the plan must be merged into it before `/build`.
