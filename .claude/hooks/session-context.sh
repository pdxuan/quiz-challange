#!/usr/bin/env bash
# SessionStart hook: prints where the work stands, so every session (start, resume, /clear,
# compact) knows it without reading files first. Stdout becomes context for Claude.
# Local and fast: git and docs/PLAN.md only, no network. Never blocks (always exit 0).
cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

base=main
[ -f .claude/base-ref ] && base=$(head -1 .claude/base-ref | tr -d '\r')
branch=$(git branch --show-current 2>/dev/null)
dirty=$(git status --short 2>/dev/null | wc -l | tr -d ' ')

echo "Kit state (git + docs/PLAN.md, at session start):"
echo "- Branch: ${branch:-detached} (base: $base)$([ "$branch" = "$base" ] && echo ' - on the base: create a slice branch before any commit')"
echo "- Uncommitted files: $dirty"

if [ -f docs/PLAN.md ]; then
  slices=$(grep -E '^- \[[ x~]\] \*\*S[0-9]+' docs/PLAN.md | sed -E 's/^- \[(.)\] \*\*(S[0-9]+)[^*]*\*\*.*/\2[\1]/' | tr '\n' ' ')
  [ -n "$slices" ] && echo "- Slices (this branch's PLAN.md): $slices"
  next=$(grep -E '^- \[ \] \*\*S[0-9]+' docs/PLAN.md | head -1 | sed -E 's/^- \[ \] \*\*([^*]*)\*\*.*/\1/')
  [ -n "$next" ] && echo "- Next todo: $next"
  wip=$(grep -E '^- \[~\] \*\*S[0-9]+' docs/PLAN.md | sed -E 's/^- \[~\] \*\*([^*]*)\*\*.*/\1/' | tr '\n' ';')
  [ -n "$wip" ] && echo "- In progress: $wip"
else
  echo "- docs/PLAN.md: not written yet (start with /intake, then /plan)"
fi

open=$(git branch --list '*/s*' --no-merged "$base" 2>/dev/null | sed 's/^[* ]*//' | tr '\n' ' ')
[ -n "$open" ] && echo "- Slice branches not merged into $base locally: $open"
echo "Read docs/TASK.md (Architecture, Code structure, Contract) before changing code outside a skill."
exit 0
