# Claude Code Game Studios -- Game Studio Agent Architecture

Indie game development managed through 48 coordinated Claude Code subagents.
Each agent owns a specific domain, enforcing separation of concerns and quality.

## Technology Stack

- **Engine**: s&box (Source 2 + .NET) — version pinned 2026-04-27 (1.0 launch)
- **Language**: C# (.NET, primary)
- **Version Control**: Git with trunk-based development
- **Build System**: s&box editor build system + dotnet
- **Asset Pipeline**: s&box content/asset system (Source 2 .vmdl, .vmat, .vsndevts, etc.)

> **Note**: s&box is NOT one of the template's natively supported engines
> (Godot/Unity/Unreal). No dedicated `sbox-specialist` agent exists. C# code
> review uses general-purpose agents; engine-specific patterns require manual
> verification against https://sbox.game/dev/doc.

## Project Structure

@.claude/docs/directory-structure.md

## Engine Version Reference

@docs/engine-reference/sbox/VERSION.md

## Technical Preferences

@.claude/docs/technical-preferences.md

## Coordination Rules

@.claude/docs/coordination-rules.md

## Collaboration Protocol

**User-driven collaboration, not autonomous execution.**
Every task follows: **Question -> Options -> Decision -> Draft -> Approval**

- Agents MUST ask "May I write this to [filepath]?" before using Write/Edit tools
- Agents MUST show drafts or summaries before requesting approval
- Multi-file changes require explicit approval for the full changeset
- No commits without user instruction

See `docs/COLLABORATIVE-DESIGN-PRINCIPLE.md` for full protocol and examples.

> **First session?** If the project has no engine configured and no game concept,
> run `/start` to begin the guided onboarding flow.

## Coding Standards

@.claude/docs/coding-standards.md

## Context Management

@.claude/docs/context-management.md
