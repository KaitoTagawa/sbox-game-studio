# Technical Preferences

<!-- Populated by /setup-engine. Updated as the user makes decisions throughout development. -->
<!-- All agents reference this file for project-specific standards and conventions. -->

## Engine & Language

- **Engine**: s&box (Source 2 + .NET, MIT-licensed since Nov 2025)
- **Language**: C# (.NET 8+)
- **Rendering**: Source 2 renderer
- **Physics**: Source 2 / Rubikon physics

## Input & Platform

<!-- Written by /setup-engine. Read by /ux-design, /ux-review, /test-setup, /team-ui, and /dev-story -->
<!-- to scope interaction specs, test helpers, and implementation to the correct input methods. -->

- **Target Platforms**: PC (Steam, standalone via Valve license, Linux supported)
- **Input Methods**: Keyboard/Mouse, Gamepad
- **Primary Input**: Keyboard/Mouse (tycoon UI-driven)
- **Gamepad Support**: Partial (recommended for menu navigation; not required for core loop)
- **Touch Support**: None
- **Platform Notes**: s&box is PC-only currently. `game2.sbproj` has
  `GameNetworkType: "Multiplayer"` set to default boilerplate, but design intent
  is single-player — consider reverting to single-player config in a future cleanup.

## Naming Conventions

- **Classes**: PascalCase (e.g., `Employee`, `GameProjectManager`)
- **Public properties**: PascalCase, `[Property]` attribute for editor exposure
- **Private fields**: `_camelCase` (e.g., `_currentState`)
- **Methods**: PascalCase
- **Files**: PascalCase matching class (e.g., `Employee.cs`)
- **Scenes/Prefabs**: PascalCase matching root GameObject
- **Constants**: PascalCase
- **s&box-specific**: Components inherit from `Component`; networked properties
  use `[Sync]`; RPCs use `[Broadcast]`/`[Authority]`/`[HostSync]`

## Performance Budgets

- **Target Framerate**: 60fps
- **Frame Budget**: 16.6ms
- **Draw Calls**: <2000 (Source 2 handles batching well)
- **Memory Ceiling**: 4GB managed heap, 8GB total

## Testing

- **Framework**: [TO BE CONFIGURED — s&box has limited test support; investigate via /test-setup]
- **Minimum Coverage**: 50% for logic systems (tycoon formulas, employee state machines)
- **Required Tests**: Balance formulas, save/load roundtrip, employee state transitions

## Forbidden Patterns

<!-- Add patterns that should never appear in this project's codebase -->
- [None configured yet — add as architectural decisions are made]

## Allowed Libraries / Addons

<!-- Add approved third-party dependencies here -->
- [None configured yet — add as dependencies are approved]

## Architecture Decisions Log

<!-- Quick reference linking to full ADRs in docs/architecture/ -->
- [No ADRs yet — use /architecture-decision to create one]

## Engine Specialists

<!-- Written by /setup-engine when engine is configured. -->
<!-- Read by /code-review, /architecture-decision, /architecture-review, and team skills -->
<!-- to know which specialist to spawn for engine-specific validation. -->

- **Primary**: general-purpose (no `sbox-specialist` agent exists)
- **Language/Code Specialist**: general-purpose with C# expertise
  (closest reference: `godot-csharp-specialist` for general C# patterns, but
  be aware those patterns are NOT directly applicable to s&box's component model)
- **Shader Specialist**: general-purpose (s&box uses Source 2 shaders + ShaderGraph)
- **UI Specialist**: general-purpose (s&box uses Razor for UI, not standard XAML/UMG/UXML)
- **Additional Specialists**: None — s&box-specific concerns require manual review
- **Routing Notes**: When working on s&box code, agents MUST consult
  https://sbox.game/dev/doc before suggesting APIs. The LLM's training data
  predates s&box 1.0 (April 2026), so any Component, Scene, or networking
  pattern not verified against the docs is suspect. Cross-reference
  `docs/engine-reference/sbox/VERSION.md` for the knowledge gap warning list.

### File Extension Routing

<!-- Skills use this table to select the right specialist per file type. -->
<!-- If a row says [TO BE CONFIGURED], fall back to Primary for that file type. -->

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (.cs files) | general-purpose (verify against s&box docs) |
| Shader / material files (.shader, .vmat, ShaderGraph) | general-purpose |
| UI / screen files (.razor, .scss) | general-purpose |
| Scene / prefab / level files (.scene, .object, .vmap) | general-purpose |
| Project config (.sbproj, .csproj) | general-purpose |
| General architecture review | general-purpose |
