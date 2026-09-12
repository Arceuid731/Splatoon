# Preset Hub — mechanic coverage

The hub builds a persistent library of drawing aids from public GitHub sources.
Layouts are source documents, not the unit players have to install. Updating the
library analyzes all enabled sources and prepares selections for every identified
territory. The entry panel shows coverage and remembered activation controls.

## Player workflow

- **Coverage**: game-data area hierarchy, encounters identified by active actor IDs,
  mechanics, other aids, per-area/encounter/mechanic toggles and drawing previews.
- **Update coverage**: refresh sources and recompute their combined contributions.
- **Source library**: inspect provenance and review executable scripts. Layout rows
  open their coverage instead of installing a second independent copy.
- **Installed**: retained installation records; managed layout backups remain in the
  original configuration. Scripts keep their review/update/uninstall controls.
- **Repositories / Duty prompts**: source selection and entry-panel preferences.

Sources do not provide an exhaustive encounter reference. Counts describe identified
mechanics in the library, not a percentage of all mechanics in a fight. No Foretell
code or data is required. Unclassified but scoped aids appear under other aids in
the relevant area. Already-installed global aids are scoped to the current area for
display and control while preserving their original blacklist.

## Compilation and merging

`LayoutCoverageAnalyzer` reads the predicates used by the current Splatoon runtime.
An inactive cast/buff/time/distance field does not count as a predicate. Fixed shapes
do not inherit inactive actor filters. Cast OR-lists can be separated; inverted
tests stay intact. Capture dependencies and conditional sequences form connected
groups. Trigger-driven layouts, subconfigurations and freezing remain indivisible.

Each contribution retains its complete executable layout fragment and source. A
claim identifies its territory, actor context, active signals, predicate and aid
role. Geometry/text variants with equivalent conditions compete for the same aid;
different roles such as an area and a textual reminder remain complementary. Local
capture references are normalized by the captured condition rather than translated
element names. Original names and references remain intact in executable fragments.

`CoveragePlanner` maximizes distinct aid coverage under those conflicts, without
splitting linked contributions. Equally covering variants prefer local changes,
language-independent matching, readable English text and source confidence. An
explicit choice takes precedence. A bounded search handles unusually entangled
sets; its completion flag is retained for diagnostics. This is structural analysis,
not proof that a drawing's strategy or timing is correct.

`CoverageLibraryBuilder` caches analysis per export revision and prepares all area
selections. Changes to source content, compatibility or metadata invalidate the
affected entries. The compiled library and activation preferences are persisted
separately with atomic file replacement. Unchanged library refreshes preserve live
runtime objects, including their trigger state.

The catalogue contains 17 sources. All of Hibiya615's repository is indexed, not
only `[EN Set]`; English preference applies to content wherever it appears. The
previous built-in EN-only restriction is migrated while retaining enabled and
script-installation settings. The eight added repositories remain layout-only.
Downloads are pinned to a commit, bounded in size and concurrent. Failed refreshes
retain cached data and are reported on the main coverage page.

## Runtime and existing installations

Prepared fragments are passed to Splatoon's existing renderers. The coverage library
does not insert thousands of layouts into the user's configuration. Old managed
layouts are retained as backups and skipped only after a replacement runtime set
has been prepared successfully. Preparation failure keeps the previous set and its
ownership decisions together.

Local geometry/condition changes are retained and suppress the corresponding remote
export. Existing disabled layouts/groups seed remembered opt-outs once. Later hub
choices are not repeatedly overwritten by that migration. Disabling the library
does not reactivate its old managed backups. Untouched manual layouts remain under
Splatoon's normal layout controls.

Reviewed scripts remain executable code and are not merged by this layout compiler.
They appear in a separate scripted-aids section, including saved unavailable sources
and installed global scripts. Managed scripts have per-area opt-outs; area/global
hub controls also gate them. Unmanaged scripts retain their normal controls. Script
installation records are written only after successful loading and initialization.

## Preview

The isolated top-down preview uses example actor/player positions. It supports
circles, rings, cones, lines/rectangles, text, tethers and knockback extensions.
Geometry follows the renderer's coordinate/rotation conventions and applies current
display-style overrides. It never adds a live layout or fires a game event.

Encounter timing, live target resolution, cast animation and gradients are not
replayed. Hitbox assumptions and dynamic position/direction limitations are shown
when relevant. Capture-only elements are identified as non-drawing elements.

## Validation and limits

```powershell
dotnet test Splatoon.PresetHub.Core.Tests --configuration Release
dotnet build Splatoon/Splatoon.csproj --configuration Release -p:Platform=x64
dotnet run --project tools/PresetHubAudit --configuration Release -- --coverage artifacts/coverage-audit PATH_TO_EXISTING_CACHE PATH_TO_CANDIDATE_CACHE
```

Tests include the production installer and coverage runtime adapter compiled into a
minimal host, plus planner, persistence, dependency, migration and preview geometry
checks. The corpus audit verifies plans across all cached territories. These checks
do not constitute live battle validation. A new-game-session check remains useful
after updating, particularly for encounter-specific dynamic drawings.

Ambiguous legacy ownership, missing historical source content and external capture
dependencies cannot always be reconstructed. Existing layout files are retained.
New unscoped source exports are visible in the source library but are not assumed
applicable everywhere. Private repositories, Discord attachments and wiki pages are
not directly fetched. GitHub rate limits can postpone new data while cached coverage
remains available.

See [the implementation checklist](Coverage-refactor.md) and
[the original audit](Audit-2026-09-12.md).
