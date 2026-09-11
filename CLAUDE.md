# Project Development Guidelines

Before making any changes to this project, read `README.md` in the repository root for the
project overview, architecture, and module map, and read and follow the applicable guidelines
in the `constitution` folder:

- `constitution/.NET-Core-API-Development-Guidelines.MD` for .NET Core API and backend development.
- `constitution/Angular-20-UI-Development-Guidelines.MD` for Angular UI and frontend development.

When a task spans both backend and frontend code, read and apply both guideline files. These
guidelines are required project conventions and should be followed together with the existing
codebase patterns and project configuration.

Beyond `README.md` and the applicable constitution file(s), only read or reference files that
are actually relevant to the task at hand — e.g. the specific module doc under `docs/` for the
layer being touched, and the source files being read or changed. Do not open unrelated modules,
docs, or source files just to build broader context unless the task genuinely requires it.

## Keeping documentation in sync

Every code change must leave the documentation for the module(s) it touches accurate. As part
of any code update:

- Identify the module(s) affected and check the corresponding doc under `docs/` (for example
  `docs/domain.md`, `docs/application.md`, `docs/infrastructure.md`, `docs/api.md`, or
  `docs/cross-cutting-concerns.md`) as well as `README.md` if the change affects the overall
  architecture or module map.
- If the change makes any statement in those docs outdated or incomplete — new/removed/renamed
  endpoints, models, services, modules, configuration, or behavior — update the doc in the same
  change, not as a follow-up.
- If no documentation exists yet for the affected behavior, add a concise entry to the relevant
  doc rather than leaving the change undocumented.
- If the change genuinely has no user-facing or architectural impact (e.g. a pure refactor with
  no behavior or structure change), it is fine to leave the docs untouched — but confirm this
  explicitly rather than skipping the check.
