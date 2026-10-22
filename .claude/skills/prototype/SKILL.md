---
name: prototype
description: Optional, after /intake and before /plan. Builds a throwaway clickable HTML prototype from docs/TASK.md (key screens, or an API walkthrough, plus the open decisions as cards) so I can see the shape and give feedback before real code. My feedback goes back into TASK.md.
argument-hint: [focus, e.g. "invite flow only", or my feedback on the current prototype]
disable-model-invocation: true
---

Input: $ARGUMENTS

Read docs/TASK.md. This is a sketch to get my feedback, not product code: one
self-contained file, `docs/prototype/index.html` (plain HTML, CSS and JS; no build, no
packages, no network calls, fake data inline). The app never imports it; it is not a
slice and needs no tests.

If the input is feedback on an existing prototype, or I pasted `> REVIEW:` lines, go to
step 4.

1. **Views** (at most 5), following the main flow in TASK.md:
   - Task with a UI: the key screens as clickable views with navigation between them,
     realistic fake data, and the states of the riskiest acceptance criteria (empty,
     error, pending, conflict).
   - API-only task: an API walkthrough of the main flow, one step per call: method and
     path, request body, response status and body from the Contract, plus one error path.
   Mark anything the prototype assumes that TASK.md does not say with a visible "?" note.
2. **Decisions tab**: one card per open item in TASK.md (Decisions with status default or
   deferred, Assumptions with source "assumed", ONE-WAY requirements). Each card: the
   question, the options as radio buttons (recommended first and marked), and a comment
   box. A "Copy feedback" button turns the cards I touched into lines
   `> REVIEW: <id> - <choice>; <comment>` and copies them (fallback: show them in a
   textarea to copy by hand).
3. A top banner: "Prototype - throwaway, generated from docs/TASK.md". Keep it under about
   400 lines. Write the file, then tell me to open it (`start docs\prototype\index.html`
   in PowerShell) and say in 3 lines what each view shows and what you want feedback on.

   **GATE:** stop for my feedback.

4. **Apply feedback.** For each item (pasted `> REVIEW:` lines or notes in chat): update
   docs/TASK.md first (Requirements and lanes, Contract, Acceptance criteria, Decisions,
   Not building), then the prototype to match. List each item -> what changed in TASK.md.
   Say explicitly when feedback changes a lane, adds a ONE-WAY item, or grows the scope.

   **GATE:** stop. When I am happy I run `/plan`. The prototype stays local (`docs/prototype/` is gitignored); its outcome lives in docs/TASK.md.
