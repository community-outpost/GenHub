# Contributor Guidelines

This document outlines the standards and patterns for contributing to the GenHub documentation system.

## Documentation Structure

GenHub uses **VitePress** for documentation. The structure is organized as follows:

- `/docs`: Root directory for all documentation.
- `/docs/dev`: Technical documentation for developers (constants, models, converters).
- `/docs/features`: Detailed descriptions of application features.
- `/docs/FlowCharts`: Mermaid-based flowcharts representing system logic.
- `/docs/.vitepress/config.js`: Central configuration for the sidebar and navigation.

## Standardized Patterns

### Constants Documentation

When adding new constants to the codebase:

1. Update the corresponding C# class in `GenHub.Core/Constants`.
2. Update `docs/dev/constants.md` by adding a new `## ClassName Class` section.
3. Use markdown tables for listing constants and their values/descriptions.

### Model Documentation

When adding or modifying data models:

1. Update `docs/dev/models.md`.
2. Include C# record/class snippets for clarity.
3. Explain the *Purpose* of the model if it's not immediately obvious.

### Mermaid Flowcharts

Flowcharts use the `vitepress-plugin-mermaid`.

- Themes are customized in `.vitepress/config.js`.
- Use `graph TD` for top-down logic.
- Maintain consistent `classDef` styles for Orchestrators, Providers, and Components.

## Maintenance Guidelines

1. **Scope of Changes**: Always ensure that documentation updates match the scope of code changes (e.g., if a new provider is added, document it in both `features/` and `dev/` sections).
2. **Cross-Referencing**: Link to other parts of the documentation using relative links (e.g., `[Content Pipeline](../features/content.md)`).
3. **Diagram Updates**: If logic flows change (e.g., adding a new step to content resolution), update the corresponding mermaid diagram in `docs/FlowCharts/`.
