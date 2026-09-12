# Preset Hub

Preset Hub discovers Splatoon layouts and scripts in public GitHub repositories. It proposes matching content on duty entry and supports batch installation, variant comparison, updates and uninstall.

## Components

- `Splatoon.PresetHub.Core`: revision-pinned GitHub downloads, versioned cache, indexing, deduplication, source provenance, installation records, territory matching and advisory script analysis.
- `Splatoon/Modules/PresetHub`: cancellable refresh coordination and the adapter to Splatoon's importer/compiler.
- `Splatoon/Gui/PresetHub`: Browse, Installed, Repositories, Duty prompts and the entry window.
- `Splatoon.PresetHub.Core.Tests`: core tests and the production installer compiled against a minimal test host.
- `tools/PresetHubAudit`: reads an existing cache and inspects candidate sources, writing only to the supplied output directory.

## Behaviour

Catalogue upgrades add eight sources: Hibiya615's English set, Leathen, SourP, NebulousByte, RedAsteroid, Lu-Jiejie, MisaUo and OkuKatsu. These new sources are enabled and restricted to layout installation. All existing source settings are preserved. The full catalogue has 17 sources.

Downloads use a fixed commit and up to eight concurrent requests. The cache is reused only when revision, index schema and source settings match. Failed or cancelled refreshes retain the previous snapshot. Concurrent refresh requests are coalesced; unloading cancels network work.

The indexer handles modern and legacy exports, including multiline and inline Markdown. Payloads are compacted for Splatoon's line-based importer. Unrelated export reordering no longer changes identities. Same-name variants use content fingerprints. Exact copies are collapsed, while different versions retain a choice panel. Unscoped layouts in unrelated files are not grouped solely by name.

Territories are read from literal collections on the actual script class, including generic `SplatoonScript<T>` bases. Computed expressions are not guessed. A layout's zone blacklist is not treated as an inclusion list.

Duty suggestions can use the cache during refresh. An empty cache waits for refresh completion instead of expiring after 30 seconds. Leaving the territory cancels the pending visit. Closing the prompt postpones it until the next visit; permanent suppression remains configurable.

The default batch avoids selecting layouts with overlapping cast, status or VFX identifiers, taking existing managed installations into account. Broader coverage and English editions are preferred. The NPC identity alone does not suppress complementary mechanics. Alternatives remain manually selectable. This is conservative selection, not proof of semantic equivalence: partial overlaps may leave unique content unchecked, and overlays without comparable mechanic IDs can still overlap.

Layout replacements restore previous objects, ordering and UI selection on import or registry-write failure. Name collisions remain blocked even with Ctrl held. New installations receive a persistent ownership ID that survives renaming. External imports have any supplied ownership ID cleared. Legacy installations use their old names, with ambiguous matches excluded from removal.

Records retain a source entry so installations remain accessible after source removal. Old ordinal records are matched by content or unambiguous source/runtime identity, never by assuming that a new export at the same position is the old preset.

Reviewed scripts are recorded after successful loading and initialization, rather than when compilation is queued. Failed compilation can be retried. Pending duplicate installs and collisions with an unmanaged loaded script are blocked. The compiler's cache flag and completion callbacks belong to each queue entry.

## Limits

- Static script analysis is advisory; scripts execute with the plugin's access.
- Parsing and builds do not validate encounter timings, mechanic coverage or rendering in game.
- Legacy installations renamed before this update, or already missing their cache, may require manual reconciliation. Old records cannot establish retrospectively whether a previously queued script compiled successfully.
- Config and registry persistence is not a crash-atomic multi-file transaction.
- GitHub rate limits can temporarily block refresh; cached content remains usable.
- Private repositories, Discord attachments and wiki pages are not directly indexed. Wiki links help locate source repositories.
- A simultaneous source path/name/content change cannot always be matched safely; the previous installation remains available for management.

## Validation

```powershell
dotnet test Splatoon.PresetHub.Core.Tests/Splatoon.PresetHub.Core.Tests.csproj --configuration Release
dotnet build Splatoon/Splatoon.csproj --configuration Release -p:Platform=x64
dotnet run --project tools/PresetHubAudit --configuration Release -- artifacts/audit PATH_TO_EXISTING_CACHE Hibiya615/Splatoon_Presets
```

A local directory of candidate snapshots can replace the final repository argument to audit default selections without network access. The package workflow reads the artifact version from the project and runs the tests. The published repository manifest remains on the last published version until a new release is approved and uploaded.

See [the September 2026 audit](Audit-2026-09-12.md) for findings and checked sources.
