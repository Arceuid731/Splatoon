# Native coverage tree regression

Run on Windows with the Dalamud development files installed:

```powershell
dotnet run --project tools/PresetHubUiSmoke -c Release
```

The test links the production `CoverageTree` helper and renders nested nodes in
an isolated ImGui context, using the actual Dalamud binding and native library.
No game process or player configuration is used. CI supplies `DalamudLibPath`.

For diagnosis only, `--reproduce-empty-label` calls the original unsafe binding
with an empty label. This deliberately terminates the isolated test process with
access violation `0xC0000005`; it is not part of the normal CI command.
