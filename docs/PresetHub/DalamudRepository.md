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

`preset-hub-v3.0.1` / assembly `3.9.2.31` fixes the native crash reported when
expanding A Realm Reborn. The source commit is
`cf1bb1b95476ec7b33be87c7b83057b96d4439f5`.
Both [core CI](https://github.com/Arceuid731/Splatoon/actions/runs/34716895489)
and [Windows packaging with native UI regression](https://github.com/Arceuid731/Splatoon/actions/runs/34716895491)
passed. The public package matches the CI artifact, SHA-256
`0f6a499f4ec46aac4287d6c13bb36a05909b25dd0e99528c80ecf9c4e6f8a7d2`.
The original native crash was reproduced in an isolated process; the corrected
tree renders successfully with the actual Dalamud ImGui binding. Reopening it in
the player's client remains to be confirmed. See [release notes](Release-v3.0.1.md).
