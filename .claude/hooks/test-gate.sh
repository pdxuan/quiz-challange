#!/usr/bin/env bash
# PreToolUse gate on git commands (settings `if: Bash(git *)`). It protects the base branch
# and keeps red code out of commits:
#   git push   -> blocked when it targets the base branch (except the first push of a new
#                 base that is not on the remote yet). Slice branches push freely.
#   git commit -> blocked on the base branch, and blocked while the tests are red.
# Exit 2 blocks the call and sends stderr back to Claude; exit 0 lets it through.
# Test command: $KIT_TEST_CMD (selftest), else .claude/test-cmd, else auto-detect.

# The settings `if` filter is best-effort: commands it cannot parse (heredocs, pipes)
# still reach this hook, so the script matches `git ... push|commit` itself (global options
# like -C, -c, --no-pager included); other commands pass straight through.
# Free-text fields (the tool call's description, paths) are dropped so "...before git commit"
# in a description does not trigger the gate.
input=$(cat | sed -E 's/"(description|cwd|transcript_path)"[[:space:]]*:[[:space:]]*"([^"\\]|\\.)*"//g')
is_push=0; is_commit=0
echo "$input" | grep -Eq '\bgit\b[^|;&]*[[:space:]]push\b' && is_push=1
echo "$input" | grep -Eq '\bgit\b[^|;&]*[[:space:]]commit\b' && is_commit=1
[ "$is_push" = 1 ] || [ "$is_commit" = 1 ] || exit 0

cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0

# Base branch: line 1 of .claude/base-ref. KIT_BASE_BRANCH / KIT_CURRENT_BRANCH /
# KIT_BASE_ON_REMOTE are for the selftest.
base="${KIT_BASE_BRANCH:-}"
if [ -z "$base" ] && [ -f .claude/base-ref ]; then
  base=$(head -1 .claude/base-ref | tr -d '\r')
fi

# Nothing is pushed to the base: work reaches it only through a PR a human merges.
# Matches `origin main`, `HEAD:main`, `x:refs/heads/main`; not `feature/main-page`.
if [ "$is_push" = 1 ] && [ -n "$base" ]; then
  current="${KIT_CURRENT_BRANCH:-$(git branch --show-current 2>/dev/null)}"
  targets_base=0
  echo "$input" | grep -Eq "\bpush\b[^|;&\"]*([[:space:]]|:|refs/heads/)${base}([[:space:]\"]|$)" && targets_base=1
  # A bare `git push` (or `git push origin`) from the base pushes the base itself.
  if [ "$current" = "$base" ] && ! echo "$input" | grep -Eq '\bpush\b[^|;&"]*[[:space:]][^[:space:]"]+/[^[:space:]"]+'; then
    targets_base=1
  fi
  if [ "$targets_base" = 1 ]; then
    on_remote="${KIT_BASE_ON_REMOTE:-}"
    if [ -z "$on_remote" ]; then
      git ls-remote --exit-code --heads origin "$base" >/dev/null 2>&1 && on_remote=1 || on_remote=0
    fi
    if [ "$on_remote" = 1 ]; then
      echo "Push blocked by test-gate: '$base' is the base branch. Push the slice branch and open a PR; a human merges (CLAUDE.md, Branching)." >&2
      exit 2
    fi
  fi
fi
[ "$is_commit" = 1 ] || exit 0

# Nothing is committed on the base branch.
current="${KIT_CURRENT_BRANCH:-$(git branch --show-current 2>/dev/null)}"
if [ -n "$base" ] && [ "$current" = "$base" ]; then
  echo "Commit blocked by test-gate: '$current' is the base branch. Create the slice branch first: git switch -c <type>/<id>-<slug> (CLAUDE.md, Branching)." >&2
  exit 2
fi

fail() {
  echo "Commit blocked by test-gate: tests are failing. Fix the code (not the tests), then commit." >&2
  echo "$1" | grep -E "Failed|error|FAIL|failed" | head -20 >&2
  exit 2
}

cmd="${KIT_TEST_CMD:-}"
if [ -z "$cmd" ] && [ -f .claude/test-cmd ]; then
  cmd=$(head -1 .claude/test-cmd | tr -d '\r')
fi

if [ -n "$cmd" ] && [ "$cmd" != "TBD" ]; then
  out=$(bash -c "$cmd" 2>&1) || fail "$out"
  exit 0
fi

# No test command recorded yet: detect one.
sln=$(ls ./*.sln ./*.slnx 2>/dev/null | head -1)
test_projects=$(find . -name '*Test*.csproj' -not -path './.git/*' -not -path './.claude/*' -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null)

if [ -n "$sln" ]; then
  out=$(dotnet test "$sln" --nologo -v q 2>&1) || fail "$out"
elif [ -n "$test_projects" ]; then
  for p in $test_projects; do
    out=$(dotnet test "$p" --nologo -v q 2>&1) || fail "$out"
  done
elif [ -f package.json ] && grep -q '"test"' package.json && ! grep -q 'no test specified' package.json; then
  out=$(npm test --silent 2>&1) || fail "$out"
else
  echo "test-gate: no test command found (.claude/test-cmd is TBD); commit not checked." >&2
fi
exit 0
