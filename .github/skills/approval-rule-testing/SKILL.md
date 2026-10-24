---
name: approval-rule-testing
description: Plan, review, or execute unit and Playwright tests for the invoice approval lifecycle, role limits, high-value approvals, rejection atomicity, and authenticated identity. Use whenever invoice approval behavior or its tests change.
---

# Approval Rule Testing

## Inputs

- Active OpenSpec spec and design assumptions.
- Domain/API changes under test and current test fixtures/page objects.

## Procedure

1. Read `AGENTS.md`, the active OpenSpec spec/design/tasks, relevant Domain/API/auth code, existing xUnit tests, and Playwright page objects.
2. For single-approval limits, cover values below, at, and above 50,000 and 500,000 for each applicable role. Keep high-value first-role assumptions explicitly provisional.
3. For high-value flow, cover first approval pending state/history, distinct Director completion, repeated identity, non-Director second approver, invalid stages/status, and serialized competing attempts.
4. For every rejected domain attempt, assert clear reason, status, all decision fields, approval history, persisted repository object, and update behavior remain unchanged where appropriate.
5. Verify approval actor identity/role come from the authenticated session, not request body fields. Keep deletion Admin-only unless requirements say otherwise.
6. E2E tests use page objects and `getByRole`/`getByLabel`; avoid CSS/XPath selectors. Cover user-visible rejection and unchanged state.
7. Run from repository root: `dotnet test backend/InvoiceApp.sln`; E2E: `cd e2e && npm test` (install packages/browser only if absent).

## Output

Report coverage by requirement, missing/added cases, commands and observed results, and any assumptions that remain open. Do not claim tests ran if merely recommended. Do not weaken assertions to get green tests.
