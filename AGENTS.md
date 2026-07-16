# AIThon Unity Project Agent Guide

This guide applies to every agent in this repository unless a child folder has a more specific AGENTS.md.

## Project

- Product: production-ready 2D active-ragdoll physics sandbox.
- Unity: **2022.3.62f2 LTS**. Do not upgrade Unity or packages unless explicitly requested.
- Primary scene: `Assets/GameData/Scene/RagdollSandbox.unity`.
- Main namespace: `KickTheBuddy`.
- Physics: Physics2D with Rigidbody2D, Collider2D, HingeJoint2D, and TargetJoint2D.
- UI: uGUI/TextMeshPro where already used.
- Optimize for mobile, safe serialization, low allocation, and Inspector-driven tuning.

## Required hierarchy

Keep authored content in:

1. `Assets/GameData/Scripts`
2. `Assets/GameData/Audios`
3. `Assets/GameData/Scene`
4. `Assets/GameData/Animations`
5. `Assets/GameData/UI`
6. `Assets/GameData/Materials`
7. `Assets/GameData/Prefabs`

Feature folders under Scripts:

- `Ragdoll`: physics, profiles, input receivers, damage, elements, breakable limbs.
- `Gameplay`: flow, levels, saving, HUD, scoring, bootstrap.
- `Audio`: core contracts, catalog, pooling, playback.
- `Haptics`: profiles, platform driver, manager, gameplay adapter.
- `VFX`: hit/damage/death effects and particle pooling.
- `Editor`: editor-only setup/authoring/validation. Never reference UnityEditor in runtime code.

Always move Unity assets with their .meta files and preserve GUIDs.

## Agent roles

- Unity gameplay/architecture: owns gameplay, Physics2D, input, scenes, ScriptableObjects, persistence, UI flow, editor tools, performance, and tests. Use `unity-developer`.
- Unity VFX: use `unity-vfx-engineer` for substantial particles, shaders, dissolve, explosions, trails, VFX pooling, or overdraw work. Gameplay owns event contracts; VFX only consumes them.
- Audio: use the existing `Scripts/Audio` system and `AudioCatalog`. Avoid scattered direct AudioSource calls.
- UI/game flow: keep transitions in `GameplayManager` and presentation in `GameplayHUD`. UI buttons call public commands and never directly mutate health/save/level state.
- QA: EditMode for deterministic rules; PlayMode for physics, lifecycle, input, scenes, KO/death, and UI. Report only checks actually run.

Do not create parallel agents unless the user explicitly requests delegation or an active skill requires a specialist.

## Ownership contracts

- `RagdollController`: thin public facade and compatibility events only; it performs no physics, profile, input, state, animation, or damage calculations.
- `RagdollRigController2D`: body/joint/collider discovery, component wiring, authored defaults, and low-level rig access.
- `RagdollProfileController2D`: category selection and all mass, gravity, drag, material, joint-limit, and durability profile calculations.
- `RagdollPoseController2D`: standing, balance, fall detection, motor control, and get-up calculations.
- `RagdollStateController2D`: active/frozen/limp/knockout/revive transitions and timers.
- `RagdollDamageManager`: sole damage authority and weighted aggregate of all `RagdollPartHealth` components; critical head depletion causes death.
- `RagdollAnimationController`: presentation-only idle, drag, facial, damage, knockout, and death reactions; it never applies physics movement.
- `RagdollInputManager`: sole owner of raw mouse/touch selection and drag lifecycle. Limbs/controllers do not independently poll input.
- `DamageReceiver2D`: local hit and drag contracts. Damage belongs to the body part actually hit.
- `RagdollPartHealth`, `DismemberableLimb`, `RagdollBreakableLimb`: integrity and severing.
- `RagdollProfile`: authored physics/drag feel; never mutable shared runtime state.
- `GameplayManager`: game state and completion.
- `GameSaveManager`: validated save serialization.
- `LevelsManager`/`LevelCatalog`: sequence/progression.
- `GameplayHUD`: presentation and user commands.
- Audio, haptics, and VFX consume events and are not gameplay authorities.

Prefer explicit serialized references and controlled bootstrap wiring. Avoid new service locators, broad static event buses, and casual singletons.

## Critical gameplay invariants

- A healthy active character stands idle without dancing or self-propelling.
- Self-righting acts only while fallen/grounded after the configured delay.
- Pause self-righting and active pose control while grabbed; hang from the selected point.
- Release retains only physics-generated drag velocity; never add locomotion.
- Drag flexibility, frequency, damping, max force, and elastic limits remain Inspector/profile tunable and smooth.
- Read input in Update; apply forces, torque, motors, and physics targets in FixedUpdate.
- Dragging alone never damages or severs limbs. Damage requires legitimate external collision/attack.
- Ignore minor scuffs. Impact damage affects the actual hit limb only.
- Temporary knockout is not death and does not complete a level.
- True death is `CurrentHealth <= 0`: release drag, disable input, enter LevelComplete, and show the Level Complete popup.
- Starting/restarting re-enables input and resets result UI.
- Fire transitions once. Prevent duplicate KO, death, sever, completion, save, audio, haptic, and VFX events.
- Preserve attached child chains after parent severing where designed.

## C# and SOLID

- Apply SOLID pragmatically: clear ownership, focused interfaces, composition, meaningful dependency inversion.
- One primary type per file; filename matches.
- Prefer `[SerializeField] private` plus read-only properties or commands.
- Preserve serialized field names; use `FormerlySerializedAs` when renaming.
- Cache local components in Awake; pair subscriptions in OnEnable/OnDisable.
- Treat Inspector references as nullable and fail with actionable messages outside hot loops.
- Publishers alone invoke events; always unsubscribe.
- Avoid LINQ, allocations, object searches, formatting, and logs in hot paths.
- Pool repeated particles, damage text, explosions, and concurrent audio sources with explicit reset/return behavior.
- Do not add dependencies when Unity/current code already supplies the capability.

## Scene/editor rules

- Prefer Editor APIs and existing setup tools over manual scene/prefab YAML edits.
- Mutating editor tools use Undo, validation, dirty flags, and intentional saves.
- Inspect diffs after scene saves for unintended serialization.
- Never run two Unity instances on this project. Close interactive Unity before batch mutation.
- Do not upgrade packages, render pipeline, input system, or scripting backend unless requested.

## Mandatory backups

Before every mutating task:

1. Create `D:/AI Hackathon GD/Ragdoll2DAithon/Backups/AIThon_Pre<Feature>_yyyyMMdd-HHmmss`.
2. Copy `Assets`, `Packages`, and `ProjectSettings`.

After implementation and successful verification:

1. Create `AIThon_<Feature>Complete_Source_yyyyMMdd-HHmmss` with the same folders.
2. Report both paths.

Never back up Library, Temp, Logs, Obj, IDE caches, or builds. They are generated and may exceed Windows path limits. A partial backup does not count.

## Safety and workflow

- Inspect git status and preserve all user/unrelated changes.
- Never use git reset --hard, destructive checkout, mass deletion, or broad reserialization.
- Do not commit unless requested.
- Extend/fix the current controller; do not rewrite it unless explicitly authorized.
- Search call sites, scenes, prefabs, and tools before changing APIs/serialized data.
- Keep credentials and personal paths out of runtime assets.

For every task:

1. Read this file and deeper AGENTS.md files.
2. Inspect the narrow code path and git status.
3. For bugs, establish expected/actual behavior and first meaningful error.
4. Create the pre-change source backup.
5. Implement the smallest complete root-cause fix.
6. Review code and serialized diffs.
7. Compile with Unity 2022.3.62f2.
8. Run focused tests or a deterministic scene smoke test proportional to risk.
9. Confirm no new Console errors/warnings.
10. Create the verified completion source backup.
11. Reopen Unity when the user expects it open.
12. Report outcome, files, checks, backups, and real limitations.

## Definition of done

A task is complete only when scene/prefab references and GUIDs are preserved, it compiles in the installed Unity version, input/physics/state ownership is respected, lifecycle/null cases are handled, unrelated changes are untouched, verification is credible, and both source backups exist. Gameplay changes must also check pause, restart, knockout, death, and completion and prevent duplicate input, save, audio, haptic, UI, VFX, or physics side effects.
