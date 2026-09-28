# FastDelete — Benchmarks

All numbers measured with `src/FastDelete.Benchmarks` against trees from
`tests/FastDelete.TestData` (deterministic seeds). Machine: Windows VM, 20 logical CPUs,
NTFS on SSD-backed storage. **This VM shows large run-to-run noise**; only
consistent patterns and same-session comparisons are meaningful.

## Delete-strategy shootout (tiny10k, w=8, 2026-09-28)

| Strategy                                        | Measured    | Verdict |
|-------------------------------------------------|-------------|---------|
| DeleteFileW classic (1 kernel call)             | ~7,200 i/s sweep / 12,300 i/s isolated | **winner for normal files** |
| NtSetInformationFile DispositionInfoEx          | ~5,400 i/s  | fallback for locked/read-only files (POSIX unlink, IGNORE_READONLY) |
| FILE_DELETE_ON_CLOSE                            | reported 58,000 i/s | **rejected**: file silently survived close (probe-verified); phantom speed |

Lesson: validate strategies with tests that check the disk afterwards, not timers.
The engine now deletes normal files via classic DeleteFileW and falls back to
DispositionEx only for sharing-locked files. 48/48 tests green.

## Final sweep — classic-first chain (best of 1)

| Profile   | w=1   | w=4   | w=8 (default) |
|-----------|-------|-------|---------------|
| tiny10k   | 5,197 | 6,067 | **7,221 i/s** |
| tiny100k  | 3,153 | 3,475 | **3,532 i/s** |
| deep1k    | 408   | 411   | **454 i/s**   |
| mixed     | 884   | 1,429 | **1,619 i/s** |

Default worker count is clamp(ProcessorCount / 2, 2, 8) — measured +30-40% over the
old cap of 4 on file-heavy trees.

## Explorer comparison — same tree (tiny10k, 10,100 items), same machine

| Method | Seconds | Items/s |
|--------|---------|---------|
| Explorer delete verb → Recycle Bin | 10.2 | ~988 |
| PowerShell Remove-Item -Recurse -Force | 5.6 | ~1,816 |
| **FastDelete v0.5.1** | **1.4** | **~7,200** |

## Historical decisions (kept for context)

1. **Directories are never deleted by parallel workers** — parallel RemoveDirectory
   convoys on NTFS metadata locks (deep1k measured 5× worse). Sequential post-order pass.
2. **Bounded channel with backpressure** — the walk must block when the channel fills;
   `TryWrite` silently dropped items (regression test: 20k-item tree).
3. **The Win32 SetFileInformationByHandle(FileDispositionInfoEx) wrapper rejects the
   class on some systems** — the engine uses `NtSetInformationFile` (class 64) instead.
4. **Symlink creation needs SeCreateSymbolicLinkPrivilege** — tests skip gracefully (1314);
   junctions cover the reparse path.

## Reproduce

```bat
bash dn.sh run --project tests/FastDelete.TestData -- --root %TEMP%\fdtrees --profile all
bash dn.sh run --project src/FastDelete.Benchmarks -- --profiles tiny10k,tiny100k,deep1k,mixed --workers 1,4,8
```
