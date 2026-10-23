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

The system SHALL require two approvals from distinct users before an invoice above 500,000 is approved, and the second approver SHALL be a Director.

#### Scenario: Distinct Director completes high-value approval

- **WHEN** a Director different from the first approver provides the second approval
- **THEN** the invoice is approved

#### Scenario: First approver attempts the second approval

- **WHEN** the first approver attempts to count as the second approver
- **THEN** the attempt is rejected with a clear reason and invoice state remains unchanged

#### Scenario: Non-Director attempts the second approval

- **WHEN** a user who is not a Director attempts the second approval
- **THEN** the attempt is rejected with a clear reason and invoice state remains unchanged

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
