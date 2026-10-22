---
description: Integration testing standards
paths:
  - "**/*Test*/**/*.cs"
  - "**/*Tests*/**/*.cs"
---

# Testing standards

- Prefer integration tests through the public boundary (for an HTTP API:
  `WebApplicationFactory<Program>`) that exercise the real path down to storage, whatever the
  code structure. Do not mock our own code; mock only external services.
- One behavior per test, Arrange / Act / Assert. Name: `Method_Scenario_Expected` or a clear sentence.
- Every acceptance criterion in docs/TASK.md has at least one test, including the error paths
  (400 validation, 404 missing, 409 conflict).
- Each test must fail if the behavior it names breaks: assert on the response body or the
  stored state, not only the status code (for errors, check the ProblemDetails field too).
  Never assert on what a mock returns.
- Existing codebase: before changing a behavior, add a characterization test that pins down
  what it does today, then change it.
- Each test owns its data; tests must pass in any order and when run alone.
- Never weaken an assertion, add `Skip`, or delete a test to get green. Stop and explain instead.
