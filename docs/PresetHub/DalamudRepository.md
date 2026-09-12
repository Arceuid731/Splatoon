# Custom Dalamud repository preparation

A public prerelease package is available for direct in-game testing. It remains separate from any official Dalamud submission or PunishXIV release.

The custom repository URL is:

```text
https://raw.githubusercontent.com/Arceuid731/Splatoon/codex/preset-hub-v1/distribution/pluginmaster.json
```

## Release checklist

1. Merge a tested Preset Hub release commit into the chosen release branch.
2. Synchronize and validate against `PunishXIV/Splatoon`.
3. Choose a version newer than the installed upstream Splatoon version and build `Splatoon/Splatoon.csproj` in Release/x64 with the current Dalamud development files.
4. Run core and runtime-adapter tests and inspect the packaged manifest and assemblies. Smoke-test the packaged plugin in Dalamud when a client is available: coverage refresh, area/mechanic controls, preview, migration, reviewed scripts, restart persistence, and offline cache. State explicitly when client or combat validation remains pending.
5. Host the generated plugin zip at a stable HTTPS URL.
6. Run `distribution/Prepare-DalamudRepository.ps1` with that exact URL and version.
7. Validate the generated `pluginmaster.json` locally before publishing its URL.
8. Publish only after explicit approval.

The fork intentionally keeps `InternalName: Splatoon`, because it is a replacement build rather than a side-by-side companion plugin. Users should disable/remove the upstream build before installing this one.

## Current delivery

`preset-hub-v3.0.0` / assembly `3.9.2.30` was published on 2026-09-12 under the
owner's explicit request to deliver the refactor for an in-game update. Both CI
workflows passed; the public package hash and the exact repository URL above were
verified. Client and combat smoke tests remain pending. See
[the coverage report](Coverage-refactor.md#publication-vérifiée-le-12-septembre-2026)
for the source commit, workflow links and package hash.
