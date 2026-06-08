# Collection Download Support - Design Document

## Problem Statement

Users frequently encounter torrent downloads containing an author's complete works (or large subsets). Current Readarr behavior:

1. **Text files** (epub/pdf): Each file becomes its own `LocalEdition` and is independently identified. This works but has no concept of "pick only what I want from this collection."
2. **Audio files**: Grouped by directory/tags into `LocalEdition` objects, but the grouping assumes a single audiobook download, not a collection.
3. **Import decisions**: All identified books get imported if they pass quality checks. There's no "I already have this, skip it" at the collection level — only at the individual file level via `AlreadyImportedSpecification`.

The result: when a user grabs a "Complete Works of Author X" torrent containing 40 books (and they only want 3), Readarr either:
- Imports everything (cluttering the library)
- Fails to process it properly because the folder parsing assumes a single release

## Current Architecture (Relevant Path)

```
CompletedDownloadService.Import()
  → DownloadedBooksImportService.ProcessFolder()
    → DiskScanService.GetBookFiles() [finds ALL book files recursively]
    → ImportDecisionMaker.GetImportDecisions()
      → MetadataTagService.ReadTags() [extract metadata from each file]
      → TrackGroupingService.GroupTracks() [split into LocalEdition groups]
      → IdentificationService.Identify() [match each group to a book]
      → Decision Specs [approve/reject each]
    → ImportApprovedBooks.Import() [move approved to library]
```

### Key Insight: TrackGroupingService

For **text files**, every file is treated as its own release (one file = one book). This is actually correct for collection handling — each epub/pdf IS a separate book.

For **audio files**, grouping happens by directory structure and tags. A collection download typically has one subdirectory per audiobook, which the existing disc-detection logic handles somewhat.

### Key Insight: Decision Specifications

The existing `AlreadyImportedSpecification` only checks if *this specific download* was already imported. It does NOT check if the book already exists in the library with adequate quality.

The `UpgradeSpecification` DOES check existing library files but currently only gates on quality — if the new file is same-or-worse quality, it's rejected. This is actually close to what we want.

## Proposed Solution

### Phase 1: Collection-Aware Import Filtering

**Goal:** When processing a download containing multiple books, only import books that are:
- Monitored (user wants them)
- Missing from library OR an upgrade over existing quality

**Changes Required:**

#### 1.1 New Specification: `WantedBookSpecification`

Location: `src/NzbDrone.Core/MediaFiles/BookImport/Specifications/`

```csharp
public class WantedBookSpecification : IImportDecisionEngineSpecification<LocalEdition>
{
    // Reject if:
    // - The matched book is NOT monitored
    // - The matched book already has files at or above cutoff quality
    // - The matched author is NOT in the library (unless AddUnmonitored is enabled)
}
```

This differs from existing specs:
- `UpgradeSpecification` operates per-file, not per-book
- `AlreadyImportedSpecification` checks download history, not library state
- We need a holistic "do I want this book?" check

#### 1.2 Collection Detection in ProcessFolder

Location: `src/NzbDrone.Core/MediaFiles/DownloadedBooksImportService.cs`

Add collection detection heuristic:
```csharp
private bool IsCollectionDownload(List<IFileInfo> bookFiles, ParsedBookInfo folderInfo)
{
    // A download is a "collection" if:
    // - Contains 3+ book files (text) or 3+ subdirectories with audio
    // - Folder name matches collection patterns:
    //   "Author Name - Complete Works"
    //   "Author Name - Discography"
    //   "Author Name (ebook collection)"
    //   Or simply: file count > expected for a single book
}
```

When a collection is detected:
- Set `ImportDecisionMakerConfig.IsCollection = true`
- Log clearly: "Detected collection download with N books"
- Apply stricter filtering (only import wanted/missing books)

#### 1.3 Import Mode: Selective (for collections)

For collection downloads, use **Copy** mode (not Move) by default:
- Don't delete the source folder (it may be seeding)
- Only copy out the desired books
- Track what was imported vs skipped

New config option: `CollectionImportMode` (Copy/Hardlink/Move)

#### 1.4 Enhanced Logging for Collections

When processing a collection, produce a clear summary:
```
Collection: "Author Name - Complete Works" (40 items)
  Importing: 3 books (missing from library)
  Skipping: 35 books (already in library at acceptable quality)
  Skipping: 2 books (not monitored)
  Failed: 0 books
```

### Phase 2: Improved File Identification for Collections

**Goal:** Better matching when collection files have inconsistent naming/tagging.

#### 2.1 Author-Scoped Identification

When we detect a collection is by a single author:
- Pre-fetch ALL editions for that author from the metadata source
- Match files against this complete catalog (faster, more accurate)
- Use stricter author matching (all files should match the same author)

#### 2.2 Enhanced Filename Parsing for Collections

Common collection filename patterns not currently handled well:
```
Author Name/
├── Series Name/
│   ├── 01 - Book Title.epub
│   ├── 02 - Book Title.epub
│   └── ...
├── Standalone/
│   ├── Book Title (2019).epub
│   └── ...
└── Short Stories/
    └── Collection Name.epub
```

Add parser patterns for:
- Series numbering prefixes: `01 - Title`, `Book 1 - Title`
- Year suffixes: `Title (2019)`
- Directory-as-series-name hints

#### 2.3 Audiobook Collection Directory Structure

Common audiobook collection patterns:
```
Author Name/
├── Book Title 1/
│   ├── Chapter 01.mp3
│   ├── Chapter 02.mp3
│   └── ...
├── Book Title 2/
│   ├── Part 1.m4b
│   └── Part 2.m4b
└── Book Title 3/
    └── Book Title 3.m4b  (single file)
```

Enhance `TrackGroupingService` to:
- Detect top-level = author, second-level = individual books
- Group all audio files in a subdirectory as one audiobook
- Handle mixed single-file (.m4b) and multi-file (.mp3) audiobooks

### Phase 3: UI & User Control

#### 3.1 Manual Import Enhancement

The existing Manual Import UI should show:
- Collection detection indicator
- Per-book import toggle (checkbox)
- "Import Only Missing" button
- Quality comparison column (existing vs new)

#### 3.2 Collection Import Preview API

New API endpoint: `POST /api/v1/manualimport/collection`
- Returns structured list of identified books in the collection
- For each: match confidence, existing library status, quality comparison
- Allows user to cherry-pick before import

#### 3.3 Import List Integration

When a collection is detected, offer to:
- Add all identified authors to the library (monitored or unmonitored)
- Add all identified books as wanted
- Show "new discoveries" — books/authors not yet in library

### Phase 4: Seeding-Aware Import

#### 4.1 Hardlink/Reflink Support for Collections

Since collection torrents are often large and long-seeded:
- Default to hardlink when source and dest are same filesystem
- Fall back to reflink (btrfs/xfs) if available
- Only copy as last resort
- Never move/delete files from seeding downloads

#### 4.2 Download Client Integration

Track per-file import status:
- Mark which files from a collection have been imported
- Allow re-processing if new books are added to monitoring
- Integration with torrent clients' file priority (skip unwanted files before download)

## Implementation Priority

| Phase | Effort | Impact | Priority |
|-------|--------|--------|----------|
| 1.1 WantedBookSpecification | Low | High | P0 |
| 1.2 Collection Detection | Low | High | P0 |
| 1.3 Selective Import Mode | Medium | High | P0 |
| 1.4 Enhanced Logging | Low | Medium | P1 |
| 2.1 Author-Scoped Identification | Medium | High | P1 |
| 2.2 Enhanced Filename Parsing | Medium | Medium | P1 |
| 2.3 Audio Collection Grouping | Medium | High | P1 |
| 3.1 Manual Import UI | High | High | P2 |
| 3.2 Collection API | Medium | Medium | P2 |
| 3.3 Import List Integration | Medium | Low | P3 |
| 4.1 Hardlink/Reflink | Low | High | P1 |
| 4.2 Download Client Integration | High | Medium | P3 |

## Data Model Changes

### New: `CollectionImport` table (tracking)
```sql
CREATE TABLE CollectionImports (
    Id INTEGER PRIMARY KEY,
    DownloadId TEXT NOT NULL,
    SourcePath TEXT NOT NULL,
    DetectedBookCount INTEGER NOT NULL,
    ImportedBookCount INTEGER NOT NULL,
    SkippedBookCount INTEGER NOT NULL,
    FailedBookCount INTEGER NOT NULL,
    ProcessedDate DATETIME NOT NULL
);
```

### Modified: `ImportDecisionMakerConfig`
```csharp
public class ImportDecisionMakerConfig
{
    public bool NewDownload { get; set; }
    public bool SingleRelease { get; set; }
    public bool IsCollection { get; set; }         // NEW
    public bool ImportOnlyWanted { get; set; }     // NEW (default: true for collections)
    public bool IncludeExisting { get; set; }
}
```

## Configuration

New settings in `config.xml` / UI Settings:
```
Import.CollectionHandling = true (default)
Import.CollectionThreshold = 3 (min books to trigger collection mode)
Import.CollectionMode = Hardlink|Copy|Move (default: Hardlink)
Import.CollectionImportUnmonitored = false (default)
```

## Risks & Mitigations

1. **False positive collection detection**: A single audiobook with 40 chapter files could be misidentified as a collection. Mitigation: Check tag consistency — if all files share the same book title tag, it's one book, not a collection.

2. **Identification accuracy at scale**: Matching 40 books from file names alone may produce errors. Mitigation: Use metadata tags (ISBN/ASIN) first, fall back to title matching, require higher confidence threshold for auto-import from collections.

3. **Performance with large collections**: Some complete-works torrents have 100+ books. Mitigation: Batch metadata lookups, cache author catalog, parallelize identification where possible.

4. **Seeding impact**: Moving files breaks seeding. Mitigation: Default to hardlink for collections, warn if move is selected and download is still seeding.
