# Custom Dalamud repository preparation

No release or install endpoint is published by this repository yet. The files under `distribution/` only prepare a future custom repository after explicit approval.

Once the test release is published, the custom repository URL is:

```text
https://raw.githubusercontent.com/Arceuid731/Splatoon/codex/preset-hub-v1/distribution/pluginmaster.json
```

## Release checklist

1. Merge a tested Preset Hub release commit into the chosen release branch.
2. Synchronize and validate against `PunishXIV/Splatoon`.
3. Choose a version newer than the installed upstream Splatoon version and build `Splatoon/Splatoon.csproj` in Release/x64 with the current Dalamud development files.
4. Smoke-test the packaged plugin in Dalamud: repository refresh, layout install/update/uninstall, reviewed script install/update/uninstall, restart persistence, and offline cache.
5. Host the generated plugin zip at a stable HTTPS URL.
6. Run `distribution/Prepare-DalamudRepository.ps1` with that exact URL and version.
7. Validate the generated `pluginmaster.json` locally before publishing its URL.
8. Publish only after explicit approval.

The fork intentionally keeps `InternalName: Splatoon`, because it is a replacement build rather than a side-by-side companion plugin. Users should disable/remove the upstream build before installing this one.
