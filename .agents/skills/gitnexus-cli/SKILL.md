---
name: gitnexus-cli
description: "Use when the user needs to run GitNexus CLI commands like analyze/index a repo, check status, clean the index, generate a wiki, or list indexed repos. Examples: \"Index this repo\", \"Reanalyze the codebase\", \"Generate a wiki\""
---

# GitNexus CLI Commands

In this repository, GitNexus is locked via `package.json` / `pnpm-lock.yaml` and executed via `pnpm exec gitnexus`. (Alternatively, `npx -y gitnexus@1.6.9` can be used outside a pnpm environment).

## Commands

### analyze — Build or refresh the index

```bash
pnpm exec gitnexus analyze
```

Run from the project root. This parses all source files, builds the knowledge graph, writes it to `.gitnexus/`, and generates AGENTS.md / AGENTS.md context files.

| Flag           | Effect                                                           |
| -------------- | ---------------------------------------------------------------- |
| `--force`      | Force full re-index even if up to date                           |
| `--index-only` | Build graph without regenerating context files                   |
| `--embeddings` | Enable embedding generation for semantic search (off by default) |

**When to run:** First time in a project, after major code changes, or when `gitnexus://repo/{name}/context` reports the index is stale. In Codex, a PostToolUse hook runs `analyze` automatically after `git commit` and `git merge`, preserving embeddings if previously generated.

### status — Check index freshness

```bash
pnpm exec gitnexus status
```

Shows whether the current repo has a GitNexus index, when it was last updated, and symbol/relationship counts. Use this to check if re-indexing is needed.

### detect-changes — Impact analysis for git changes

```bash
# Map staged changes against execution flows (pre-commit check)
pnpm exec gitnexus detect-changes --scope staged

# Map full branch diff against target base branch (PR validation)
pnpm exec gitnexus detect-changes --scope compare --base-ref origin/development
```

| Flag                    | Effect                                              |
| ----------------------- | --------------------------------------------------- |
| `--scope staged`        | Analyze staged git changes (recommended pre-commit) |
| `--scope compare`       | Compare current branch against `--base-ref`         |
| `--base-ref <ref>`      | Base reference branch or SHA to compare against     |
| `--scope working`       | Analyze unstaged working tree changes (default)     |

### clean — Delete the index

```bash
pnpm exec gitnexus clean
```

Deletes the `.gitnexus/` directory and unregisters the repo from the global registry. Use before re-indexing if the index is corrupt or after removing GitNexus from a project.

| Flag      | Effect                                            |
| --------- | ------------------------------------------------- |
| `--force` | Skip confirmation prompt                          |
| `--all`   | Clean all indexed repos, not just the current one |

### wiki — Generate documentation from the graph

```bash
pnpm exec gitnexus wiki
```

Generates repository documentation from the knowledge graph using an LLM. Requires an API key (saved to `~/.gitnexus/config.json` on first use).

| Flag                | Effect                                    |
| ------------------- | ----------------------------------------- |
| `--force`           | Force full regeneration                   |
| `--model <model>`   | LLM model (default: minimax/minimax-m2.5) |
| `--base-url <url>`  | LLM API base URL                          |
| `--api-key <key>`   | LLM API key                               |
| `--concurrency <n>` | Parallel LLM calls (default: 3)           |
| `--gist`            | Publish wiki as a public GitHub Gist      |

### list — Show all indexed repos

```bash
pnpm exec gitnexus list
```

Lists all repositories registered in `~/.gitnexus/registry.json`. The MCP `list_repos` tool provides the same information.

## After Indexing

1. **Read `gitnexus://repo/{name}/context`** to verify the index loaded
2. Use the other GitNexus skills (`exploring`, `debugging`, `impact-analysis`, `refactoring`) for your task

## Troubleshooting

- **"Not inside a git repository"**: Run from a directory inside a git repo
- **Index is stale after re-analyzing**: Restart Codex to reload the MCP server
- **Embeddings slow**: Omit `--embeddings` (it's off by default) or set `OPENAI_API_KEY` for faster API-based embedding
