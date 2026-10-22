# Music Mate

Music Mate (`com.bkdaniels.musicmate`) is a .NET 10 MAUI music-practice app. On Windows it targets
`net10.0-windows` and `net10.0-android`; on Linux/macOS only the `net10.0-android` target is available.

## Cursor Cloud specific instructions

The cloud VM is headless Linux (x86_64), so only the Android target is buildable here.

### Toolchain (baked into the VM snapshot)
- .NET 10 SDK in `~/.dotnet`, `maui-android` workload, Android SDK in `~/android-sdk`, JDK 21.
- Env vars (`DOTNET_ROOT`, `ANDROID_HOME`/`ANDROID_SDK_ROOT`, `JAVA_HOME`, `PATH`) are exported from `~/.bashrc`.
  A non-interactive shell that does not source `~/.bashrc` must set these itself (or call `~/.dotnet/dotnet` by full path).

### Build / run the app
- Build (dev): `dotnet build musicmate.csproj -f net10.0-android -c Debug`.
- ALWAYS pass `-f net10.0-android`. Omitting `-f` restores/builds every TargetFramework (incl. the Windows target)
  and can hang on NuGet — see `scripts/build-android.ps1` for the same warning.
- Output signed, installable APK: `bin/Debug/net10.0-android/com.bkdaniels.musicmate-Signed.apk`.
- A clean build takes ~1 minute; MAUI Android packaging can be slower on a cold build.
- Running the app interactively is not practical here: it is an Android app and the VM has no `/dev/kvm`, so an
  Android emulator cannot be hardware-accelerated. Validate changes by building the APK (and, on Windows, running
  the test suite), or deploy the APK to a physical device.

### Tests
- `musicmate.Tests` targets `net10.0-windows` (`RuntimeIdentifier win-x64`) and references the MAUI project, so it
  only builds/runs on Windows. On this Linux VM it fails with `NETSDK1100` (Windows targeting) and, if forced,
  `NU1201` (the app project exposes only `net10.0-android`). Run `dotnet test musicmate.Tests/musicmate.Tests.csproj`
  on a Windows machine.

### Lint
- There is no separate lint step; analyzers/style are driven by `.editorconfig` during `dotnet build`.
