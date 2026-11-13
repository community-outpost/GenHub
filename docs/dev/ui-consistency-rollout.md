---
title: UI consistency rollout
description: Incremental adoption of shared GenHub action controls and icon conventions
---

# UI consistency rollout

## Objective and baseline

Equivalent actions should look and behave consistently across GenHub. Preserve
command behavior, localization, theme switching, keyboard operation and platform
window integration while removing independent copies of button presentation.

Baseline: development commit `82f696500`. The project already has a mandatory
[UI guide](./ui-styling.md), semantic theme resources, Material icons, shared
filter chips, window chrome, editor shells and tool action bars. Build on these.
The guide's ordinary action classes were documented but not globally implemented.

## Review units

Each migration depends on the foundation. Keep the foundation first and review
the migrations separately; a temporary stack may be retargeted to development
once its prerequisite merges. No production navigation entry is added for the
developer showcase.

| Unit | Scope | Completion criteria |
|---|---|---|
| 1. Shared action components | ButtonStyles, dimension/contrast tokens, guide, developer showcase, state tests | Actual Fluent template responds to hover, pressed, disabled and keyboard focus; all shipped accent palettes readable; live theme changes propagate |
| 2. Common dialogs | Generic Message, Confirmation, Update Option | Shared primary/secondary/danger actions; dynamic action roles and commands preserved; retain any diagnostics changes from concurrent PRs |
| 3. Settings and subscription dialogs | Settings ordinary actions, Import Subscription, Subscription Confirmation | Browse, replacement, delete and close actions share roles; theme cards, filters and navigation preserved |
| 4. Tool action bars | Map Manager, Replay Manager, Texture Editor | Shared ordinary/compact actions and icon buttons; retain split action corners, checked state, rotation, panning and specialized controls |

These four units are the first implementation batch. Their boundary is action
controls, not a redesign of every screen. Track the following additional batches
explicitly; do not describe the entire application as migrated after this batch.

## Subsequent migration batches

1. **Launcher and profile flows:** Launcher, profile cards, settings/content
   editors, setup wizard, import/share and add-local-content. Integrate the active
   card/wizard/scan/profile-label PRs first. Common actions adopt roles; hero Play
   remains an intentional component family with its own size and emphasis.
2. **Downloads:** browser toolbar, content cards/detail, filter panel and profile
   selection. Preserve the existing shared filter chips and selected states.
3. **Publisher Studio:** studio navigation, libraries, hosting/storage,
   publishing/sharing, profile/referrals and authoring dialogs. Integrate current
   content-dialog changes before migrating those controls.
4. **Remaining tools, Info and Notifications:** GenHotkeys (after its active PR),
   ModBuilder/dashboard, Info actions and notification actions/dismissal. Keep
   hotkey keys and media transport as intentional specialized components.
5. **Other component families:** consolidate any remaining repeated navigation,
   selectable card and form patterns after comparing real consumers. Define one
   family at a time; do not replace them with ordinary action-button styles.

## Design decisions

- Opt-in styles on Avalonia Button, no additional widget framework or icon package.
- Solid primary/danger surfaces with dark text; support every current accent.
- Default 36px and compact 32px minimum height; content may grow for translation.
- Shared 8px corners and 16px action glyphs, with 20/24px section/navigation tokens.
- Common semantic geometry resources first; inherit action foreground.
- Localized accessible name plus tooltip for icon-only actions.
- Keep branding, faction identity and purposeful component roles distinct.
- Preserve existing command, parameter, enabled/visible/default/cancel bindings,
  key bindings, action order and window behavior during style-only migration.

## Verification and evidence

For each unit, review its complete final diff and trace shared consumers. Build
affected projects. Run the action-state tests and relevant consumer tests with
native-engine and live-network tests excluded. Verify selected test count and
exit status. Include a regression case for new shared behavior rather than
snapshotting implementation details.

Capture the gallery using production resources; also inspect migrated dialogs,
Settings and dense toolbars. Include disabled, focus, hover/pressed, palette
changes, long Russian/Arabic captions and narrow layouts. A headless screenshot
is labeled as such and is not evidence of native platform behavior. Native
Windows/Linux/macOS visual checks remain a release/merge verification item when
those platforms are available.

## Publishing and overlap

Prepare local commits, exact PR descriptions and screenshots before requesting
external-write approval. No push, PR creation, comment or description update is
implicit in this plan. Re-check current base and active PRs immediately before
publication; preserve concurrent functional changes when rebasing. Generic
Message overlaps dependency-validation PRs; launcher/card/wizard and Studio work
also have active owners. Never edit those other worktrees as part of this rollout.
