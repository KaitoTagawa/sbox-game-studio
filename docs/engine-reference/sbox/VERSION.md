# s&box — Version Reference

| Field | Value |
|-------|-------|
| **Engine Version** | s&box 1.0 (rolling release, build pinned 2026-04-27) |
| **Release Date** | April 28, 2026 (1.0 official launch) |
| **Project Pinned** | 2026-04-27 |
| **Last Docs Verified** | 2026-04-27 |
| **LLM Knowledge Cutoff** | May 2025 |
| **Risk Level** | HIGH — engine launched after training cutoff |

## Knowledge Gap Warning

The LLM's training data ends in May 2025, almost a full year before s&box 1.0.
The engine's API stabilized over that period. Any of the following are likely
to be wrong without verification against current docs:

- Component lifecycle (`OnAwake`, `OnStart`, `OnUpdate`, `OnFixedUpdate`)
- Networking attributes (`[Sync]`, `[Broadcast]`, `[Authority]`, `[HostSync]`)
- Scene graph / GameObject API (Source 2 GameObject, distinct from Unity's)
- Razor UI syntax and lifecycle
- ShaderGraph node set and conventions
- ActionGraph (visual scripting) integration
- s&box's `Component` base class — DIFFERENT from Unity's `MonoBehaviour`
- Asset pipeline conventions (.vmdl, .vmat, .vsndevts, .scene, .object)
- Hotload behavior and what survives a hot reload vs. requires restart

## Verified Sources

- Official docs: https://sbox.game/dev/doc
- Source repo: https://github.com/Facepunch/sbox-public (MIT, since Nov 2025)
- Steam page: https://store.steampowered.com/app/590830/sbox/
- Wiki: https://wiki.facepunch.com/sbox/

## Required Workflow for Agents

Before any agent suggests an s&box API:

1. Verify the symbol exists in current docs (sbox.game/dev/doc)
2. If uncertain, use WebSearch to check
3. Never assume Unity/Godot patterns transfer — s&box has its own component model
4. Treat any code suggestion containing `MonoBehaviour`, `ScriptableObject`,
   `UnityEngine.*`, `Godot.*`, `Node`, `_Ready()`, etc. as a red flag —
   these are NOT s&box APIs

## 1.0 Launch Context (2026-04-27)

- **April 28, 2026**: official 1.0 launch
- **Late March 2026**: Valve licensing deal signed — standalone games shippable
  via Steam royalty-free
- **November 2025**: source code released under MIT license (engine code only;
  underlying Source 2 layer remains proprietary)
- **Built on**: heavily modified Source 2 + custom .NET runtime
- **Hotload system**: live code reloading without restart
- **Multiplayer**: first-class concern — Source 2 networking baked in
- **Scripting**: C# primary; ActionGraph for visual scripting; ShaderGraph for shaders

## Refresh Schedule

s&box is rolling-release with frequent updates. Recommended refresh cadence:

- After any major s&box update note
- Before starting a new system that touches networking, UI, or asset pipeline
- Quarterly minimum

Run `/setup-engine refresh` to update this file with new findings.
