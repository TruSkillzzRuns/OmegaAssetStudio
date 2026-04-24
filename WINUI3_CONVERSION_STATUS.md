# WinUI 3 Conversion Status

## Scope

This file tracks the local-only conversion of:

- `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master`

into a WinUI 3 version of MH UPK Manager.

This is **not** the active GitHub push target yet. The current goal is to convert and validate locally first.

## Non-Negotiable Migration Standard

This WinUI 3 conversion must be treated as a true replacement target for the old app.

The standing rule for every workspace is:

- `1:1 functionality`
- `1:1 options`
- `same behavior`
- `same fixes`

That means:

- do **not** redesign workflows just because the shell is changing
- do **not** simplify controls or remove options as a final state
- do **not** accept "close enough" behavior where the old app was more complete
- do **not** leave old bug fixes behind during the migration

The correct approach is:

1. match the old app's controls, defaults, and workflows as closely as practical
2. carry forward the real backend logic and all validated fixes from the old app
3. rebuild the UI natively in WinUI 3 without changing the expected behavior
4. treat any non-parity implementation as temporary only, not as the intended end state

This rule applies to all migrated areas, including:

- `Objects`
- `Mesh`
- `Backup`
- `Texture`
- `Retarget`
- `UI Editor`
- and any later migrated workspace

## Current state

The existing app in:

- `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\src`

is still a large WinForms application with:

- `MainForm`
- designer-generated forms
- WinForms-specific controls
- custom workspace panels
- OpenTK / Direct3D preview integration

Because of that, this is a staged migration, not a simple project-file swap.

## What has already been created

A parallel WinUI 3 shell project now exists here:

- `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\src.winui\OmegaAssetStudio.WinUI.csproj`

Related files:

- `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\src.winui\App.xaml`
- `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\src.winui\App.xaml.cs`
- `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\src.winui\MainWindow.xaml`
- `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\src.winui\MainWindow.xaml.cs`
- `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\src.winui\app.manifest`

The solution was also updated to include the new project:

- `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\OmegaAssetStudio.sln`

## Why a parallel shell

The current app is a large WinForms surface with designer-generated forms, custom rendering controls, and workspace panels created directly inside `MainForm`.

A direct in-place rewrite would be risky and hard to validate.

The safer path is:

1. Keep `DDSLib`, `UpkManager`, and other reusable libraries intact.
2. Stand up a WinUI 3 app shell in parallel.
3. Port one workspace at a time.
4. Retire the old WinForms shell only after the WinUI shell reaches feature parity.

## What the WinUI shell currently does

The WinUI 3 project currently provides:

- app entry point
- top-level navigation shell
- Home page
- navigation shell
- first-pass `Objects` workspace layout
- first-pass `Mesh` workspace layout with live SkeletalMesh / LOD data
- workspace placeholders for:
  - Backup
  - Texture
  - Retarget
  - UI Editor
  - Enemy Converter

It is still a migration shell overall. The `Objects` page now mirrors the original left-tree / right-inspector layout structure and now provides live UPK data, but it is still short of full old-app parity.

## Build status

The WinUI 3 shell now builds successfully on this machine.

Working build command:

```powershell
dotnet build "C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\src.winui\OmegaAssetStudio.WinUI.csproj" -c Debug -m:1
```

Key project settings that unblocked the build:

- `EnablePriGenTooling=false`
- `EnablePreviewMsixTooling=true`

These settings force the project away from the missing Visual Studio `AppxPackage` PRI tooling path and let it use the package-local Windows App SDK MSIX / PRI targets instead.

There was also one WinUI/XAML model fix needed:

- `ObjectTreeItem.Name` changed from `init` to `set`
- `ObjectTreeItem.Kind` changed from `init` to `set`

This was required because generated WinUI XAML binding code assigns those properties after construction.

## Objects workspace progress

The WinUI `Objects` page is no longer only a static placeholder.

It now supports:

- opening a `.upk` file from a WinUI file picker
- loading the package with `UpkFileRepository`
- reading the live header tables
- loading required native UPK compression DLLs in the WinUI output:
  - `lzo2_64.dll`
  - `msvcr100.dll`
- showing package summary counts for:
  - names
  - imports
  - exports
- populating a first-pass tree for:
  - Exports
  - Imports
- populating first-pass tab lists for:
  - Names
  - Imports
  - Exports
- basic filtering in the tree
- collapsed child branches by default instead of opening every subtree
- basic inspector summary updates when selecting a tree item
- richer `File` tab package metadata
- selection-aware `Names`, `Imports`, and `Exports` tabs
- parsed property-tree population for selected exports
- first-pass inspector actions for selected exports:
  - Copy Path
  - Open Hex
  - Texture Details
  - Mesh Details
- first-pass workspace handoff from `Objects` into:
  - Texture
  - Mesh
- adjustable left-side object browser width from inside the page
- startup and object-load crash logging:
  - `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio_WinUI_crash.log`
  - `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio_WinUI_objects.log`
- a wider left-side object browser
- cached `Objects` page navigation so switching tabs should not unload the currently opened UPK

It still does **not** yet provide:

- full WinForms parity for the right-side inspector
- full texture workspace implementation after handoff
- tree context menus and old app shortcut actions
- dense old-app formatting and icon behavior

## Mesh workspace progress

The WinUI `Mesh` page is now the next real migration target instead of a placeholder.

It now supports:

- opening a `.upk` directly from the Mesh page
- reusing `Objects` handoff context to jump into Mesh with a selected export
- loading live `USkeletalMesh` exports from the package
- selecting a SkeletalMesh export and LOD
- a first-pass internal workspace layout matching the old WinForms structure:
  - Preview
  - Exporter
  - Importer
  - Sections
- live preview-side summary rows for the selected mesh:
  - class
  - outer
  - serial size
  - material count
  - socket count
  - LOD count
  - ref skeleton bone count
  - skeletal depth
  - clothing asset count
- live LOD summary rows for the selected LOD:
  - sections
  - chunks
  - vertices
  - indices
  - active bones
  - required bones
  - texcoords
  - LOD size
  - color vertex count
- section and chunk breakdown lists for the active LOD
- material and socket / bone summary lists
- first-pass exporter/importer workflow panes that stay tied to the selected mesh
- real FBX export actions through the existing backend
- real FBX import / replace actions through the existing backend
- importer confirmation dialog before replacing the UPK
- importer `Replace all LODs` state now updates the workflow text immediately when toggled
- live export/import log streaming inside the WinUI mesh panels
- first native WinUI preview slice:
  - uses the existing UE3 mesh conversion path
  - renders a software clay preview into a WinUI image surface
  - does not reuse the old WinForms viewport UI
- first native WinUI `Direct3D11` preview path:
  - uses `SwapChainPanel`
  - uses a native WinUI/Vortice D3D11 renderer instead of the old app renderer
  - keeps the old-app-style preview control surface in front of the new renderer
  - is partially wired but not yet stable enough to replace the fallback path
- first native WinUI preview control surface:
  - Display Mode
  - Shading Mode
  - Background
  - Lighting Preset
  - Material Channel
  - Weight View
  - Focused Bone
  - Focused Section
  - mesh overlay toggles
  - wireframe toggle
  - ground toggle
  - yaw / pitch / zoom controls
  - Reset Preview
  - Reset Camera
- first live native preview settings now wired:
  - shading mode
  - background style
  - lighting preset
  - wireframe overlay
  - ground plane
  - camera-driven rerender

It still does **not** yet provide:

- stable final hardware-accelerated native preview viewport
- section-editing tools
- pointer-driven viewport camera interaction in WinUI
- full GameApprox / material-aware preview parity
- full live behavior for all preview toggles and channels

## Mesh preview renderer status

There are currently two native WinUI preview paths in the Mesh page:

1. `WinUI.NativePreview`
2. `VorticeDirect3D11`

Important:

- `WinUI.NativePreview` is **temporary fallback/debug only**
- it is CPU-heavy and slow
- it produces poor visual quality and is **not** acceptable as the final preview renderer
- it should not be treated as the real replacement path

The real renderer target is:

- `VorticeDirect3D11`

That is the path that must be stabilized and then improved until it matches the old app's preview behavior and quality.

Current `VorticeDirect3D11` status:

- the WinUI page reaches the D3D11 path
- a native `SwapChainPanel`-based renderer now exists
- the first interop hook was replaced with Vortice's built-in `ISwapChainPanelNative`
- the page now waits for the preview surface to load before trying the first D3D11 render
- it is still not fully reliable in real use and needs more stabilization work

## Current focus for Mesh preview

Do **not** spend more time treating `WinUI.NativePreview` as the real renderer.

The standing focus now is:

1. stabilize `VorticeDirect3D11`
2. make it the real/default preview path
3. improve that D3D11 path until it reaches old-app behavior and quality
4. keep the same control surface and same option set as the old app while doing that

This is the correct path under the migration standard:

- `1:1 functionality`
- `1:1 options`
- `same behavior`
- `same fixes`

## Recommended migration order

1. Expand the `Objects` workspace until the right-side inspector feels close to the old app.
2. Keep deepening the `Mesh` workspace now that it has live SkeletalMesh / LOD data, real export/import actions, and a first native preview slice.
3. Port `Backup` and `Texture` next.
4. Port mesh preview hosting after the data/workflow shell is strong enough.
5. Port `Retarget` after mesh preview hosting is proven.
6. Port `UI Editor` later.

## Current resume point

The current best next step is:

1. launch the WinUI app locally
2. verify the new Mesh workspace shape:
   - Preview
   - Exporter
   - Importer
   - Sections
3. keep deepening the Mesh page until it feels like the old workspace shell
4. keep replacing software preview placeholders with native WinUI preview capabilities
5. continue wiring the remaining old Mesh preview options into the native renderer one group at a time
6. then decide whether the next step is `Texture` or `Backup`

- live UPK browsing
- mesh preview hosting
- texture workflows
- retargeting workflows
- UI editor functionality

Those still need to be ported intentionally from the existing WinForms implementation.

## Why this order

`Objects`, `Backup`, and `Texture` are safer first targets because they are less dependent on the most complex custom rendering stack.

`Mesh` and `Retarget` are later targets because they depend on:

- OpenTK / Direct3D preview behavior
- custom control hosting
- more state-heavy workflows

## Important note about Enemy Converter

`Enemy Converter` is only represented as a placeholder in the WinUI shell right now.

It should stay isolated from the WinUI migration until that feature itself is ready for migration.

## Resume point

If work resumes later, the next action should be:

1. reopen:
   - `C:\Users\TruSkillzzRuns\Desktop\OmegaAssetStudio-v2.0-master\src.winui\bin\Debug\net8.0-windows10.0.19041.0\win-x64\OmegaAssetStudio.WinUI.exe`
2. verify:
   - `Objects` still loads and hands off cleanly into `Mesh`
   - `Mesh` loads SkeletalMesh exports from a real UPK
   - Preview / Exporter / Importer / Sections switch cleanly
   - mesh and LOD selection refresh the live summary rows correctly
3. if those are good, continue deepening the native Mesh preview
4. next WinUI Mesh preview task:
   - stabilize the `VorticeDirect3D11` path first
   - do not rely on `WinUI.NativePreview` except as temporary fallback/debug
   - make `VorticeDirect3D11` the real working path
   - then wire the remaining old preview toggles and channels into that renderer
   - then add pointer-driven orbit / pan / zoom on top of the stable D3D11 path

