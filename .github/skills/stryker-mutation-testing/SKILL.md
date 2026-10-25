---
name: stryker-mutation-testing
description: Run and analyze project-scoped Stryker.NET mutation tests for the .NET invoice API. Use when establishing or improving mutation coverage or reviewing a mutation report.
---

# Stryker.NET Mutation Testing

## Inputs

- API project path and test project path.
- Previous Stryker report for a comparable baseline, if available.
- Current source and relevant test files.

## Procedure

1. Inspect `AGENTS.md`, project files, Stryker configuration/package/version, source, tests, and prior report.
2. Confirm installed Stryker and .NET versions. For this repository, the expected invocation is:
   `dotnet stryker --project backend/src/InvoiceApp.Api/InvoiceApp.Api.csproj --test-project backend/tests/InvoiceApp.Tests/InvoiceApp.Tests.csproj`
3. Inspect HTML report mutant records for `Survived`, `NoCoverage`, `CompileError`, and `Ignored`; distinguish tested denominator from all created mutants.
4. Map each meaningful mutant to behavior and propose the smallest test that observes the mutation. Treat compile-error mutants as tool-generated invalid variants unless evidence shows otherwise.
5. Never alter production behavior, filters, exclusions, or thresholds to game score. Do not add a brittle assertion for an internal detail when a behavior-level assertion suffices.
6. Run `dotnet test backend/InvoiceApp.sln` before the mutation campaign and after test edits.

## Output

Report exact command/version, comparable baseline and current score, percentage-point change, counts by status, killed/surviving mutant details, tests expected to kill each survivor, remaining gaps, warnings, and limitations. Only report observed results from actual runs.
