---
description: HTTP API design, validation and error response standards
paths:
  - "**/*.cs"
---

# API standards

If the repo already has conventions, follow them over this file.

- Request and response models are `record` types. Never expose persistence entities directly.
- Validate input at the boundary; invalid input returns 400 with a `ProblemDetails` body
  (`Results.ValidationProblem` for field errors). No manual checks scattered in handlers.
- Status codes: 201 + Location for create, 200 for read/update, 204 for delete, 404 when the
  resource does not exist, 409 for a business-rule conflict (duplicate, overlap, stale version).
- Errors always use `ProblemDetails` (`AddProblemDetails()`); never return raw exception text.
- A business rule lives in one place (domain or service), not in the endpoint and not in tests.
- Operations a client may retry (create with client id, payment-like actions) must be idempotent
  or docs/TASK.md must say why not.
- Time: store and compare in UTC (`DateTimeOffset`/`TimeProvider`), never `DateTime.Now`.
- The API describes itself: an OpenAPI document (`AddOpenApi()` / `MapOpenApi()`, at
  `/openapi/v1.json`) and a browsable UI at `/swagger` (Swashbuckle.AspNetCore.SwaggerUI, or
  Scalar), both only in Development. These are kit defaults, set up in the walking skeleton and
  pre-approved as dependencies (still listed in docs/TASK.md Decisions). Every endpoint
  declares its name, a one-line summary, its request type and every status code it returns
  (`.WithName()`, `.WithSummary()`, `.Produces<T>()` / `.ProducesProblem()` /
  `.ProducesValidationProblem()`, or the controller attributes), so the document matches the
  Contract in docs/TASK.md.
- Keep endpoints thin: parse, delegate, map the result. Where the logic goes (feature folder,
  service, domain model, handler) follows docs/TASK.md Code structure; this file does not
  choose an architecture.
- Each feature registers itself through one extension point in the composition root, created
  as an empty stub by the walking skeleton, so LEAF slices never need to touch Program.cs.
