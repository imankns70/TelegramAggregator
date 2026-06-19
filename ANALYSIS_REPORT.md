# TelegramAggregator — Full Codebase Analysis Report

**Date:** 2026-06-19  
**Files analyzed:** All source files under `src/TelegramAggregator.WebAPI/`

---

## 🔴 Critical Bugs (Fixed)

### 1. `GetByIdAsync` ignores its `id` parameter
**File:** `Data/Repositories/NewsRepository.cs`  
**Bug:** `FirstOrDefaultAsync(ct)` had no predicate — always returned the first record in the DB, regardless of the requested ID.  
**Fix:** Added `a => a.Id == id` predicate to `FirstOrDefaultAsync`.

---

### 2. `InQueueAsync` always logs a failure message — even on success
**File:** `NewsRepository.cs`  
**Bug:** The `_logger.LogDebug(...)` line for "not found" was outside the `if (news != null)` block, so it ran unconditionally — even after successfully updating the entity.  
**Fix:** Moved the log into a proper `else` branch and changed severity to `LogWarning`.

---

### 3. `ChangeNewsInQueueToExpiredState` — wrong date direction expires today's news
**File:** `NewsRepository.cs`  
**Bug:** Filter was `n.InsertDate.Date >= yest.Date` ("yesterday or newer") — this also expired today's freshly-inserted news.  
**Fix:** Changed to `n.InsertDate.Date < todayUtc` ("inserted before today").

---

### 4. `Task.Delay(TimeSpan.FromSeconds(3000))` — 50-minute freeze on Telegram error
**File:** `Services/FootballforiService.cs`  
**Bug:** On a Telegram send failure the delay was `FromSeconds(3000)` (50 minutes), not 3 seconds. The publish loop was effectively dead for nearly an hour after any error.  
**Fix:** Changed to `TimeSpan.FromSeconds(3)`.

---

### 5. `NullReferenceException` on `item.FullDescription.Contains(...)`
**File:** `Services/FootballforiService.cs`  
**Bug:** Football360 items skip the full-description fetch, leaving `FullDescription` potentially null. The subsequent `.Contains("خطا در دریافت محتوا")` call throws at runtime.  
**Fix:** Added `item.FullDescription != null &&` guard before `.Contains(...)`.

---

## 🟠 Memory Leaks (Fixed)

### 6. `TextSimilarity.LevenshteinDistance` — O(n×m) 2D array allocation per call
**File:** `Infrastructure/TextSimilarity.cs`  
**Bug:** The Levenshtein implementation allocated a full `int[n+1, m+1]` 2D array per comparison. With long article texts (title + shortDesc + fullDesc can be 2000+ chars) and comparing each new item against all DB items from the last 8 hours, this produces repeated large allocations that pressure the GC.  
**Fix:** Replaced with a two-row rolling algorithm (`int[] prev, curr`) — O(min(n,m)) space. Also added a fast-exit based on length ratio: if the two strings differ in length by >10%, they cannot be ≥90% similar, so skip Levenshtein entirely.

---

### 7. Unmanaged stream in `TryHtmlForCheckVideoAsync`
**File:** `Services/HttpHelperService.cs`  
**Bug:** `var stream = await response.Content.ReadAsStreamAsync(ct)` was not disposed (`using` was missing).  
**Fix:** Changed to `using var stream = ...`.

---

### 8. EF Change Tracker bloat from N individual `SaveChangesAsync` calls
**File:** `NewsRepository.cs — ReceiveRangeAsync`  
**Bug:** Every news item was added to the DbContext and saved immediately in a loop, keeping all tracked entities alive in the change tracker across the entire batch.  
**Fix:** See Bottleneck #9 — all entities added first, single `SaveChangesAsync` at the end.

---

## 🟡 Performance Bottlenecks (Fixed)

### 9. `ReceiveRangeAsync` — N separate DB inserts + no dedup check
**File:** `NewsRepository.cs`  
**Bug:** Each news item triggered its own `SaveChangesAsync`. 20 items = 20 round trips. Also, items already in the DB were re-inserted (duplicates caught downstream by unique constraints).  
**Fix:** Pre-check existing links in one query, then batch-add all new entities and call `SaveChangesAsync` once.

---

### 10. `InQueueRangeAsync` — N+1 DB reads
**File:** `NewsRepository.cs`  
**Bug:** `InQueueAsync` was called in a loop, each call issuing a separate `FirstOrDefaultAsync` to load the entity by ID. 20 items = 20 queries.  
**Fix:** Replaced with bulk-load (`WHERE Id IN (...)`) followed by dictionary lookup — 1 query total.

---

### 11. `ProcessNewsAsync` — synchronous DB query on async thread
**File:** `NewsRepository.cs`  
**Bug:** `GetAll().Where(...).ToList()` (no `await`, no `Async`) blocked a thread-pool thread while waiting for Postgres.  
**Fix:** Changed to `await GetAll().Where(...).AsNoTracking().ToListAsync(ct)`.

---

### 12. `Football360ScraperService` — 10 sequential HTTP fetches for article detail pages
**File:** `Services/Football360ScraperService.cs`  
**Bug:** Articles were fetched one at a time in a `foreach` loop. With 10 articles and ~1-2s latency each, fetch time was 10-20 seconds sequentially.  
**Fix:** Replaced with `Task.WhenAll(uniqueNodes.Select(...))` — all 10 pages fetched in parallel.

---

### 13. Same article URL fetched 3+ times
**File:** `Services/FootballforiService.cs` + `Football360ScraperService.cs`  
**Not fixed (architectural):** For Football360 items, the detail page is fetched once in the scraper, then again in `ExtractMainImageAsync`, then again in `HasVideoAsync`. For non-Football360 items it's fetched in `GetStringAsync` (image), `TryHtmlForCheckVideoAsync` (video), and `GetFullDescriptionAsync`. Consider caching the HTML response per URL within a single publish cycle.

---

### 14. `ScoreCalculator` — derby keywords array re-created per call
**File:** `Infrastructure/ScoreCalculator.cs`  
**Bug:** `var derbyKeywords = new[] { ... }` inside `IsDerbyMatch` allocated a new array on every score calculation.  
**Fix:** Moved to `private static readonly string[] DerbyKeywords`.

---

### 15. `DecayRangeAsync` — missing `CancellationToken` on `SaveChangesAsync`
**File:** `NewsRepository.cs`  
**Bug:** `await _context.SaveChangesAsync()` without `ct` — DB call could not be cancelled.  
**Fix:** Added `ct` parameter.

---

## 🔵 Business Logic Issues

### 16. `GetBestNews` picks oldest news among equal-score items
**File:** `NewsRepository.cs`  
**Bug:** `ThenBy(n => n.PubDate)` sorts ascending, so the oldest item wins when scores are tied. News becomes progressively more stale.  
**Fix:** Changed to `ThenByDescending(n => n.PubDate)`.

---

### 17. `PublishLoopAsync` — production delay was commented out (15s vs. 20-40min)
**File:** `BackgroundServices/FootballforiBackgroundService.cs`  
**Bug:** `await Task.Delay(TimeSpan.FromSeconds(15))` was left active with the random-minute delay commented out. The bot was posting every 15 seconds in production — potential channel spam and Telegram rate-limiting.  
**Fix:** Restored `TimeSpan.FromMinutes(randomId)` delay (20-40 min) as the active path.

---

### 18. `FullDecriptionExtractorService.GetFullDescriptionAsync` — no `CancellationToken`
**File:** `Services/FullDecriptionExtractorService.cs`  
**Bug:** The method signature lacked `CancellationToken`, so the HTTP call inside could not be cancelled when the app shuts down.  
**Fix:** Added `CancellationToken ct = default` to the interface and implementation; passed `ct` to the inner HTTP call.

---

### 19. `GetBestNews` — missing `.Include(n => n.Histories)` before `DecayAsync`
**File:** `NewsRepository.cs`  
**Bug:** `GetBestNews` loaded a `News` entity without its `Histories` collection. When `DecayAsync` later calls `news.Histories.Add(...)`, if the collection is not loaded it may throw or silently lose history entries depending on lazy-loading config.  
**Fix:** Added `.Include(n => n.Histories)` to `GetBestNews`.

---

### 20. `NewsBalancer` class is defined but never used
**File:** `Infrastructure/NewsBalancer.cs`  
**Observation:** The entire balancing logic is dead code. News items go directly from RSS → `ProcessNewsAsync` → `InQueueRangeAsync` without ever calling `BalanceNews`. If source diversity is a goal (it appears to be, given the balancer exists), it needs to be wired into the flow.

---

### 21. Working-hours inconsistency between fetch and publish loops
**File:** `BackgroundServices/FootballforiBackgroundService.cs`  
**Observation:**
- **FetchLoop** stops: 01:00–07:59 Iran time
- **PublishLoop** runs: 00:00–02:59 and 08:00–23:59 Iran time

Between 00:00–00:59: both fetch and publish run (fine).  
Between 01:00–02:59: publish runs but fetch is stopped — queue drains with no new content.  
Between 03:00–07:59: neither runs.  
**Suggestion:** Align: stop both loops at 01:00, restart at 08:00.

---

### 22. `GetSportSourceId` uses hardcoded RSS title strings — fragile
**File:** `NewsRepository.cs`  
**Bug:** Source identification relies on exact string matching of the RSS feed's `<title>` element. If a source changes its channel title, all items from that source silently fall through to the default (`Football360`), causing wrong scoring and incorrect duplicate-detection.  
**Suggestion:** Map by URL or a stable identifier from `SourceConfig`, not the human-readable title.

---

## Summary Table

| # | Severity | Area | File | Fixed |
|---|----------|------|------|-------|
| 1 | 🔴 Bug | GetByIdAsync ignores ID | NewsRepository.cs | ✅ |
| 2 | 🔴 Bug | InQueueAsync logs error on success | NewsRepository.cs | ✅ |
| 3 | 🔴 Bug | Expire job expires today's news | NewsRepository.cs | ✅ |
| 4 | 🔴 Bug | 3000s delay on Telegram error (50 min) | FootballforiService.cs | ✅ |
| 5 | 🔴 Bug | NullRef on FullDescription.Contains | FootballforiService.cs | ✅ |
| 6 | 🟠 Memory | O(n×m) Levenshtein array per comparison | TextSimilarity.cs | ✅ |
| 7 | 🟠 Memory | Unmanaged stream in TryHtmlForCheckVideoAsync | HttpHelperService.cs | ✅ |
| 8 | 🟠 Memory | EF change tracker bloat (N SaveChanges) | NewsRepository.cs | ✅ |
| 9 | 🟡 Perf | N separate DB inserts + no dedup | NewsRepository.cs | ✅ |
| 10 | 🟡 Perf | N+1 DB reads in InQueueRangeAsync | NewsRepository.cs | ✅ |
| 11 | 🟡 Perf | Synchronous DB query on async thread | NewsRepository.cs | ✅ |
| 12 | 🟡 Perf | 10 sequential HTTP fetches (Football360) | Football360ScraperService.cs | ✅ |
| 13 | 🟡 Perf | Same URL fetched 3+ times | FootballforiService.cs | ⚠️ Noted |
| 14 | 🟡 Perf | Derby keywords array re-created per call | ScoreCalculator.cs | ✅ |
| 15 | 🟡 Perf | Missing CancellationToken in SaveChangesAsync | NewsRepository.cs | ✅ |
| 16 | 🔵 Logic | GetBestNews picks OLDEST on score tie | NewsRepository.cs | ✅ |
| 17 | 🔵 Logic | 15s publish delay left active (was 20-40min) | BackgroundService.cs | ✅ |
| 18 | 🔵 Logic | GetFullDescriptionAsync missing CancellationToken | FullDecriptionExtractorService.cs | ✅ |
| 19 | 🔵 Logic | GetBestNews missing Include(Histories) | NewsRepository.cs | ✅ |
| 20 | 🔵 Logic | NewsBalancer never called — dead code | NewsBalancer.cs | ⚠️ Noted |
| 21 | 🔵 Logic | Fetch/publish hour mismatch | BackgroundService.cs | ⚠️ Noted |
| 22 | 🔵 Logic | Source ID by RSS title string — fragile | NewsRepository.cs | ⚠️ Noted |
