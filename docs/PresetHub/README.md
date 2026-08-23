# Preset Hub V1

Preset Hub is an integrated, opt-in catalogue for Splatoon layouts and scripts. It intentionally does not implement automatic duty-entry prompts; that remains V2 scope.

## Architecture

- `Splatoon.PresetHub.Core` contains GitHub repository definitions, revision-aware synchronization, local JSON cache, `.md`/`.cs` indexing, installation records, search metadata, and Roslyn-based script review. It has no Dalamud dependency.
- `Splatoon/Modules/PresetHub` adapts the core to Splatoon's existing `Utils.ImportLayouts` and `ScriptingProcessor.CompileAndLoad` pipelines.
- `Splatoon/Gui/PresetHub` contains the Browse, Installed, and Repositories screens.

Only three small hooks exist outside those folders: one project reference, module lifecycle initialization/disposal, and one configuration tab. This is deliberate so merges from `PunishXIV/Splatoon` remain straightforward.

## V1 behaviour

- The official `PunishXIV/Splatoon` repository is configured on first use with `Presets/` and `SplatoonScripts/` prefixes.
- Additional public GitHub repositories can be added as `owner/repository`, enabled/disabled, refreshed, and removed.
- The GitHub commit SHA is checked before downloading changed `.md` and `.cs` files. Cached snapshots remain usable when GitHub is unavailable.
- Layouts are imported through Splatoon's existing importer and tracked by their installed layout names.
- Scripts are never installed directly from a URL. Preset Hub analyses and displays the exact cached source and SHA-256 hash, requires an explicit confirmation, then passes those same bytes to Splatoon's compiler.
- Managed installations record their source and content hash, enabling Installed and Update available states plus uninstall.

## Security boundary

The script analyser highlights syntax errors, unsafe code, file/network/process/reflection/native APIs, and access to other plugins. It is advisory, not a sandbox or proof of safety. A reviewed C# script still runs with Splatoon's effective access.

Repository trust is provenance metadata only. `Official` is reserved for the built-in PunishXIV source; user-added sources start as `Untrusted`.

## Current limitations

- V1 supports public GitHub repositories. Private repository authentication is not stored in Splatoon.
- Installation completion from Splatoon's asynchronous script compiler has no callback, so Preset Hub records a reviewed script when it is queued. Compiler failures remain visible through Splatoon's existing script UI/logs.
- Preset Hub manages only items installed through Preset Hub; it does not claim ownership of manually imported layouts or scripts.
- Automatic suggestions/popups on duty entry, remembered choices, and background auto-install are explicitly deferred to V2.
