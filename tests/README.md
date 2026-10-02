# Tests

Run all tests from the repo root:

    dotnet test

Snapshot tests render into a `FrameBuffer` and compare pixel-exact against golden PNGs in
`tests/LedMatrixOS.Tests/Snapshots/`. A missing golden is created on first run. To accept intentional
changes, regenerate goldens and commit them:

    UPDATE_SNAPSHOTS=1 dotnet test        # PowerShell: $env:UPDATE_SNAPSHOTS=1; dotnet test

On a mismatch the test fails and writes `<name>.actual.png` next to the golden (gitignored) for inspection.
Time-dependent apps (using `DateTime.Now`) are only smoke-tested until a TimeProvider/FrameContext exists.
