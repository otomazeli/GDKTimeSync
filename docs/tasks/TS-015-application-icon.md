# TS-015 — Application icon

## Status

Implemented. Documented retroactively from commit `aa54f59` (Task 1). No task brief, plan, or SDD review trail exists for this task, unlike TS-008–TS-012.

## Objective

Give the packaged desktop executable a real application icon instead of the default .NET icon.

## Task 1 — Add application icon (commit `aa54f59`)

### Scope

- Add `src/GDK.TimeSync.Desktop/Assets/GDK.TimeSync.ico`.
- Reference it from `GDK.TimeSync.Desktop.csproj` via `<ApplicationIcon>Assets\GDK.TimeSync.ico</ApplicationIcon>`.

### Safety boundaries

- Static asset and build-property change only; no runtime behavior, credential, or network path touched.

## Verification

- `ApplicationIconTests`: asserts the csproj declares the icon path and that the referenced `.ico` file exists and contains real image data (>100 bytes).

## Known gaps

- No task brief, plan, or `.superpowers/sdd` review history exists for TS-015. This document was reconstructed from the commit diff after the fact and has not been through the project's usual review/re-review cycle.
- The icon's provenance/licensing hasn't been confirmed in this pass — worth checking with whoever supplied `GDK.TimeSync.ico` (an untracked `GDK-TimeSync.png` sits in the repo root on the main worktree, which may be the source) before it ships in a public release build.
