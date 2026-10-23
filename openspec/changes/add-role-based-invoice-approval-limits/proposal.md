# Proposal

## Why

The approval service currently approves any pending invoice with one approver, regardless of amount or role. It cannot enforce the specified authority limits or require independent approval for invoices above 500,000.

## What Changes

### Confirmed Assignment Requirements

- Use Manager and Director as the only approval-policy role names. Configure the existing `demo.approver` and `demo.admin` identities with their existing credentials as Manager and Director respectively; do not add accounts or permanent role aliases.
- Enforce the inclusive Manager limit of 50,000 and Director single-approval limit of 500,000.
- Require two approvals by distinct users for invoices above 500,000, with the second approval made by a Director.
- Derive approval identity and role from the authenticated session; never trust actor identity supplied in an approval request body.
- Return a clear reason for rejected approval attempts and leave invoice state and approval records unchanged.
- Keep invoice deletion restricted to Admin. Consequently, the existing `demo.admin` identity, configured as Director, will no longer be able to use the Admin-only delete action. Do not expand this change to redesign deletion authorization.
- Implement in separate commits: planning artifacts, Stage 1 single-approver limits and tests, and Stage 2 two-approver workflow and tests.

### Proposed Stage 2 Design Assumptions (Not Confirmed by the Assignment)

- Either a Manager or Director may provide the first approval for an invoice above 500,000; the second approval must be by a different Director.
- Record each accepted approval as history containing authenticated identity, role, and timestamp. After the first approval, keep the invoice `PendingApproval` and leave `DecidedBy`/`DecidedAt` for the final decision.
- Validate these assumptions against the assignment and resolve them before Stage 2 implementation. They are design proposals, not confirmed requirements.

## Capabilities

### New Capabilities

- `invoice-approval-limits`: Role-based approval limits, high-value two-person approval, and authenticated approval identity enforcement.

### Modified Capabilities

None. The repository has no existing capability specs.

## Impact

- Domain: approval policy and invoice approval state in `ApprovalService` and invoice models.
- API/authentication: approval authorization and passing authenticated session identity and role to the Domain; existing Admin-only deletion authorization remains unchanged.
- Configuration/UI: update roles on the two existing demo identities; the generic UI displays the configured role and existing decision error area can show rejection reasons.
- Tests: add staged xUnit coverage and a Playwright UI test for a rejected over-limit approval.

## Open Questions

1. Does the assignment permit either a Manager or Director to make the first approval for an invoice above 500,000, provided the second approval is by a different Director?
2. Is approval history with authenticated identity, role, and timestamp, `PendingApproval` after the first approval, and final-only `DecidedBy`/`DecidedAt` acceptable as the Stage 2 representation?
