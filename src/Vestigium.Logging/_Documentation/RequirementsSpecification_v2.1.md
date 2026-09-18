# Vestigium.Logging — Software Requirements Specification

**Document ID:** VEST-LOG-SRS-002  
**Version:** 2.1  
**Status:** Binding for package 1.6.x  
**Date:** 18 September 2026  
**Target:** .NET 10 LTS, Visual Studio 2026  
**Supersedes:** SRS 2.0 (18 September 2026)

## Revision history

| Version | Date | Package | Summary |
|---|---|---|---|
| 1.0–1.2 | 2026 | 1.2.x | Initial suite logger: JSONL, flood, taxonomy, disk tripwire, WPF pump. |
| 1.3 | 2026 | 1.3.0 | Owned VestigiumJsonlWriter. Serilog removed from runtime. |
| 1.4 | 2026-09-18 | 1.4.0 | Required EVENTID. Thrown. Information has no Exception parameter. |
| 1.5 / 2.0 | 2026-09-18 | 1.5.0 | Catalog 0–4999 / 5000+. Custom catalog CRUD, Load/Unload. SRS 2.0. |
| **2.1** | 2026-09-18 | 1.6.0 | Unflushed peek, Head/Tail, janitor, SHA256 archive. This document. |

Sections **1–18** of RequirementsSpecification_v2.0.md remain binding. This revision adds §§19–22 and extends §15, §17, and §18.

## 19. Unflushed disk queue (L0)

| ID | Requirement |
|---|---|
| R13 | Flush persists the disk queue and does not stop writes. |
| R14 | PendingDiskCount is queued, not yet on disk. |
| R15 | PeekPendingDisk(n) copies oldest queued lines and does not dequeue. |
| R16 | n < 1 throws. Cap is LogReadMaxLines (default 1,000, minimum 1). |
| R17 | No live follow / tail -f API. |

## 20. Persisted Head / Tail (L1)

VestigiumLogReader.Head/Tail. Files vestigium-{APPID}-*.json. Torn last line dropped. FileShare.ReadWrite. Offline requires directory + appId. Same n rules as R16.

## 21. Janitor (L2)

DeleteOlderThan(age). age > 0. Cutoff is yyyyMMdd stamp. Skip ActiveLogPath. Return Deleted, Bytes, SkippedOpen.

## 22. Archive (L3)

ArchiveOlderThan(age, archiveDirectory). Copy → uppercase SHA256 both files → sidecar `HEX  filename` → delete source on match. Mismatch leaves source, Failed++. Flush first to include the queue. No zip.

## 15 / 17 / 18 amendments

LogReadMaxLines default 1,000. Non-goals: live tail, zip archives, automatic janitor on Shutdown. Trace R13–R20 as above.
