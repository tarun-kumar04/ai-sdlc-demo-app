# Design

## Context

See proposal.md for motivation and specs/invoice-approval-limits/spec.md for observable behavior. `ApprovalService` currently approves any pending invoice in one call and stores one `DecidedBy`/`DecidedAt` pair. `ApprovalsController` uses `[RequireRole("Approver", "Admin")]` and passes only the session username to the Domain. Login copies the configured role into `UserSession(Username, Role)`; session middleware attaches it to authenticated API requests. The UI displays the session role and an existing alert area for decision errors. Invoices and sessions are in memory.

## Goals / Non-Goals

**Goals:**

- Use only Manager and Director as approval-policy roles and update the two existing demo-user roles without adding identities or aliases.
- Keep amount limits, approval sequencing, and distinct-user checks in the Domain.
- Carry authenticated identity and role from the API session to the Domain, without accepting actor identity from a request body.
- Complete the work in separate planning, Stage 1, and Stage 2 commits.

**Non-Goals:**

- Redesign invoice deletion authorization. The endpoint remains Admin-only; changing `demo.admin` to Director means that identity no longer has access to deletion.
- Add durable storage, external identity providers, or unrelated lifecycle refactoring.
- Decide the unresolved first-approver eligibility or interim high-value invoice representation without confirmation.

## Decisions

- **Use the configured session roles directly.** Change `demo.approver` to Manager and `demo.admin` to Director while retaining usernames and credentials. Do not add role aliases or additional accounts. Login and middleware already carry the configured role in the session, so no new authentication mechanism is needed.
- **Keep API authorization consistent with existing session auth.** Change approval endpoint authorization to Manager and Director, and pass username and role from the authenticated session to the service. The Domain remains authoritative for amount and approval-order rules; request bodies must not supply approver identity. Keep the invoice-delete endpoint's `[RequireRole("Admin")]` unchanged. The Director demo identity therefore cannot use that action; do not expand this feature to change deletion permissions.
- **Keep approval rules and sequencing in the Domain.** Controllers supply authenticated context and translate domain outcomes, rather than implement limits or workflow. Validate a decision before mutation so rejected attempts do not change status, decision fields, or approval records. Approval-policy rejection responses must include a clear reason; authentication/role authorization behavior must remain consistent with the existing middleware and role-filter architecture.
- **Stage implementation and commits.** Commit the planning artifacts separately. Implement single-approval limits and their tests in Stage 1, then implement the high-value two-approver flow and its tests in Stage 2. Do not combine Stage 1 and Stage 2 commits.
- **Keep high-value first-step design open.** The proposal for either Manager or Director to act first is provisional only. Confirm the eligible first role and the representation/pending state for the first approval before Stage 2 design or implementation. Do not encode the proposal as an established requirement.

## Risks / Trade-offs

- [The Director demo identity loses the existing Admin-only delete capability] → Keep deletion restricted to Admin as directed; do not silently grant it to Director. Record this intentional demo-account behavior.
- [The first-approver role and interim state are unresolved] → Block Stage 2 implementation until both decisions are confirmed and the spec/design are updated.
- [The in-memory repository loses approval history on restart] → Accept this limitation for the demo; no persistence migration is included.
- [Concurrent requests could attempt to occupy the same approval stage] → Make stage validation and mutation atomic for the repository's concurrency model and verify no rejected attempt partially changes state.
- [Current role-filter denials can return an empty 403] → Ensure rejected approval attempts expose a clear reason without weakening session authentication or changing unrelated endpoint authorization.

## Migration Plan

No persistent data migration is needed because users, sessions, and invoices are in memory. Update the configured roles and application behavior together. Restarting clears existing sessions and in-memory invoice data. Rollback is reverting the feature/configuration changes and restarting; the Admin-only delete restriction remains unchanged.

## Open Questions

1. Which role may make the first approval for an invoice above 500,000? The planning proposal is Manager or Director, but it is unconfirmed and must not be treated as settled.
2. How is that first approval stored, and what pending state is used while the invoice awaits the Director's second approval?
