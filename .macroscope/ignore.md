# Macroscope Ignore Rules
# Note: Defining this file replaces Macroscope's built-in default ignores.
# Reference: https://docs.macroscope.com/bug-detection-and-fixes#excluding-files-with-macroscope-ignore-md

# Localization & Resource files
*.resx

# Static web and landing page showcase
Landing-page/**

# Package manager manifests & lockfiles
**/package.json
**/package-lock.json
**/pnpm-lock.yaml
**/yarn.lock
**/bun.lockb
**/go.mod
**/go.sum
**/pom.xml
*.lock

# Build outputs & caches
**/bin/**
**/obj/**
**/dist/**
**/build/**
**/target/**
**/.vs/**
**/TestResults/**
*.log

# Dependencies, virtual environments & runtime caches
**/node_modules/**
**/vendor/**
**/.pnpm-store/**
**/.git/**
**/__pycache__/**
**/.pytest_cache/**
**/.mypy_cache/**
**/.ruff_cache/**
**/venv/**
**/.venv/**
**/__snapshots__/**
**/__Snapshots__/**

# Generated code, bundles & sourcemaps
**/*.min.js
**/*.min.css
**/*.bundle.js
**/*.js.map
**/*.d.ts
**/*.designer.cs
**/*.gen.*
**/generated/**
