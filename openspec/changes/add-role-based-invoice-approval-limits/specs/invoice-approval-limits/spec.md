# Spec Delta

## Purpose

Define role-based invoice approval limits and independent approval requirements for high-value invoices.

## ADDED Requirements

### Requirement: Manager single-approval limit

The system SHALL allow a Manager to approve a pending invoice in the normal single-approval flow only when its amount is at most 50,000.

#### Scenario: Manager approves at the limit

- **WHEN** a Manager approves a pending invoice of 50,000 in the normal single-approval flow
- **THEN** the invoice is approved

#### Scenario: Manager attempts an over-limit single approval

- **WHEN** a Manager attempts a normal single approval for an invoice above 50,000 and at or below 500,000
- **THEN** the attempt is rejected with a clear reason and the invoice state remains unchanged

### Requirement: Director single-approval limit

The system SHALL allow a Director to approve a pending invoice in the normal single-approval flow only when its amount is at most 500,000.

#### Scenario: Director approves at the limit

- **WHEN** a Director approves a pending invoice of 500,000 in the normal single-approval flow
- **THEN** the invoice is approved

#### Scenario: One approval cannot finalize a high-value invoice

- **WHEN** only one approval has been recorded for an invoice above 500,000
- **THEN** the invoice is not approved and a second approval from a distinct Director is required

### Requirement: Two distinct approvers for high-value invoices

The system SHALL require two approvals from distinct users before an invoice above 500,000 is approved, and the second approver SHALL be a Director. First-approver eligibility and the interim representation described below are proposed design assumptions, not confirmed assignment requirements, and SHALL be validated before Stage 2 implementation.

#### Scenario: First approval is recorded without approving the invoice

- **WHEN** a first approval is accepted for an invoice above 500,000 under a first-approver eligibility rule confirmed before Stage 2 implementation
- **THEN** the system records the authenticated approver's identity, role, and timestamp, leaves the invoice `PendingApproval`, and leaves `DecidedBy` and `DecidedAt` unset for the final decision

First-approver eligibility is provisional and must be confirmed against the assignment before Stage 2 implementation; Manager-or-Director-first is not an established assignment requirement.

#### Scenario: Distinct Director completes high-value approval

- **WHEN** a Director different from the first approver provides the second approval
- **THEN** the system records the second approval, sets the final decision fields, and marks the invoice `Approved`

#### Scenario: Non-Director attempts the second approval

- **WHEN** a user who is not a Director attempts the second approval
- **THEN** the attempt is rejected with a clear reason and invoice state, approval history, and final-decision fields remain unchanged

#### Scenario: First approver attempts to approve again

- **WHEN** the user recorded as the first approver attempts the second approval
- **THEN** the attempt is rejected with a clear reason and invoice status, approval history, and final-decision fields remain unchanged

### Requirement: Approval identity and role come from the authenticated session

The system SHALL derive the approval actor's identity and role from the authenticated session and SHALL NOT trust actor identity or role supplied in an approval request body.

#### Scenario: Request body contains actor information

- **WHEN** an authenticated user submits an approval request containing a user ID, username, or role
- **THEN** the system uses the identity and role from that user's authenticated session

### Requirement: Rejected approval attempts preserve invoice state

The system SHALL return a clear reason for a rejected approval attempt and SHALL leave invoice state and approval records unchanged.

#### Scenario: Approval is rejected

- **WHEN** an approval attempt is rejected because of authentication, authorization, amount, invoice state, approval order, or approver identity
- **THEN** the response explains the rejection and the invoice status, decision fields, and approval records remain unchanged
