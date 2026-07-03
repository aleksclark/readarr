# Readarr Forks Comparison

**Test Date:** 2026-07-03 (in progress — identification phase)  
**Audiobook Library:** 52 authors, ~16,234 audio files  
**eBook Library:** 6 authors  
**Host:** node-3 (MooseFS, 32GB RAM)

## Forks Tested

| Fork | Image | Metadata Source | Status |
|------|-------|-----------------|--------|
| aleksclark | `ghcr.io/aleksclark/readarr:develop` | Open Library (direct) | Identifying (977/1794) — NullRef bug fixed in latest |
| bookshelf-gr | `ghcr.io/pennydreadful/bookshelf:softcover-v0.4.20.129` | api.bookinfo.pro (GoodReads) | Identifying (39/1764) — rate-limited by 429s |
| bookshelf-hc | `ghcr.io/pennydreadful/bookshelf:hardcover-v0.4.20.129` | hardcover.bookinfo.pro | Identifying (43/1764) — rate-limited |
| faustvii | `ghcr.io/faustvii/readarr:latest` | api.bookinfo.pro | FAILED — runs as `nobody`, can't access media dirs |

## Early Observations

### Speed
- **aleksclark (Open Library):** ~20x faster identification — processed 977 books in ~25 min
- **bookshelf (bookinfo.pro):** Only 39-43 books in ~25 min due to API rate limiting (HTTP 429, 103s backoffs)
- **faustvii:** Could not run — image runs as user `nobody` and requires writable media paths

### Reliability
- **aleksclark:** Had a NullReferenceException in `LocalEdition.PopulateMatch()` causing 99% identification failure (now fixed in `60a6007`)
- **bookshelf-gr/hc:** Very stable — only 7 errors total across all attempts
- **faustvii:** Non-functional without permission adjustments

### Key Takeaway
Open Library provides much faster metadata lookups (no rate limiting), but our fork had a critical bug in the identification pipeline. With the fix deployed, the aleksclark fork should dramatically outperform other forks on bulk library scans while maintaining comparable matching quality.

## Methodology

1. Start each fork with a completely fresh PostgreSQL database (tmpfs — no persistence)
2. Mount audiobooks library at `/audiobooks` (rw for Readarr validation)
3. Mount ebooks library at `/ebooks` (rw for Readarr validation)
4. Add root folders with default quality/metadata profiles via API
5. Trigger `RescanFolders` command with `filter=none` and `addNewAuthors=true`
6. Wait for identification phase to complete
7. Query API for final author/book/file counts

### Command Used
```json
{"name": "RescanFolders", "filter": "none", "addNewAuthors": true}
```

### What happens during scan
1. **File Reading** (16,349 files) — takes ~15 min, all forks equal speed
2. **Identification** (1,764-1,794 book groups) — speed depends on metadata source
3. **Author creation** — happens after identification batch completes
4. **File linking** — matches files to identified books/editions

## Reproduction

```bash
# On a node with MooseFS mounted at /mnt/moosefs
cd tests/forks-comparison
./run-comparison.sh
```

Requirements: `docker compose`, `jq`, `curl`, `bc`.

## Notes

- The bookinfo.pro service rate-limits at ~1 req/2-3s with 103s backoff on 429
- Open Library has no rate limiting for reasonable usage patterns
- Faustvii image requires PUID/PGID matching the media dir ownership
- Full comparison (all forks reaching 100% identification) estimated at 4-6 hours due to rate limits
