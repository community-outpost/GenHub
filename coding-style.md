# GenHub C# Coding Style Guide

This document outlines the coding conventions for the GenHub C# project.
The goal is to ensure consistency, readability, and maintainability across the codebase.
These conventions are based on the [Microsoft C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions),
with selected practices from the [CoreFX C#](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)
and [Google C#](https://google.github.io/styleguide/csharp-style.html) style guides,
and additional project-specific preferences aligned with StyleCop, DeepSource, and SonarCloud.

---

## 1. General Principles

- **Consistency**: Follow these conventions throughout the codebase.
- **Readability**: Prioritize code clarity and maintainability.
- **Common Sense**: Use good judgment; code reviews will enforce readability and style.
- **Pre-Emptive Hygiene**: Fix analyzer warnings and naming conventions locally before opening a pull request.

---

## 2. Indentation and Formatting

- **Indentation**: Use 4 spaces per indentation level (no tabs).
- **Line Length**: No enforced maximum; use common sense for readability.
- **Braces:** Use Allman braces style (opening brace on their own line with same indentation as the declaration).
    Single line statements (e.g. if, for, while) may omit braces.
- **Blank Lines**: Use single blank lines to separate logical sections of code.
- **Whitespace**:
  - Use spaces around binary operators (e.g., `a + b`).
  - No spaces after method names before parentheses (e.g., `MethodName()`).
  - No spaces before commas, semicolons, or colons.
- **Comments**:
  - Use `//` for single-line comments.
  - Avoid the use of `/**/` block comments.
  - Place comments on their own line above the code they describe, not at the end of a line.
	
---

## 3. Naming Conventions

- **Namespaces**: PascalCase (e.g., `GenHub.Core`).
- **Classes, Structs, Enums, Delegates**: PascalCase.
- **Methods, Properties, Events**: PascalCase.
- **Async Methods**: Methods returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` must have the `Async` suffix (e.g., `DownloadFileAsync`, `ComputeHashAsync`).
- **Interfaces**: Prefix with `I`, PascalCase (e.g., `IService`).
- **Fields**:
  - `private` and `internal`: `_camelCase` (prefix with underscore).
  - `public` and `protected`: PascalCase.
- **Local Variables and Parameters**: camelCase.
- **Constants**: PascalCase.

---

## 4. Ordering

- **Class Member Order** (strict, as per Google/CoreFX/StyleCop):
  1. Nested types
  2. Static fields
  3. Instance fields
  4. Constructors
  5. Finalizers
  6. Properties
  7. Indexers
  8. Events
  9. Methods
     1. Static methods go first, then instance methods.
     2. Methods should be ordered by visibility: `public`, `protected`, `internal`, `private`.

- **Using Directives**:
  - Alphabetical order (no special treatment for `System` namespaces).
  - Place outside the namespace declaration.

---

## 5. Language Features

- **LINQ**: Prefer the fluent (method chain) syntax over query expressions.
- **Primary Constructors**: Use primary constructors and access parameters directly when possible.
- **Simple Using Declarations**: Prefer `using var x = ...;` statements over nested `using (var x = ...) { ... }` blocks to prevent excessive indentation.
- **Expression-Bodied Lambdas**: Write single-expression lambdas concisely (`x => x.Value`) rather than block bodies (`x => { return x.Value; }`).
- **Conditional Expressions**: Avoid deeply nested or multi-level ternary expressions (`? :`). Use `if / else` blocks, switch expressions, or pattern matching.
- **Nested Conditionals**: Merge adjacent nested `if` statements without `else` blocks (`if (a && b)`).
- **Nullable Reference Types**: Enable and use explicit nullable annotations.

---

## 6. Additional Guidelines

- **Comments**: Use XML documentation comments for all public and protected classes, interfaces, properties and methods.
- **File Structure**: One top-level type per file.
- **Error Handling**:
  - Use exceptions appropriately; avoid empty catch blocks.
  - Catch specific domain exceptions (`IOException`, `HttpRequestException`, `JsonException`) instead of generic `System.Exception`.
  - For predictable domain failures, prefer `OperationResult<T>` over throwing exceptions for control flow.
- **Time and Dates**: Always use `DateTime.UtcNow` or `DateTimeOffset.UtcNow` for timestamps, file manifests, and metrics. Never use local `DateTime.Now`.
- **Concurrency & Locking**: Never lock on `this`, `typeof(...)`, or string literals. Use a dedicated `private readonly object _syncLock = new();` or asynchronous synchronization primitives like `SemaphoreSlim`.
- **Cancellation**: Long-running or asynchronous operations must accept and propagate a `CancellationToken`.
- **User Notifications & Feedback**:
  - Always inject `INotificationService` and dispatch toast notifications (`ShowSuccess`, `ShowInfo`, `ShowWarning`, `ShowError`) for user actions, operation outcomes, warnings, and errors.
  - Never introduce one-off status labels, status textblocks, or `StatusMessage` / `StatusText` UI properties to display action results. Status labels are an anti-pattern.
  - Use `NotificationDurations` constants (`Short`, `Medium`, `Long`, `VeryLong`, `Critical`) for timeout durations.

---

## 7. Tooling

- Enforced locally via [`.editorconfig`](.editorconfig) and [`stylecop.json`](stylecop.json) in the repository root.
- Use Visual Studio's default formatting (__Edit > Advanced > Format Document__) or `dotnet format`.
- Adhering to these conventions ensures that local builds stay clean and cloud bots (DeepSource, SonarCloud, CodeRabbit) pass without review delays.

---

Adhering to these conventions will help keep the GenHub codebase clean, consistent, and easy to maintain.
