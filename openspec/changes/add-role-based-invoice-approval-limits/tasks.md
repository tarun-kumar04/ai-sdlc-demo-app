# Tasks

## 0. Planning commit (Commit 1)

- [ ] 0.1 Commit the revised proposal, capability spec, design, and staged tasks as a planning-only commit; verify only the intended OpenSpec artifacts are included and `openspec validate add-role-based-invoice-approval-limits` succeeds.

## 1. Stage 1 — Single-approver limits (Commit 2)

- [ ] 1.1 Change the configured roles for the existing demo identities to Manager and Director without changing usernames or credentials or adding accounts; update approval endpoint role restrictions to Manager/Director while leaving invoice deletion Admin-only, and verify login/session roles plus a Director's 403 response on the Admin-only delete endpoint.
- [ ] 1.2 Implement the Manager limit of 50,000 and Director normal single-approval limit of 500,000 in the Domain, passing identity and role from the authenticated session and accepting no actor identity from request bodies; verify approval policy is not implemented in API controllers.
- [ ] 1.3 Add unit tests below, at, and above both thresholds (including 49,999.99, 50,000, 50,000.01, 499,999.99, 500,000, and 500,000.01); verify Manager approval eligibility, Director normal single-approval eligibility, and that an invoice above 500,000 cannot be finalized with only one approval, without assuming which role may make that first approval. For every rejection assert a clear reason and unchanged invoice status, decision fields, and approval records; run `dotnet test backend/InvoiceApp.sln`.
- [ ] 1.4 Add a separate Playwright UI test in the E2E suite where a Manager attempts a rejected over-limit approval; assert the UI shows the rejection reason and invoice status/state remains unchanged, using page objects and role-based locators, then run `cd e2e && npm test`.
- [ ] 1.5 Commit Stage 1 implementation and its tests separately from the planning and Stage 2 commits; verify the commit includes the role configuration, single-approval policy, and Stage 1 tests only.

## 2. Stage 2 — High-value two-approver workflow (Commit 3)

- [ ] 2.1 Before Stage 2 implementation, obtain confirmation of the first-approver role and interim approval storage/pending state; update the proposal, spec, and design to record the confirmed decisions and verify no provisional first-approver proposal is presented as settled.
- [ ] 2.2 Implement the above-500,000 approval sequence in the Domain using the confirmed first-approver and interim-state decisions; require a second approval from a Director distinct from the first approver, and verify controllers only pass authenticated context and translate Domain outcomes.
- [ ] 2.3 Add unit tests for accepted first and second approval stages, repeated-user rejection, non-Director second-approver rejection, invalid stage/state, and applicable amount boundaries. For every rejected attempt assert a clear reason and unchanged invoice status, decision fields, and all approval records; run `dotnet test backend/InvoiceApp.sln`.
- [ ] 2.4 Add or extend Playwright coverage for the confirmed high-value approval flow and rejected repeated/non-Director attempts; verify visible rejection reasons and unchanged invoice state with page objects and role-based locators, then run `cd e2e && npm test`.
- [ ] 2.5 Commit Stage 2 implementation and its tests separately from the planning and Stage 1 commits; verify the commit contains the high-value workflow and Stage 2 tests.

## 3. Final integration verification and review

- [ ] 3.1 Run `dotnet test backend/InvoiceApp.sln` and `cd e2e && npm test`; verify both results are recorded accurately and no unexecuted check is reported as passing.
- [ ] 3.2 Review the complete diff and commit history against this checklist: Manager/Director are the only approval-policy roles; existing identities/credentials are retained with changed roles and no aliases/new accounts; deletion remains Admin-only; approval rules remain in Domain; session identity/role are trusted over request data; boundary, negative, unchanged-state, two-approver, and rejected-over-limit UI tests are present; planning, Stage 1, and Stage 2 are three separate commits. Report changed files and actual test results.
