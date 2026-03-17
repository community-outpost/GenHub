# GenHub C# Coding Style Guide

This document outlines the coding conventions for the GenHub C# project.
The goal is to ensure consistency, readability, and maintainability across the codebase.
These conventions are based on the [Microsoft C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions),
with selected practices from the [CoreFX C#](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)
and [Google C#](https://google.github.io/styleguide/csharp-style.html) style guides,
and additional project-specific preferences.

---

## 1. General Principles

- **Consistency**: Follow these conventions throughout the codebase.
- **Readability**: Prioritize code clarity and maintainability.
- **Common Sense**: Use good judgment; code reviews will enforce readability and style.

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
- **Nullable Reference Types**: Enable and use explicit nullable annotations.

---

## 6. Additional Guidelines

- **Comments**: Use XML documentation comments for all public and protected classes, interfaces, properties and methods.
- **File Structure**: One top-level type per file.
- **Error Handling**: Use exceptions appropriately; avoid empty catch blocks.

---

## 7. Tooling

- Use Visual Studio's default formatting (**Edit > Advanced > Format Document**).
- StyleCop and similar analyzers are used to enforce ordering and style.
- Enable nullable reference types in project settings.

---

Adhering to these conventions will help keep the GenHub codebase clean, consistent, and easy to maintain.
