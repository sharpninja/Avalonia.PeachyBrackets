# Milestone 1 progress log

## Step 1 - Solution structure (done)
- `Avalonia.PeachBrackets.slnx` (SDK 10 solution format), `global.json` pins SDK 10.0.403 (rollForward latestFeature).
- `src/Avalonia.PeachBrackets` - control library, multi-targets net8.0 + net10.0, trimmable/AOT flags on.
- `samples/Avalonia.PeachBrackets.Demo` - demo app (net10.0).
- `tests/` reserved for tests.
- `Directory.Build.props` (nullable, implicit usings) and `Directory.Packages.props` (central package management).
- `dotnet build`: 0 warnings, 0 errors.
