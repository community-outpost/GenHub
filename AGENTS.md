# Instructions for AI Agents

## 1. Build Safety
- **NEVER** run `dotnet build` or `dotnet restore` directly.
- **ALWAYS** use `powershell -File scripts\build-check.ps1` (with `-Project` for single project checks).

## 2. Pull Request, CI Check & Review Safety
- **NEVER** push code changes before all ongoing CI checks and bot code reviews (Kilo Code Review, Greptile, CodeRabbit, Build workflows) have finished (`status == completed`).
- **ALWAYS** babysit and monitor CI runs whenever pushing code: run a monitoring process or timer until all check runs reach `status == completed`.
- **ALWAYS** wait for all check runs and automated reviewers to finish completely before taking action or addressing comments.
- **ALWAYS** address all review comments in a single consolidated pass, test with `build-check.ps1`, verify no active check runs, and only push when complete.

## 3. Code Style & Architecture
- Always use primary constructors when possible.
- Remove redundant instance fields when using primary constructors; use parameters directly.
- Prefer `IReadOnlyList<T>` over `IEnumerable<T>`.
- Adhere to the Result pattern (`docs/dev/result-pattern.md`) and constants pattern (`docs/dev/constants.md`).
- Never use inline namespaces; place usings at the top of the file.
- Never use `this.`.
