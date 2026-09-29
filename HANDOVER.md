# FastDelete — Handover Guide

Read this fully before touching anything. It assumes a fresh AI session with zero context.

## What this project is

FastDelete: a free, open-source Windows app for deleting enormous folders (tested to
100k+ files) as fast as NTFS allows, with a GUI designed for non-technical users
("mom-friendly"). Public repo: https://github.com/berkkarabacak/FastDelete
Live site (GitHub Pages): https://berkkarabacak.github.io/FastDelete/

Latest released version: **v0.5.4** (one-click install.cmd/uninstall.cmd in the zip,
plus a fix for swallowed count-cancellation). Latest published release on GitHub: v0.5.4.

The app is also INSTALLED on the dev machine (see "Installed on this machine" below).

## Repository layout

```
FastDelete.sln
src/FastDelete.Core        the engine (net8.0 library) - all the hard stuff
src/FastDelete.App         the WPF GUI (net8.0-windows, UseWPF + UseWindowsForms)
src/FastDelete.Benchmarks  throughput harness (console)
tests/FastDelete.Core.Tests   48 xUnit tests - ALL MUST PASS BEFORE ANY RELEASE
tests/FastDelete.TestData     deterministic tree generator (console)
docs/index.html            GitHub Pages landing site (source = main branch /docs)
docs/benchmarks.md         measurement history + strategy shootout
docs/step-1..3.png         annotated walkthrough screenshots
release/                   files that ship inside the release zip (install.cmd,
                           uninstall.cmd, README.txt, make-practice-files.cmd,
                           Benchmark-on-this-PC.bat/.ps1) - edit versions here
tools/make_icon.py         regenerates Assets/app.ico
artifacts/                 release zips (gitignored; rebuild via publish)
scripts/                   UI-automation test scripts (see below)
```

## CRITICAL environment facts (this machine will waste your hours if you skip these)

1. **Always run dotnet through `bash dn.sh <args>` from the repo root.** This Git Bash
   environment is missing standard Windows env vars (ProgramFiles(x86), USERPROFILE,
   etc.); bare `dotnet` crashes with "Value cannot be null (Parameter 'path1')".
   dn.sh injects everything via `env`. If restore misbehaves:
   `bash dn.sh build-server shutdown` (stale MSBuild node reuse is a trap - it once
   kept running pre-fix binaries after an edit; clean obj/bin if in doubt).
   NOTE: dn.sh hardcodes OdinLocal paths and `/c/Program Files/dotnet`. On any other
   machine, copy it to an untracked `dn-local.sh`, fix USERPROFILE/APPDATA/TEMP and
   the SDK PATH (the dotnet-install script needs `-Architecture x64` explicitly -
   same missing-env-var trap).
2. **Never declare a `GetLastError` P/Invoke stub** — a stub clobbers last error.
   Always `Marshal.GetLastWin32Error()` (this bug cost a day).
3. **The Win32 `SetFileInformationByHandle(FileDispositionInfoEx)` wrapper rejects
   class 64 with ERROR_INVALID_PARAMETER on this machine.** The engine calls
   `NtSetInformationFile` (ntdll, class 64) instead. This was NOT a fluke.
4. **`FILE_DELETE_ON_CLOSE` silently does not delete on this machine** (open succeeds,
   file survives close). Do not reintroduce it. `docs/benchmarks.md` documents the
   shootout; the 48 tests caught its phantom success.
5. **Screen capture and UIA on this VM lie.** WPF windows capture blank via
   CopyFromScreen unless the process is DPI-aware AND WPF renders software-only
   (already set: `RenderOptions.ProcessRenderMode = SoftwareOnly` in App.xaml.cs).
   WPF popups/menus and owned dialogs often do NOT appear in top-level UIA scans —
   enumerate the FastDelete window subtree instead, or find popup hwnds with
   EnumWindows and `AutomationElement.FromHandle`. See scripts/ for working examples.
   Screen is 2880x1800 physical at 175% DPI; non-DPI-aware tools see 1646x1029.
6. **Symlinks cannot be created here** (no SeCreateSymbolicLinkPrivilege); junctions
   via `cmd /c mklink /J` work. The symlink test skips gracefully on error 1314.
7. **git push**: the Kimi-bundled git has a `credential.gitHubHelper` that hangs ~90s.
   Bypass it: `BASE=$(printf 'x-access-token:TOKEN' | base64 -w0); git -c credential.helper=
   -c http.extraheader="Authorization: Basic $BASE" push`. A repo PAT (ghp_...) was
   provided by the owner in chat; if it stopped working, ask for a new one.
   Do not store the token in .git/config.
8. PowerShell 5.1 CANNOT load net8 assemblies (no pwsh 7 installed). To exercise app
   code from a script, use the app's CLI flags, not Add-Type reflection.

## Commands

```bash
bash dn.sh build FastDelete.sln          # build everything
bash dn.sh test                          # 48 tests, ~1.5 min - MUST be 48/48 green
bash dn.sh run --project tests/FastDelete.TestData -- --root %TEMP%\fd --profile tiny10k
bash dn.sh run --project src/FastDelete.Benchmarks -- --profiles tiny10k,tiny100k --workers 1,4,8
src/FastDelete.App/bin/Debug/net8.0-windows/FastDelete.exe [folder]   # run GUI
FastDelete.exe --bench <folder>          # engine benchmark CLI (no window)
FastDelete.exe --install-explorer-menu / --uninstall-explorer-menu    # Explorer entry
```

## Release process (follow exactly)

1. `bash dn.sh test` → 48/48.
2. Bump `<Version>` in src/FastDelete.App/FastDelete.App.csproj.
3. `bash dn.sh publish src/FastDelete.App/FastDelete.App.csproj -c Release -r win-x64
   --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o artifacts/publish`
4. Zip (PowerShell Compress-Archive): publish\FastDelete.exe + the 5 *_cor3.dll +
   everything from release\ (README.txt + make-practice-files.cmd +
   Benchmark-on-this-PC.bat/.ps1 + install.cmd + uninstall.cmd) →
   artifacts/FastDelete-vX.Y.Z-win-x64.zip. Bump the version line in
   release/README.txt first.
5. `sed -i 's/vX.Y.Z-win-x64.zip/vNEW-win-x64.zip/g' docs/index.html README.md`.
6. Commit, push (see env fact 7).
7. Create release via GitHub API (POST /repos/berkkarabacak/FastDelete/releases),
   then upload the zip to its upload_url with Content-Type: application/zip.
8. Wait ~60s, verify https://berkkarabacak.github.io/FastDelete/ serves the new
   version string and the download URL returns 206/200 with --range 0-1000.
   If Pages doesn't rebuild within ~5 min of the push (happened for v0.5.4),
   trigger it: POST /repos/berkkarabacak/FastDelete/pages/builds, then re-check.
9. GUI smoke test: launch the published exe with a folder arg; check via UIA.

## Architecture notes (don't reopen these decisions)

- **Walker**: single-pass `FindFirstFileEx(FindExInfoBasic)` streaming to a BOUNDED
  channel with `WriteAsync` backpressure (TryWrite silently dropped items >16k —
  regression test exists). Post-order dir emission.
- **Dirs are NEVER deleted by parallel workers** (NTFS metadata-lock convoy, 5x
  slowdown on deep trees). All dirs flow through one sequential post-order pass with
  a 64-round DIR_NOT_EMPTY retry for external TOCTOU.
- **Per-file strategy (measured, see docs/benchmarks.md)**: classic DeleteFileW first
  (fastest); DispositionEx (NtSetInformationFile class 64, POSIX_SEMANTICS) only as
  the fallback for sharing-locked files. Read-only files handled by attribute-clear
  retry in the classic path.
- **Default workers** = clamp(ProcessorCount/2, 2, 8) — measured +30-40% over 4.
- **Reparse points are unlinked, never followed** (junctions + symlinks; FSCTL_DELETE
  REPARSE_POINT with tag fallback, then plain removal — plain removal of a link is
  always safe).
- GUI: MVVM (CommunityToolkit.Mvvm), light theme default, Explorer-style selection
  (no checkbox column!), settings persisted to %APPDATA%\FastDelete\settings.json
  (saved eagerly on navigation).

## Testing tools that work here

- 48 xUnit tests cover the engine incl. hostile cases.
- scripts/ has PowerShell UIA drivers: e2e-delete.ps1 (full flow), test-menuclick3.ps1
  (context-menu multi-delete via hwnd-based popup detection), capture-steps.ps1 +
  annotate-steps.py (annotated walkthrough screenshots for the site).
- Benchmark-on-this-PC (in the zip) races Explorer/PowerShell/FastDelete on any PC.

## Installed on this machine (dev box)

NOTE: the box is now the `berk` machine (the OdinLocal paths below are history).
- `C:\Users\berk\AppData\Local\Programs\FastDelete\` (v0.5.4, installed via
  release\install.cmd - the zip's installer does exactly this).
- Start Menu shortcut "FastDelete" (under %APPDATA%\Microsoft\Windows\Start Menu\Programs).
- Explorer right-click "Delete with FastDelete" enabled (HKCU).
- To uninstall: run uninstall.cmd in the install folder (removes all three).
- .NET SDK 8.0.425 lives at C:\Users\berk\.dotnet (user-local, not on PATH;
  dn-local.sh points at it).

## Known rough edges / TODO ideas

- No Inno Setup/MSI installer (Inno absent on the box); install.cmd/uninstall.cmd
  from release/ cover install, Start Menu, Explorer entry, and clean removal.
- GUI untested by unit tests (all GUI verification is UIA scripting). UIA notes:
  the big buttons have empty Name (label is a child Text, often behind an icon
  glyph - match with *substring*), and the confirm dialog is a Window INSIDE the
  main window's subtree, not a top-level window.
- Recycle-bin path has no per-file progress granularity.
- The race script's Explorer leg can hit the "Delete Multiple Items" confirmation;
  it auto-presses Enter, but the dialog title may vary by Windows build.
- Token hygiene: owner was asked (4x now) to revoke the PAT used during setup.

## Owner's context

Built for the owner's mother. The owner's feedback loop has been: UI readability ->
selection difficulty -> features -> speed -> "test as a real user" -> installed on
this machine. Tone of the UI copy: plain English, no jargon, counts always visible
before destructive actions.
