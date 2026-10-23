# Project conventions for AI agents

Invoice approval demo app: ASP.NET Core 8 Web API with a small static web UI.

## Layout

- `backend/src/InvoiceApp.Domain`: business rules, no web or storage code. `ApprovalService` owns the invoice lifecycle (Draft, PendingApproval, Approved, Rejected).
- `backend/src/InvoiceApp.Api`: controllers, sign-in (`Auth/`), in-memory repository, and the web UI under `wwwroot/`.
- `backend/tests/InvoiceApp.Tests`: xUnit tests, one test class per class under test, named `<ClassUnderTest>Tests`.
- `e2e`: Playwright end-to-end tests (`tests/`) and page objects (`pages/`).
- `docs/use-cases`: short briefs for features we may build.
- `openspec`: specification-driven change proposals, designs, specifications, and task breakdowns.

## Conventions

- Controllers live in `Controllers/`, one per resource, route `api/[controller]`. Request bodies are `record` types declared inside the controller.
- Controllers inherit `ApiControllerBase` and use `Handle(...)` so service errors map to 404, 409 and 400.
- Every endpoint is behind sign-in. Restrict actions with `[RequireRole("...")]`. Destructive actions remain Admin-only. Approval authorization must use the configured Manager and Director roles.
- Business rules go in the Domain project, never in controllers.
- Register services in `Program.cs`. Repositories are interfaces in Domain with an in-memory implementation in Api.
- Every new rule or endpoint needs tests. Use `FakeInvoiceRepository` in tests.
- Use clear names; no one-letter variables or `flag` parameters.
- End-to-end tests use page objects and role-based locators (`getByRole`, `getByLabel`), never CSS or XPath selectors.
- Follow the active OpenSpec proposal, specification, design, and tasks. Keep them consistent with implementation; document unresolved decisions rather than silently inventing requirements.

## Invoice approval rules

- A Manager may approve an invoice in the normal single-approval flow when the amount is less than or equal to 50,000.
- A Director may approve an invoice in the normal single-approval flow when the amount is less than or equal to 500,000.
- Invoices above 500,000 require two approvals from distinct users. The second approver must be a Director.
- The required role for the first approver of an invoice above 500,000 is currently unresolved. Do not infer or implement a first-approver role until the requirement is confirmed.
- The storage of the first high-value approval and the invoice's interim status while awaiting the second approval are also unresolved. Confirm these decisions before implementing the Stage 2 workflow.
- Approval thresholds, approval sequencing, and distinct-user checks belong in the Domain layer.
- Controllers pass authenticated context to the Domain and translate service outcomes; they must not implement approval policy.
- Derive approver identity and role from the authenticated session. Never trust a user ID, username, or role supplied in an approval request body.
- Validate an approval attempt before mutating invoice state. Rejected attempts must return a clear reason and leave invoice status, decision fields, and approval records unchanged.
- Test threshold boundaries, including values immediately below, at, and above 50,000 and 500,000. Cover repeated approvers, a non-Director second approver, invalid approval stages, and unchanged state after rejected attempts.
- Keep the implementation in two stages and separate commits: Stage 1 implements normal single-approval limits and tests; Stage 2 implements the confirmed high-value two-approver workflow and tests.

## Roles and demo accounts

- Use `Manager` and `Director` as the only approval-policy role names. Do not introduce permanent `Approver`/`Admin` approval aliases or additional demo identities.
- Retain the existing demo usernames and credentials, changing their configured roles as follows:
  - `demo.approver` / `ChangeMe123!` — role `Manager`
  - `demo.admin` / `ChangeMe123!` — role `Director`
- Keep invoice deletion restricted to `Admin`. Do not grant `Director` deletion permission as part of this feature. Consequently, the configured Director demo identity will not have access to the Admin-only deletion action.
- Do not change unrelated authorization behavior or grant new privileges without an explicit requirement.

## Commands

- Run the app: `dotnet run --project backend/src/InvoiceApp.Api`, then open [http://localhost:5000](http://localhost:5000).
- Run the unit tests: `dotnet test backend/InvoiceApp.sln`.
- Run end-to-end tests: `cd e2e && npm install && npx playwright install chromium && npm test`.
- Validate the OpenSpec change: `openspec validate add-role-based-invoice-approval-limits`.
- Check OpenSpec progress: `openspec status --change add-role-based-invoice-approval-limits`.

## Never

- Commit real credentials, tokens, or customer data.
- Add business logic to `Program.cs` or controllers.
- Trust request-body identity or role information for approval authorization.
- Treat unresolved business rules as confirmed requirements.
- Claim tests, validation, or reviews passed unless they were actually run.
- Commit Stage 1 and Stage 2 implementation together.
