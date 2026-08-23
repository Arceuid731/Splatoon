# Preset Hub V2

Preset Hub is an integrated, opt-in catalogue for Splatoon layouts and scripts. V2 adds contextual suggestions when entering a duty without ever installing content automatically.

## Architecture

- `Splatoon.PresetHub.Core` contains GitHub repository definitions, revision-aware synchronization, local JSON cache, `.md`/`.cs` indexing, territory matching, installation records, remembered prompt preferences, search metadata, and Roslyn-based script review. It has no Dalamud dependency.
- `Splatoon/Modules/PresetHub` adapts the core to Splatoon's existing `Utils.ImportLayouts` and `ScriptingProcessor.CompileAndLoad` pipelines.
- `Splatoon/Gui/PresetHub` contains the Browse, Installed, Repositories, and Duty prompts screens plus the duty-entry suggestion window.

Only three small hooks exist outside those folders: one project reference, module lifecycle initialization/disposal, and one configuration tab. This is deliberate so merges from `PunishXIV/Splatoon` remain straightforward.

## Behaviour

- The official `PunishXIV/Splatoon` repository is configured on first use with `Presets/` and `SplatoonScripts/` prefixes; upstream developer-only `SplatoonScripts/Tests/` files are excluded.
- Additional public GitHub repositories can be added as `owner/repository`, enabled/disabled, refreshed, and removed.
- The GitHub commit SHA is checked before downloading changed `.md` and `.cs` files. Cached snapshots remain usable when GitHub is unavailable.
- Layouts are imported through Splatoon's existing importer and tracked by their installed layout names.
- Scripts are never installed directly from a URL. Preset Hub analyses and displays the exact cached source and SHA-256 hash, requires an explicit confirmation, then passes those same bytes to Splatoon's compiler.
- Managed installations record their source and content hash, enabling Installed and Update available states plus uninstall.
- Layout territory IDs and numeric script `ValidTerritories` are indexed. After entering an instanced duty, Preset Hub waits for repository synchronization and proposes only matching items that are not installed or have an update.
- Matching layouts are selected explicitly and can be installed together. Matching scripts always go through the existing source/hash security review.
- Closing the prompt postpones the choice until the next visit. A duty can be permanently suppressed and restored from the `Duty prompts` tab; the entire feature can also be disabled.

## Security boundary

The script analyser highlights syntax errors, unsafe code, file/network/process/reflection/native APIs, and access to other plugins. It is advisory, not a sandbox or proof of safety. A reviewed C# script still runs with Splatoon's effective access.

Repository trust is provenance metadata only. `Official` is reserved for the built-in PunishXIV source; user-added sources start as `Untrusted`.

## Current limitations

- V1 supports public GitHub repositories. Private repository authentication is not stored in Splatoon.
- Installation completion from Splatoon's asynchronous script compiler has no callback, so Preset Hub records a reviewed script when it is queued. Compiler failures remain visible through Splatoon's existing script UI/logs.
- Preset Hub manages only items installed through Preset Hub; it does not claim ownership of manually imported layouts or scripts.
- Script territory matching recognizes numeric values declared directly in `ValidTerritories`. Computed or enum-based territory expressions remain available in the browser but are not guessed for automatic duty suggestions.
- Preset Hub deliberately has no background auto-install. Every install or update remains a user action.
