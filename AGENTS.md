# Readarr — Development Guide

Instructions for AI coding assistants and developers working on the Readarr codebase.

## Overview

Readarr is a book manager and automation tool (the "Sonarr for ebooks/audiobooks"). It monitors
for new releases, searches indexers, manages downloads, and organizes your library. Built on the
Servarr platform shared with Sonarr/Radarr/Lidarr.

**Stack:** .NET 6 (C#), ASP.NET Core, SQLite/Postgres, React 17 + Redux + TypeScript frontend.

## Quick Start

```bash
# Backend
cd src
dotnet build Readarr.sln

# Run (development)
dotnet run --project NzbDrone.Console/Readarr.Console.csproj

# Tests
dotnet test Readarr.sln --filter "Category!=IntegrationTest"

# Frontend
yarn install
yarn start   # dev server with HMR
yarn build   # production build
yarn lint    # eslint + stylelint
```

## Project Structure

```
readarr/
├── src/
│   ├── Readarr.sln                           # Solution file
│   ├── Directory.Build.props                 # Shared build properties (TFM, output paths)
│   ├── Directory.Packages.props              # Central package version management
│   ├── NzbDrone.Core/                        # Core business logic (THE most important project)
│   │   ├── Books/                            # Domain models + services
│   │   │   ├── Model/                        # Book, Author, Edition, Series entities
│   │   │   ├── Services/                     # AddAuthor, AddBook, RefreshAuthor, etc.
│   │   │   └── Commands/                     # CQRS command definitions
│   │   ├── MetadataSource/                   # External metadata providers
│   │   │   ├── IProvideAuthorInfo.cs         # Author lookup interface
│   │   │   ├── IProvideBookInfo.cs           # Book lookup interface
│   │   │   ├── ISearchForNewAuthor.cs        # Author search interface
│   │   │   ├── ISearchForNewBook.cs          # Book search interface
│   │   │   ├── ISearchForNewEntity.cs        # Generic search interface
│   │   │   ├── Goodreads/                    # LEGACY: Goodreads XML API proxy
│   │   │   └── GoodreadsSearchProxy/         # LEGACY: Goodreads JSON search
│   │   ├── MediaFiles/                       # File import, organization, tagging
│   │   │   ├── AudioTagService.cs            # ID3/M4B tag reading/writing (TagLib#)
│   │   │   ├── MetadataTagService.cs         # Ebook metadata (Calibre-style)
│   │   │   ├── MediaFileExtensions.cs        # Supported extensions + quality mapping
│   │   │   └── BookImport/                   # Import pipeline + identification
│   │   ├── Qualities/                        # Quality definitions (PDF, EPUB, MP3, FLAC, M4B...)
│   │   ├── Datastore/                        # ORM, migrations, table definitions
│   │   │   └── Migration/                    # Numbered FluentMigrator migrations (001-040)
│   │   ├── Download/                         # Download client integration
│   │   ├── Indexers/                         # Indexer/search integration
│   │   ├── ImportLists/                      # External list sources (Goodreads, etc.)
│   │   ├── Notifications/                    # Notification providers
│   │   ├── Organizer/                        # File naming + path organization
│   │   └── Parser/                           # Release title parsing + quality detection
│   ├── NzbDrone.Common/                      # Shared utilities (HTTP, disk, config)
│   ├── NzbDrone.Host/                        # ASP.NET Core host + middleware
│   ├── NzbDrone.Console/                     # Console entry point
│   ├── NzbDrone/                             # Windows service entry point
│   ├── NzbDrone.SignalR/                      # Real-time UI push via SignalR
│   ├── Readarr.Api.V1/                       # REST API controllers
│   ├── Readarr.Http/                         # HTTP middleware, authentication
│   ├── NzbDrone.Update/                      # Self-updater
│   └── *Test*/                               # Test projects (NUnit)
├── frontend/                                 # React SPA (served by backend in production)
│   └── src/                                  # TypeScript + CSS Modules
├── distribution/                             # Packaging scripts (Docker, systemd, etc.)
├── build.sh                                  # CI build script
├── package.json                              # Frontend dependencies (root-level)
└── azure-pipelines.yml                       # CI pipeline definition
```

## Architecture Patterns

### Dependency Injection
DryIoc container — all services registered by convention (interface → implementation).
Constructors receive dependencies; no service locator pattern.

### CQRS / Commands
Background operations use `Command` pattern:
- Define: `src/NzbDrone.Core/Books/Commands/RefreshAuthorCommand.cs`
- Handle: Corresponding `CommandHandler` implementing `IExecute<T>`
- Dispatch: `IManageCommandQueue.Push(new RefreshAuthorCommand(...))`

### Database
- **ORM:** Dapper (raw SQL) + custom `TableMapping` system
- **Migrations:** Servarr.FluentMigrator — numbered sequential migrations
- **Backends:** SQLite (default), PostgreSQL (production option)
- **Next migration number:** 041

### Metadata Source Pattern
All metadata providers implement these interfaces:
```csharp
IProvideAuthorInfo      // GetAuthorInfo(id) → Author + books
IProvideBookInfo        // GetBookInfo(id) → Book + editions
ISearchForNewAuthor     // SearchForNewAuthor(query) → List<Author>
ISearchForNewBook       // SearchForNewBook(query) → List<Book>
ISearchForNewEntity     // SearchForNewEntity(query) → List<object>
```

To add a new metadata source, implement these interfaces and register via DI.
The current implementation is `GoodreadsProxy` (DEPRECATED — uses scraped API keys).

### Edition Model
A "Book" (Work) has multiple "Editions" — the Edition is what gets downloaded:
- `Book` → conceptual work (e.g., "The Great Gatsby")
- `Edition` → specific publication (ISBN, format, publisher, language)
- `BookFile` → actual file on disk linked to an Edition

### Quality System
Qualities have IDs that MUST be stable (stored in DB profiles):
- Text: Unknown(0), PDF(1), MOBI(2), EPUB(3), AZW3(4)
- Audio: MP3(10), FLAC(11), M4B(12)

### Frontend
React 17 class components + Redux + connected-react-router. Webpack build.
CSS Modules with PostCSS. TypeScript (partial migration — mix of .js/.ts/.tsx).

## Key Conventions

1. **Namespace:** Projects use `NzbDrone.*` namespace (legacy from Sonarr heritage)
2. **Central package versions:** ALL NuGet versions in `Directory.Packages.props`
3. **Target framework:** net6.0 (upgrading to net8.0+)
4. **Tests:** NUnit + Moq + FluentAssertions + NBuilder
5. **Logging:** NLog throughout
6. **HTTP:** Custom `IHttpClient` wrapper around HttpClient
7. **Serialization:** Newtonsoft.Json (backend), System.Text.Json (some newer code)
8. **StyleCop:** Enforced via analyzers — code style must pass

## Common Tasks

### Adding a New Migration
```bash
# Create src/NzbDrone.Core/Datastore/Migration/041_your_migration.cs
# Number must be sequential, class must inherit NzbDroneMigrationBase
```

### Adding a New API Endpoint
1. Create controller in `Readarr.Api.V1/` inheriting from appropriate base
2. Define resource class (DTO)
3. Map between domain model and resource

### Running Specific Test Categories
```bash
dotnet test --filter "FullyQualifiedName~MetadataSource"
dotnet test --filter "Category=IntegrationTest"  # requires running instance
```

## Important Files for the Revitalization

- `src/NzbDrone.Core/MetadataSource/` — Replace Goodreads with Open Library
- `src/NzbDrone.Core/Books/Model/Edition.cs` — Add audiobook format awareness
- `src/NzbDrone.Core/MediaFiles/MediaFileExtensions.cs` — Format/quality mapping
- `src/NzbDrone.Core/Qualities/Quality.cs` — Quality definitions
- `src/Directory.Packages.props` — All NuGet dependency versions
- `src/Directory.Build.props` — Target framework and build settings
- `package.json` — Frontend dependency versions
