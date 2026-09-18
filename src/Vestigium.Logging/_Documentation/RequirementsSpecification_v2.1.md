# Vestigium.Logging — SRS 2.1 (addendum to 2.0)

**Document ID:** VEST-LOG-SRS-002.1  
**Package:** 1.6.0  
**Adds:** unflushed peek, head/tail, janitor, archive.

## L0 Unflushed queue

- `Flush()` / `Flush(timeout)` persist the disk queue and do not stop writes.
- `PendingDiskCount` is queued, not yet on disk.
- `PeekPendingDisk(n)` copies n lines (oldest first) and does not dequeue.
- `n < 1` throws. `n` is capped by `LogReadMaxLines` (default 1000, host-configurable, minimum 1).
- No live tail.

## L1 Reader

`VestigiumLogReader.Head/Tail(n, directory?, appId?)`. Files `vestigium-{APPID}-*.json` in name order. Torn last line skipped. `FileShare.ReadWrite`. Offline requires directory + appId.

## L2 Janitor

`VestigiumLogJanitor.DeleteOlderThan(age, directory?, appId?)`. Age > 0. Cutoff is UTC date from the `yyyyMMdd` stamp. Skips `ActiveLogPath` and locked files. Returns Deleted, Bytes, SkippedOpen.

## L3 Archive

`VestigiumLogArchive.ArchiveOlderThan(age, archiveDirectory, directory?, appId?)`. Copy → SHA256 source and copy (`Convert.ToHexString`, uppercase) → sidecar `{file}.sha256` as `HEX  filename` → delete source. Hash mismatch leaves source and increments Failed. No zip. No live tail.

## Bindings carried from 2.0

Schema, catalog 0–4999 / 5000+, Thrown, custom catalog CRUD + Load/Unload remain as in SRS 2.0.
