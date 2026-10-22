---
name: intake
description: Start here. Parallel research (technical + domain, plus a defect audit in an existing repo), then approaches, clarifying questions, a lane per requirement, and docs/TASK.md. No code.
argument-hint: <task text, pasted as given>
disable-model-invocation: true
---

Task:

$ARGUMENTS

You are the engineer in a requirements discussion; I decide. Do not plan slices, write
code, create projects or install anything.

1. **Research in parallel.** Say your first read of the task in 2 lines. Then, in **one
   message**, spawn these subagents so they run at the same time, each with the task text:
   - `researcher`: the technical solution (stack, code structure, patterns, load).
   - `domain-researcher`: the domain (flows, business rules, edge cases the task omits).
   - **Existing code** (source files or a manifest such as `*.sln`, `*.csproj`,
     `package.json` outside `.claude/`), also in the same message:
     - `auditor`: what is already broken or critical, with evidence;
     - one Explore subagent per module if there are several; each reports only what exists.
   Tell each "existing repo: focus on the area this task touches" when code exists.
   While they run, tell me what you expect them to find; afterwards say what matched and
   what surprised you.
   Existing code, after the agents return: entry points and the main flow of the task area,
   conventions to follow, and blast radius (count dependents of the files the task will
   touch, e.g. `grep -rl "OrderService" --include=*.cs . | wc -l`; 3 or more means TRUNK).
2. Restate the task in at most 3 lines. From the research, give a one-line thesis and
   2-3 approaches, each with its trade-off (cost now / cost to change later), and
   recommend one. Show the decision backlog: which decisions you take as technical
   defaults (two-way) and which need me (product, cost, contract, ONE-WAY).
   Existing code: show the auditor's critical and high findings.
3. Find what is ambiguous or missing: data model, edge cases (use the domain researcher's
   rules and edge cases marked "changes design"), errors and status codes, validation,
   concurrency or double submission, persistence, auth, UI vs API, the real load and data
   size to design for (users, requests, records; right-size, never assume high scale),
   and backlog decisions marked "Needs human".
   Existing code: also "what must not change?", "who calls this today?", and whether the
   critical findings (and code-vs-domain mismatches) are in scope to fix.
   Rank by how much the answer changes the design; keep at most 8. Ask them with the
   `AskUserQuestion` tool, 2-4 concrete options each, your recommendation first
   (if the tool is unavailable, ask a numbered list). I may relay them to the interviewer.

   **GATE:** wait until every question is answered.

4. Classify each requirement R1..Rn into a lane using the Lanes section of CLAUDE.md.
   Judge by blast radius (who depends on it, can it be rolled back), not by keywords.
   One-line reason each. Unsure: TRUNK. A known issue chosen for fixing is a requirement too.
5. Fill `docs/TASK.md`:
   - Goal; Current state and Known issues (existing code only: baseline, auditor findings
     with evidence, and the decision for each: fix now / note / out of scope);
   - Architecture: thesis, approaches, a mermaid diagram of the recommended approach only,
     labelled as such, at most 8 nodes, source of truth, data model, key flow, and Code
     structure (style, folder tree, patterns table with what was considered and not used,
     dependency rule; existing code: what is there);
   - NFR and scale; Decisions (from the backlog, with reversibility);
   - Requirements with lanes; Assumptions (with source); Contract; Acceptance criteria (one
     checkable line per behavior, error paths included); Not building (what you were
     tempted to add and why not); Safety (the three questions).
   Only write what was answered or explicitly assumed. One exception: the "At scale"
   column is analysis, talk only. Its items and the researchers' "tempted but not asked"
   items also go to Not building.
   Existing code: also set `.claude/test-cmd` (exclude known-red tests with a filter and
   note why in Known issues) and add the TRUNK files you found to `.claude/trunk-paths.txt`.
   Then fill the `## Project overview` section (at most 8 lines, replacing every TBD) in the
   file that holds the kit agreement: `CLAUDE.md`, or `.claude/rules/00-kit-agreement.md` in a
   repo that had its own CLAUDE.md. It is loaded in every session; TASK.md keeps the detail.
6. Show a short summary: the thesis, the diagram, approach chosen, critical findings
   (existing code), decisions still open, the requirements table, anything assumed.
7. End with **Your review**: 3-5 concrete things for me to check or decide in TASK.md,
   most important first (a critical finding comes first). Each one says where to look
   (section, row), why it matters (critical finding, lane, ONE-WAY, assumed answer, open
   decision, a dependency to approve), and a ready-to-copy note, e.g.
   `> REVIEW: D2 - keep cookie auth`. Close with: "Then run `/plan`."

**GATE:** stop. I review docs/TASK.md and may edit it or leave `> REVIEW:` notes for `/plan`.
