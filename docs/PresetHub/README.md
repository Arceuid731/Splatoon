# Preset Hub V2.1

Preset Hub is an integrated, opt-in catalogue for Splatoon layouts and scripts. V2.1 adds curated community sources, content-based discovery, provenance-aware deduplication, confidence metadata, and contextual suggestions without ever installing content automatically.

## Architecture

- `Splatoon.PresetHub.Core` contains GitHub repository definitions, revision-aware synchronization, local JSON cache, content indexing, territory matching, provenance/deduplication, installation records, remembered prompt preferences, search metadata, and Roslyn-based script review. It has no Dalamud dependency.
- `Splatoon/Modules/PresetHub` adapts the core to Splatoon's existing `Utils.ImportLayouts` and `ScriptingProcessor.CompileAndLoad` pipelines.
- `Splatoon/Gui/PresetHub` contains the Browse, Installed, Repositories, and Duty prompts screens plus the duty-entry suggestion window.

Only three small hooks exist outside those folders: one project reference, module lifecycle initialization/disposal, and one configuration tab. This is deliberate so merges from `PunishXIV/Splatoon` remain straightforward.

## Behaviour

- The official `PunishXIV/Splatoon` repository plus Adam, Ksirashi, LeChuck, and Buddiman are enabled in the curated catalogue. Ungeho, cptjabberwock, Errerer, and thakyZ are present as opt-in sources. The archived `NightmareXIV/Splatoon` repository is deliberately excluded.
- Additional public GitHub repositories can be added as `owner/repository`, enabled/disabled, refreshed, and removed.
- A source supports a repository URL/name, branch or tag, optional roots, exclusions in the model, and recursive discovery. An empty root scans the full repository.
- The GitHub commit SHA is checked before downloading changed text and `.cs` files. Cached snapshots remain usable when GitHub is unavailable.
- Layout exports are detected by content, including multiple exports per file, fenced-inline `~Lv2~` exports, and legacy `Name~{...}` exports. UTF-8 titles are preserved.
- Exact layouts are collapsed by canonical content. Original sources are preferred over aggregators; mirrors remain visible as provenance. Differing layouts in the same conservative family remain separate and show their version count.
- Confidence reflects provenance, exact territory metadata, format, compatibility, and possible language-dependent actor/trigger matching. It does not claim that one mechanic solution is objectively better.
- Layouts are imported through Splatoon's existing importer and tracked by their installed layout names.
- Scripts are never installed directly from a URL. Preset Hub analyses and displays the exact cached source and SHA-256 hash, requires an explicit confirmation, then passes those same bytes to Splatoon's compiler.
- Managed installations record their source and content hash, enabling Installed and Update available states plus uninstall.
- Layout territory IDs and numeric script `ValidTerritories` are indexed with JSON and Roslyn respectively. Collection expressions and `new() { ... }` initializers are supported. After entering an instanced duty, Preset Hub proposes only exact territory matches that are not installed or have an update.
- Matching layouts are selected explicitly and can be installed together. Matching scripts always go through the existing source/hash security review.
- Closing the prompt postpones the choice until the next visit. A duty can be permanently suppressed and restored from the `Duty prompts` tab; the entire feature can also be disabled.

## Security boundary

The script analyser highlights syntax errors, unsafe code, file/network/process/reflection/native APIs, and access to other plugins. It is advisory, not a sandbox or proof of safety. A reviewed C# script still runs with Splatoon's effective access.

Repository trust is provenance metadata only. `Official` is reserved for the built-in PunishXIV source; user-added sources start as `Untrusted`.

Non-English display text is accepted normally. A layout is marked language-dependent only when runtime actor-name or text-trigger matching lacks an international form or a numeric identity.

Known incompatible scripts are visible for provenance/review but cannot be installed. The Errerer source currently blocks script installation because its scripts target older Splatoon APIs; its layouts remain available when the source is enabled.

## Current limitations

- V1 supports public GitHub repositories. Private repository authentication is not stored in Splatoon.
- Installation completion from Splatoon's asynchronous script compiler has no callback, so Preset Hub records a reviewed script when it is queued. Compiler failures remain visible through Splatoon's existing script UI/logs.
- Preset Hub manages only items installed through Preset Hub; it does not claim ownership of manually imported layouts or scripts.
- Computed or enum-based script territory expressions remain available in the browser but are not guessed for automatic duty suggestions.
- Preset families are intentionally conservative. Same-territory entries are not considered competing versions unless their normalized identity also matches; complementary boss mechanics must remain installable together.
- Most community preset repositories do not publish an explicit license. Preset Hub indexes remote content and links to its source; it does not bundle those presets into the plugin release.
- Preset Hub deliberately has no background auto-install. Every install or update remains a user action.
