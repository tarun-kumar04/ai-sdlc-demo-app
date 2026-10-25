# Design

## Context

See proposal.md for motivation and specs/invoice-approval-limits/spec.md for observable behavior. Stage 1 now enforces single-approval amount limits in `ApprovalService`. `ApprovalsController` uses `[RequireRole("Manager", "Director")]` and passes the authenticated session username and role to the Domain. Login copies configured roles into `UserSession(Username, Role)`; session middleware attaches the session to authenticated API requests. The invoice model now has approval history while `DecidedBy`/`DecidedAt` remain final-decision fields. The UI displays the session role and has an alert area for decision errors. Invoices and sessions are in memory.

## Goals / Non-Goals

**Goals:**

- Use only Manager and Director as approval-policy roles and update the two existing demo-user roles without adding identities or aliases.
- Keep amount limits, approval sequencing, and distinct-user checks in the Domain.
- Carry authenticated identity and role from the API session to the Domain, without accepting actor identity from a request body.
- Complete the work in separate planning, Stage 1, and Stage 2 commits.
- Keep user-authorized Stage 2 design choices explicitly provisional because the assignment does not settle first-approver eligibility or interim-state representation.

**Non-Goals:**

- Redesign invoice deletion authorization. The endpoint remains Admin-only; changing `demo.admin` to Director means that identity no longer has access to deletion.
- Add durable storage, external identity providers, or unrelated lifecycle refactoring.
- Treat the provisional first-approver eligibility or interim high-value invoice representation as confirmed assignment requirements.

## Decisions

- **Use the configured session roles directly.** Change `demo.approver` to Manager and `demo.admin` to Director while retaining usernames and credentials. Do not add role aliases or additional accounts. Login and middleware already carry the configured role in the session, so no new authentication mechanism is needed.
- **Keep API authorization consistent with existing session auth.** Change approval endpoint authorization to Manager and Director, and pass username and role from the authenticated session to the service. The Domain remains authoritative for amount and approval-order rules; request bodies must not supply approver identity. Keep the invoice-delete endpoint's `[RequireRole("Admin")]` unchanged. The Director demo identity therefore cannot use that action; do not expand this feature to change deletion permissions.
- **Keep approval rules and sequencing in the Domain.** Controllers supply authenticated context and translate domain outcomes, rather than implement limits or workflow. Validate a decision before mutation so rejected attempts do not change status, decision fields, or approval records. Approval-policy rejection responses must include a clear reason; authentication/role authorization behavior must remain consistent with the existing middleware and role-filter architecture.
- **Stage implementation and commits.** Commit the planning artifacts separately. Implement single-approval limits and their tests in Stage 1, then implement the high-value two-approver flow and its tests in Stage 2. Do not combine Stage 1 and Stage 2 commits.
- **Provisional Stage 2 first approver.** The user has authorized implementation on the provisional approach that either a Manager or Director may provide the first approval for an invoice above 500,000, followed by a different Director. The assignment does not specify the first approver's role, so this remains an implementation assumption, not a confirmed requirement. If authoritative assignment clarification becomes available, reconcile the behavior and artifacts.
- **Provisional first-approval representation.** The user has authorized an approval-history record with authenticated identity, role, and timestamp; after the first high-value approval, retain `Status` as `PendingApproval` and reserve `DecidedBy`/`DecidedAt` for final approval. This is an implementation assumption, not assignment-specified storage or status behavior.
- **Require a distinct Director for completion.** Before adding the second approval, validate that the session identity differs from the recorded first approver and that the session role is Director. Only then record the second approval, set final decision fields, and transition to `Approved`. Repeated-user and non-Director attempts must return a clear reason without changing the invoice, approval history, or final-decision fields.

## Risks / Trade-offs

- [The Director demo identity loses the existing Admin-only delete capability] → Keep deletion restricted to Admin as directed; do not silently grant it to Director. Record this intentional demo-account behavior.
- [The assignment does not define the first approver's role] → The user authorized Manager-or-Director-first for this implementation only; preserve the open specification question and do not describe it as an assignment requirement.
- [The assignment does not define approval-history storage or the interim state] → Implement the user-authorized history/`PendingApproval` approach provisionally; revise if requirements are clarified.
- [The in-memory repository loses approval history on restart] → Accept this limitation for the demo; no persistence migration is included.
- [Concurrent requests could attempt to occupy the same approval stage] → Make stage validation and mutation atomic for the repository's concurrency model and verify no rejected attempt partially changes state.
- [Current role-filter denials can return an empty 403] → Ensure rejected approval attempts expose a clear reason without weakening session authentication or changing unrelated endpoint authorization.

## Migration Plan

No persistent data migration is needed because users, sessions, and invoices are in memory. Update the configured roles and application behavior together. Restarting clears existing sessions and in-memory invoice data. Rollback is reverting the feature/configuration changes and restarting; the Admin-only delete restriction remains unchanged.

## Open Questions

1. Which role does the assignment intend to allow as the first approver for an invoice above 500,000? This implementation uses the user-authorized provisional Manager-or-Director-first assumption; the assignment itself remains silent.
2. Does the assignment intend approval history with authenticated identity, role, and timestamp, `PendingApproval` after the first approval, and final-only `DecidedBy`/`DecidedAt`? This implementation uses that user-authorized provisional design.
