# Acrophobia Trainer — handover

Written 2026-10-21. Everything an agent or teammate needs to pick this up cold.

---

## 1. Where the work actually is

| Location | Contains |
|---|---|
| **local `main`** (your laptop) | 66 commits — all weekend work. **The only copy.** |
| `backup/pre-lfs-export` (local) | Pre-history-rewrite snapshot, `4d51c2b`. Safety net. |
| `fork/main` on GitHub | Friday's code + 3 teammate commits. **Does not have the weekend work.** |
| `fork/jay-weekend` | Being pushed in slices at time of writing. May be incomplete. |

Remote: `https://github.com/gandhialt/Acrophobia-Trainer`

**Two histories exist and they do not fast-forward onto each other.** Local `main` was
rewritten by `git lfs migrate export`, so every SHA differs from the remote's. The
teammate (`saturated-hombre`) pushed three real commits on 2026-10-20:

```
c0d4221  feat(telemetry): Quest bio-adaptive telemetry, SUDS integration, export pipeline
61e5e18  chore: purge redundant Fruit Ninja assets, food kit, template boilerplate
8480542  chore: clean up orphaned .meta files from removed assets
```

**Never force-push to `main`.** It would destroy those three. The outstanding job is to
rebase local `main` onto `fork/main`, or merge the two.

---

## 2. Build configuration

Verified 2026-10-19. All correct for Quest already:

```
bundleId      com.arvrcoursework.acrophobiatrainer
minSdk 32   targetSdk 34   IL2CPP   ARM64   Linear colour
graphicsAPIs  Vulkan, OpenGLES3
XR            OpenXR + MetaQuest feature, configured for Android AND Standalone
keystore      none (debug keystore is fine for MQDH sideload)
Unity         6000.5.4f1 — Android build support IS installed
```

**Active build target is still `StandaloneWindows64`.** Switching to Android is the
remaining step and it is the dangerous one — see §5.

Scenes in build settings, in this order:

```
0  Assets/Scenes/Acrophobia Exposure Therapy/Exposure_Therapy.unity      (core, never unloads)
1  .../Environments/Env_Tier2_Office.unity
2  .../Environments/Env_Tier3_UnfinishedFloor.unity
3  .../Environments/Env_Tier4_Stairwell.unity
4  .../Environments/Env_Tier5_Rooftop.unity
```

---

## 3. How the tier system works

The core scene never unloads. It holds the FSM, the CSV writer, SUDS history, narrator,
player rig and the city. Each tier's environment is a **separate scene loaded additively**
during a blackout, so nothing with state has to survive a scene load.

```
tier 1   3 m    apartment       in the CORE scene (not a separate scene)
tier 2  12 m    office          Env_Tier2_Office        Tower_32  x  120  face z -48.1
tier 3  30 m    unfinished floor Env_Tier3_UnfinishedFloor Tower_35 x  -70  face z -52.2
tier 4  60 m    stairwell       Env_Tier4_Stairwell     Tower_29  x   55  face z -37.2
tier 5 123 m    rooftop         Env_Tier5_Rooftop       Tower_16  x  -58  roof y 123.3
```

`EnvironmentSceneLoader` on `[Session_Controller]` does the swap. A blank entry in its
`_sceneNames` array means "use the environment in the core scene" — that is how tier 1
still works.

**Tier flow**, driven by `RitualSequencer`:

```
Interlude  → fade black, cut ambience, load scene, place rig at Spawn_Anchor,
             show context line, play clip, fade in
Narrate    → subtitle line
Narrate    → subtitle line
WaitForClick → pulsing indicator at Exit_Point; clicking it advances the tier
```

`AdaptiveExposureFSM.RequestAdvanceTier()` runs inside `BehindBlack()` — the fade happens
*before* the world moves, otherwise the participant watches themselves get teleported.

**Controls:** `N` advance, `B` step back, `Escape` end. Trigger / `E` / left-click to
activate an indicator. Arrow keys are claimed by the XR Simulator for head pitch and
never reach the FSM.

---

## 4. Traps that have each cost a session

1. **Nothing under `Player_Rooftop_Environment` may be marked static.** Static batching
   bakes world positions; `ExposureHeightController` moves the whole assembly 101 m, so
   colliders move and meshes do not. Symptom: invisible room drawn 100 m overhead.

2. **`Spawn_Anchor` and `Exit_Point` are found by NAME at runtime** in whichever tier
   scene is loaded. Rename either and the spawn and the way out both silently stop
   working, with no error.

3. **Only `Environment_Skyscraper` and `Skyscraper_Player_Tower` have colliders.** Every
   photoscanned city tower has none, so raycasts report "clear" through solid buildings.
   Use bounds intersection, never raycasts, to check whether a room is inside a tower.
   Two levels were built inside other buildings because of this.

4. **`ScreenFader` must not use a ScreenSpaceOverlay canvas.** Overlay canvases render to
   the desktop Game view and never to an HMD. It now uses a quad parented to the camera.
   Subtitles sit at renderQueue 3600, the fade quad at 3500 — if those invert, the text
   vanishes exactly when it matters.

5. **LFS cannot be pushed to a fork of a public repo.** `.gitattributes` exempts
   `Assets/Textures/**` and `Assets/Audio/**` for this reason. Any new binary must go in
   an exempted path or it blocks every push.

6. **The XR Simulator composites its own environment.** During earlier debugging, Meta's
   bedroom and a portrait of a stranger were mistaken for project bugs. `FlatModeInspector`
   on `[BuildingBlock] Camera Rig` gives WASD + right-drag with nothing composited, but
   only when no XR display is active.

---

## 5. The Android switch — read before attempting

`Assets/vrbn_studios` is 9.87 GB across 976 textures, mostly uncompressed 4K–8K TGA,
181 of them over 16 MB. A platform switch re-encodes every one. This took a 24 GB
machine to a two-minute mouse latency and never completed, **twice**.

**Already mitigated:** all 976 now carry an Android-only override at 512px with
`textureFormat: -1` (Automatic, so Unity picks ASTC). Windows import is untouched.
Commit `4d51c2b`. `Assets/Editor/AndroidTextureBudget.cs` does the same from a menu item
if it ever needs redoing.

Before switching:
- Free space on `C:` — Recycle Bin, `%LOCALAPPDATA%\Temp`, `%LOCALAPPDATA%\Unity\cache`.
  Old Unity Editor versions in `C:\Program Files` are 8–15 GB each and the biggest win.
- Set Texture Compression to **ASTC** in the Android build profile.
- Start it when the machine can be left alone. **Do not interrupt it** — two interrupted
  switches are what corrupted `Library` and made the project unopenable.

---

## 6. What is left to do

**Blocking**
- Reconcile local `main` with `fork/main` (rebase or merge). Nothing ships until this is done.
- Switch to Android and produce an APK.

**Known defects**
- Level 2 has "a weird broken wall in front of the desk" — reported, never reproduced.
- Concrete reads as sandy stucco. That is the texture set, not the tiling; a smoother
  concrete would fix it.
- Interlude audio: footsteps carries tiers 3, 4 and 5. Thin. Tiers 3 and 5 want their own
  sound — a door, a service lift, wind rising.
- Tier 1's interlude is silent by design. May read as broken rather than intentional.

**Polish**
- Door panel relief — it is a flat slab with a bark-like texture, dead centre of the view.
- Mirror is window glass, not a mirror. Needs a reflection probe.
- Toothbrushing ritual at the mirror (the audio is already imported).
- `OVROverlayCanvas` for subtitle clarity on device, and SSAO off — both from Meta's
  Project Setup Tool checklist.
- `[Header]` attributes across 8 files; ~3,960 lines of dead fruit-ninja code (the
  teammate's purge commit may have already removed some of this).

---

## 7. Conventions

- Commits author as `gandhialt`. **Never** add Claude/AI attribution.
- Tag every milestone: `checkpoint-NN-short-name`. Currently at `checkpoint-42`.
- Never `git add -A`. Stage explicit paths.
- Never refresh assets, recompile, or save the scene while play mode is running — it
  triggers a domain reload and kills the session mid-test.
- Faculty docs in the repo root (`expec_README.md`,
  `RESEARCH_AND_IMPLEMENTATION_GUIDE.md`) are AI-generated scaffolding. The closed-loop
  elevation law, the 60 m cap and the prescribed file names do **not** bind. The course is
  Intro to AR/VR; this is a game project with light medical framing.

---

## 8. MCP

The Unity tools came from Unity's own `com.unity.ai.assistant` package, which runs the
bridge — there is no project-level `.mcp.json` to copy. Any other agent environment needs
pointing at Unity's MCP endpoint separately; nothing here carries over automatically.

Bridge is only live while the Unity Editor is open. Useful calls when it is:
`Unity_RunCommand` (compiles and runs a C# `IRunCommand`), `Unity_GetConsoleLogs`,
`Unity_Camera_Capture`.

Two gotchas with `Unity_RunCommand`:
- The class **must** be named `CommandScript` and declared `internal`.
- Nested classes and named tuples get mangled by its code rewriter. Use parallel arrays.
- `GetInstanceID()` is deprecated in this Unity version — use `GetEntityId()`. Note its
  values exceed JSON's safe integer range, so they cannot be passed as tool parameters.
