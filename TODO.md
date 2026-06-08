# Readarr Revitalization — TODO

## Phase 1: Update Dependencies

The project is on .NET 6 (EOL) with outdated NuGet packages and React 17. Modernize the stack.

### 1.1 Backend — Target Framework Upgrade
- [ ] Update `Directory.Build.props`: change `TargetFrameworks` from `net6.0` to `net9.0`
- [ ] Update `NzbDrone.Host/Readarr.Host.csproj` TFM to `net9.0`
- [ ] Update all other `.csproj` files that specify `net6.0` explicitly
- [ ] Remove `SourceLink.GitHub` workaround comment (built-in on .NET 8+)
- [ ] Fix any breaking API changes from .NET 6→9 (minimal — mostly internal)

### 1.2 Backend — NuGet Package Updates (`Directory.Packages.props`)
- [ ] Microsoft.AspNetCore.SignalR.Client: 6.0.35 → 9.0.x
- [ ] Microsoft.Extensions.*: 6.0.x → 9.0.x (Caching, Config, DI, Hosting, Logging)
- [ ] Microsoft.NET.Test.Sdk: 17.10.0 → 17.12.x
- [ ] Microsoft.Data.SqlClient: 2.1.7 → 6.0.x
- [ ] Npgsql: 7.0.10 → 9.0.x
- [ ] NLog: 5.1.4 → 5.4.x; NLog.Extensions.Logging: 5.2.3 → 5.4.x
- [ ] Newtonsoft.Json: 13.0.3 → 13.0.4 (or migrate to System.Text.Json)
- [ ] FluentValidation: 9.5.4 → 11.x
- [ ] RestSharp: 106.15.0 → 112.x (major breaking change — new API)
- [ ] Dapper: 2.0.151 → 2.1.x
- [ ] MailKit: 4.8.0 → 4.10.x
- [ ] SixLabors.ImageSharp: 3.1.7 → 3.1.8
- [ ] SharpZipLib: 1.4.2 → 1.4.3
- [ ] Polly: 8.5.2 → 8.5.x (already recent)
- [ ] Sentry: 4.0.2 → 5.x
- [ ] Moq: 4.17.2 → 4.20.x; FluentAssertions: 5.10.3 → 7.x
- [ ] NUnit: 3.14.0 → 4.x; NUnit3TestAdapter → NUnit4TestAdapter
- [ ] System.IO.Abstractions: 17.0.24 → 21.x
- [ ] System.Data.SQLite.Core.Servarr: 1.0.115.5-18 → latest
- [ ] DryIoc.dll: 5.4.3 → 5.6.x; DryIoc.Microsoft.DependencyInjection: 6.2.0 → 8.x
- [ ] Remove System.Buffers, System.Memory, System.ValueTuple (inbox on net9.0)
- [ ] Remove System.Text.Json explicit ref (inbox on net9.0)
- [ ] Update StyleCop.Analyzers: 1.1.118 → 1.2.0-beta

### 1.3 Backend — RestSharp Migration (Breaking)
RestSharp 106→112 is a complete rewrite. This affects:
- [ ] `NzbDrone.Common/Http/` — HTTP client infrastructure
- [ ] `NzbDrone.Core/MetadataSource/` — API calls
- [ ] `NzbDrone.Core/Download/` — download client API calls
- [ ] `NzbDrone.Core/Notifications/` — notification API calls
- [ ] `NzbDrone.Core/Indexers/` — indexer API calls

**Strategy:** The Servarr codebase uses a custom `IHttpClient` wrapper. RestSharp is only used
in a few places directly. Most HTTP goes through `HttpClient` already. Audit and remove RestSharp
where possible, replacing with the built-in `IHttpClient`.

### 1.4 Frontend — Dependency Updates
- [ ] React: 17.0.2 → 18.3.x (concurrent mode, automatic batching)
- [ ] react-dom: 17.0.2 → 18.3.x
- [ ] react-redux: 7.2.4 → 9.x (hooks-first API)
- [ ] redux: 4.2.1 → 5.x (or migrate to @reduxjs/toolkit)
- [ ] react-router + react-router-dom: 5.2.0 → 6.x (breaking: new API)
- [ ] connected-react-router: 6.9.3 → REMOVE (incompatible with RR6)
- [ ] TypeScript: 5.1.6 → 5.7.x
- [ ] @microsoft/signalr: 6.0.25 → 9.0.x (match backend)
- [ ] @sentry/browser: 7.x → 9.x
- [ ] Webpack: 5.95.0 → 5.97.x (or migrate to Vite)
- [ ] ESLint: 8.57.1 → 9.x (flat config)
- [ ] Replace moment.js → dayjs (moment is deprecated, 70KB smaller)
- [ ] Replace jquery: 3.7.1 → REMOVE (not needed with React)
- [ ] Update @fortawesome/* to latest 6.7.x
- [ ] Update Babel stack to latest 7.26.x

### 1.5 CI/CD Pipeline
- [ ] Replace azure-pipelines.yml with GitHub Actions
- [ ] Add .NET 9 SDK to CI matrix
- [ ] Add frontend build + lint step
- [ ] Add Docker build step

---

## Phase 2: Transition to Open Library Metadata Source

Replace the defunct Goodreads integration with Open Library's free, open API.

### 2.1 Open Library API Client
- [ ] Create `src/NzbDrone.Core/MetadataSource/OpenLibrary/` directory
- [ ] Implement `OpenLibraryProxy.cs` — main API client
  - Search endpoint: `https://openlibrary.org/search.json?q={query}`
  - Works endpoint: `https://openlibrary.org/works/{olid}.json`
  - Editions endpoint: `https://openlibrary.org/books/{olid}.json`
  - Authors endpoint: `https://openlibrary.org/authors/{olid}.json`
  - ISBN endpoint: `https://openlibrary.org/isbn/{isbn}.json`
  - Covers: `https://covers.openlibrary.org/b/{key}/{value}-{size}.jpg`
- [ ] Implement rate limiting (100 req/5min per IP for unauthenticated)
- [ ] Implement response caching (CachedHttpResponseService)
- [ ] Create resource models mapping OL JSON → internal types:
  - `OpenLibraryWorkResource.cs`
  - `OpenLibraryEditionResource.cs`
  - `OpenLibraryAuthorResource.cs`
  - `OpenLibrarySearchResultResource.cs`

### 2.2 Interface Implementation
- [ ] Implement `IProvideAuthorInfo` → map OL author to `Author` + books
  - OL Author → `AuthorMetadata` (name, bio, images, links)
  - OL Works by author → `List<Book>`
- [ ] Implement `IProvideBookInfo` → map OL work/edition to `Book`
  - OL Work → `Book` (title, subjects/genres, first publish date)
  - OL Editions → `List<Edition>` (ISBN, format, publisher, pages, language)
- [ ] Implement `ISearchForNewAuthor` → OL author search
- [ ] Implement `ISearchForNewBook` → OL search with title/author/ISBN
- [ ] Implement `ISearchForNewEntity` → unified search
- [ ] Implement `IProvideSeriesInfo` → OL series/subject mapping

### 2.3 ID Mapping Strategy
- [ ] Define `ForeignBookId` format: `OL{work_id}W` (e.g., `OL45804W`)
- [ ] Define `ForeignEditionId` format: `OL{edition_id}M` (e.g., `OL7353617M`)
- [ ] Define `ForeignAuthorId` format: `OL{author_id}A` (e.g., `OL23919A`)
- [ ] Add migration (041) to add `OpenLibraryId` column to relevant tables
- [ ] Implement ISBN→OL edition lookup for existing library matching

### 2.4 Data Enrichment
- [ ] Map OL subjects → genres (with normalization/deduplication)
- [ ] Map OL covers → MediaCover images (S/M/L sizes)
- [ ] Map OL links → book/author links
- [ ] Parse OL "physical_format" field → Edition.Format
- [ ] Parse OL "number_of_pages" → Edition.PageCount
- [ ] Handle OL's "identifiers" map (ISBN-10, ISBN-13, OCLC, LCCN, etc.)

### 2.5 Remove Goodreads Dependencies
- [ ] Remove `src/NzbDrone.Core/MetadataSource/Goodreads/` entirely
- [ ] Remove `src/NzbDrone.Core/MetadataSource/GoodreadsSearchProxy/`
- [ ] Update `src/NzbDrone.Core/ImportLists/Goodreads/` → convert to OL lists or remove
- [ ] Update `src/NzbDrone.Core/Notifications/Goodreads/` → remove (OL has no shelf API)
- [ ] Remove Goodreads OAuth settings from configuration
- [ ] Add migration (042) to clean up Goodreads-specific settings from DB

### 2.6 Tests
- [ ] Unit tests for OL resource deserialization (sample JSON fixtures)
- [ ] Unit tests for OL→internal model mapping
- [ ] Unit tests for search result ranking/deduplication
- [ ] Integration tests against OL API (optional, gated on CI flag)

---

## Phase 3: Seamless Audiobook + Text Format Management

Make Readarr a unified manager for both ebook and audiobook editions of the same work.

### 3.1 Data Model Changes
- [ ] Add `EditionFormat` enum: `Text`, `Audio`, `Unknown`
- [ ] Add `EditionFormat Format` property to `Edition` model (DB column)
- [ ] Add `Duration` (TimeSpan?) property to `Edition` for audiobook length
- [ ] Add `Narrator` (string) property to `Edition` for audiobook narrator
- [ ] Add `IsAbridged` (bool) property to `Edition`
- [ ] Migration (043): Add `Format`, `Duration`, `Narrator`, `IsAbridged` columns to Editions table
- [ ] Update `Edition.UseMetadataFrom()` to handle new fields

### 3.2 Quality System Enhancement
- [ ] Add new audio qualities: `AAC`(13), `OGG`(14), `OPUS`(15)
- [ ] Create `QualityGroup` concept: "Any Text" and "Any Audio" for profile flexibility
- [ ] Update `MediaFileExtensions` to map `.opus`, `.aac` properly
- [ ] Update quality profiles UI to show text/audio sections clearly
- [ ] Migration (044): Add new quality definitions to profiles

### 3.3 Multi-Format Download Management
- [ ] Add per-book "Wanted Formats" setting: Text Only / Audio Only / Both
- [ ] Update `BookMonitoredService` to track format-specific completion
- [ ] Update book status indicators: separate "have text" / "have audio" states
- [ ] Update search to filter by format when searching for specific edition types
- [ ] Add format-aware duplicate detection (text EPUB + audio M4B = NOT duplicate)

### 3.4 Library Organization
- [ ] Add root folder setting: "Separate audio/text folders" option
  - e.g., `/books/Author/Title/Title.epub` + `/audiobooks/Author/Title/Title.m4b`
  - OR unified: `/library/Author/Title/Title.epub` + `/library/Author/Title/Title.m4b`
- [ ] Update `FileNameBuilder` to support format-aware naming tokens:
  - `{Format}` → "ebook" or "audiobook"
  - `{Narrator}` → audiobook narrator
  - `{Duration}` → total runtime
- [ ] Update file scanning to detect and categorize both formats

### 3.5 Audiobook-Specific Features
- [ ] Parse M4B chapter markers (via TagLib#) and store chapter count
- [ ] Parse multi-file audiobooks (folder of MP3s = single audiobook)
- [ ] Support `.cue` files for single-file-with-chapters format
- [ ] Audiobook-specific naming: include Part/Disc number for multi-file
- [ ] Update `AudioTagService` to read/write audiobook-specific tags:
  - Narrator, Series, Series position, Unabridged/Abridged
- [ ] Detect "audiobook" in release titles during parsing

### 3.6 Import & Identification
- [ ] Update import pipeline to classify incoming files as text/audio
- [ ] Match audiobooks to existing works (same `Book`, different `Edition`)
- [ ] Handle multi-disc/multi-file audiobook import as single edition
- [ ] Audiobook fingerprinting: match by duration + narrator when metadata sparse

### 3.7 UI Updates
- [ ] Book detail page: show both text and audio editions with separate status
- [ ] Library grid: format badges (📖 / 🎧) on book cards
- [ ] Add filter: "Missing Audiobook" / "Missing Ebook" / "Has Both"
- [ ] Quality profile editor: grouped by format (Text group / Audio group)
- [ ] Wanted/Missing page: filter by format type
- [ ] Calendar: show which format is releasing

### 3.8 API Updates
- [ ] `GET /api/v1/book` — include format information in edition data
- [ ] `GET /api/v1/wanted/missing` — add `format` filter parameter
- [ ] `POST /api/v1/command` — search commands accept format parameter
- [ ] Update Swagger/OpenAPI definitions

### 3.9 Tests
- [ ] Unit tests for multi-format book state calculations
- [ ] Unit tests for audiobook file detection and chapter parsing
- [ ] Unit tests for format-aware naming templates
- [ ] Unit tests for multi-file audiobook import grouping
- [ ] Integration tests for format-filtered searches
