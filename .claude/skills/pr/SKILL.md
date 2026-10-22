---
name: pr
description: Open one pull request per slice branch, with a description that is evidence. Syncs the branch with the latest base first (merge, tests), checks account and tests, pushes, creates the PR from .github/pull_request_template.md, then shows the CI result. Never merges.
argument-hint: [branch names; empty = the current branch]
disable-model-invocation: true
---

Branches: $ARGUMENTS (if empty: the current branch). Base: line 1 of `.claude/base-ref`,
else `main`. Several branches: do them one at a time, in the order given, and stop at the
first one that fails.

For each branch, `git switch <branch>` first.

1. **Preconditions.** Print each with OK / FAIL and stop at the first FAIL:
   - the branch is not the base, and its name is `<type>/<slug>` with a type from CLAUDE.md
     Branching,
   - `git status --short` is clean (otherwise say what still needs a commit),
   - `git remote get-url origin` exists. No remote: go to "Local only" below. Never create
     the repository yourself,
   - `gh auth status` shows my personal account as active (show it),
   - PR state: `gh pr view <branch> --json state,number,headRefOid`. "no pull requests
     found" = OK (new PR); OPEN = OK (update it); MERGED = FAIL: commits after the merge never
     go on this branch. Show them with `git log <headRefOid>..HEAD --oneline` (works for
     merge and squash merges alike) and propose a new branch from the base
     (`git switch -c <type>/<id>-<slug>-2 origin/<base>`, then `git cherry-pick` of exactly
     those commits). Wait for my OK.
2. **Sync with the base.** `git fetch origin`, then
   `git rev-list --count HEAD..origin/<base>`.
   - 0: OK, up to date.
   - More: say which commits (`git log HEAD..origin/<base> --oneline`), then
     `git merge origin/<base>` (its own call). Never rebase a pushed branch.
     On a conflict: stop, list each conflicted file with what each side changed and a
     proposed resolution. After my OK, resolve, `git add` the files and `git commit --no-edit`
     (the test-gate hook runs the tests).
   - Then run the full test command and show the counts. Red: stop; the fix is a commit on
     this branch: `/build on <branch> <failure>`, then `/pr <branch>` again.
3. **Slice evidence** (`feature/` and `bugfix/` branches only; skip for chore, docs,
   refactor and test branches): start the app, send 1-2 real requests for the acceptance
   criteria this slice covers (the happy path and one error path; a UI slice: the request
   behind the screen), and for an HTTP API check the slice's endpoints are in
   `/openapi/v1.json`. Show status and body, then stop the app with PowerShell
   (`Stop-Process -Name <AppName>`; not Git Bash, where `taskkill` flags get mangled) and
   confirm the port refuses connections. A merged PR is a finished slice, so its evidence
   lives in the PR.
4. **Body.** Fill `.github/pull_request_template.md` into `"$(git rev-parse --git-dir)/PR_BODY.md"` (not committed; in a worktree `.git` is a file, so never hard-code `.git/`)
   for this slice: docs/PLAN.md (the slice, its lane and Decision), docs/TASK.md (the
   acceptance criteria it covers, Not building, NFR and scale if relevant),
   `git log origin/<base>..HEAD --oneline`, the test output and the requests you just ran,
   and the review Should / Nice I did not pick. Claim only what was run; leave the CI line as "pending".
   Title: `<id>: <title>`, imperative, under 70 characters.
5. Show the title and body.

   **GATE:** wait for my OK.

6. **Push**, each as its own call (my OK at the GATE is the approval; the hook blocks a
   push to the base once the base exists on the remote):
   - new project and the base branch is not on origin yet
     (`git ls-remote --heads origin <base>` is empty): `git push -u origin <base>` first,
   - then `git push -u origin <branch>`.
7. If a PR for this branch is open: `gh pr edit <branch> --body-file "$(git rev-parse --git-dir)/PR_BODY.md"`.
   Otherwise `gh pr create --base <base> --head <branch> --title "<title>" --body-file "$(git rev-parse --git-dir)/PR_BODY.md"`
   and print the URL.
8. **CI.** If the repo has `.github/workflows/`: `gh pr checks <branch> --watch --interval 10 --fail-fast`
   with a Bash timeout of 600000 ms (the Claude review check can take minutes). Report each check as pass / fail with its link.
   - Pass: set the CI line in `"$(git rev-parse --git-dir)/PR_BODY.md"` to "passed (link)" and update the PR with
     `gh pr edit <branch> --body-file "$(git rev-parse --git-dir)/PR_BODY.md"`. If that fails, show the error and stop; do not
     use `gh api` instead.
   - Fail: show `gh run view --log-failed` (last 40 lines), state one hypothesis, and stop.
     The fix is a commit on this branch: `/build on <branch> <failure>`, then `/pr <branch>`.
   - No workflows: say so; the evidence is the local test run.
   - If the `claude-review` check ran, list its PR comments (`gh pr view <branch> --comments`) as
     Must / Should / Nice for me to triage. Fixes go through `/build on <branch> …`; never reply to or
     resolve them on GitHub without my OK.

After the last branch: `git switch <base>` and say what comes next:
- a slice branch: "Merge the PR(s) on GitHub, then `/build` for the next slice (it pulls the
  base first)"; after the last slice: "then `/post-review`",
- a `bugfix/` branch from `/post-review`: "then `/post-review evidence` once every picked fix
  is merged",
- `docs/evidence`: "Merge it; the session is done".
Never merge a PR and never push to the base branch after step 6. I merge. If one of several PRs gets merged first, the others may need
another `/pr <branch>` to sync with the base before they can merge.

## Local only (no remote)

Instead of steps 1-8, for each branch: check the first two preconditions (name, clean tree).
Sync with the local base on the branch: `git rev-list --count HEAD..<base>`; if more than 0,
`git merge <base>` (I approve), resolve any conflict here on the branch exactly as in step 2,
and run the full tests. Run step 3 (slice evidence) for feature and bugfix branches. Then
show `git log <base>..<branch> --oneline`, the test counts and the requests, and ask me to approve `git switch <base>` and `git merge --no-ff <branch>` as two separate
calls. After the sync this merge cannot conflict.
