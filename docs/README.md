# GenHub Documentation

This directory contains the VitePress documentation site for GenHub.

## Development

1. Install dependencies (from the repository root):
   ```bash
   pnpm install
   ```

2. Start development server:
   ```bash
   pnpm run dev
   ```

3. Build for production:
   ```bash
   pnpm run build
   ```

## Deployment

The documentation is automatically deployed to GitHub Pages when changes are pushed to the `architecture` branch.

## Adding Content

- Add new pages as `.md` files in the `docs` directory
- Update the sidebar configuration in `.vitepress/config.js`
- Use Mermaid diagrams by wrapping code blocks with `mermaid` language identifier
