using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class CollectionPreviewItem
    {
        public string FilePath { get; set; }
        public string ParsedBookTitle { get; set; }
        public string ParsedAuthor { get; set; }
        public CollectionPreviewMatchedBook MatchedBook { get; set; }
        public double MatchConfidence { get; set; }
        public string LibraryStatus { get; set; }
        public string ExistingQuality { get; set; }
        public string NewQuality { get; set; }
        public bool Recommended { get; set; }
    }

    public class CollectionPreviewMatchedBook
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string AuthorName { get; set; }
    }

    public interface ICollectionPreviewService
    {
        List<CollectionPreviewItem> GetCollectionPreview(string path);
    }

    public class CollectionPreviewService : ICollectionPreviewService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskScanService _diskScanService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public CollectionPreviewService(IDiskProvider diskProvider,
                                        IDiskScanService diskScanService,
                                        IMakeImportDecision importDecisionMaker,
                                        IMediaFileService mediaFileService,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _diskScanService = diskScanService;
            _importDecisionMaker = importDecisionMaker;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public List<CollectionPreviewItem> GetCollectionPreview(string path)
        {
            if (!_diskProvider.FolderExists(path))
            {
                _logger.Warn("Collection path does not exist: {0}", path);
                return new List<CollectionPreviewItem>();
            }

            var bookFiles = _diskScanService.GetBookFiles(path).ToList();

            if (!bookFiles.Any())
            {
                _logger.Debug("No book files found in collection path: {0}", path);
                return new List<CollectionPreviewItem>();
            }

            _logger.Debug("Found {0} book files in collection path: {1}", bookFiles.Count, path);

            var config = new ImportDecisionMakerConfig
            {
                Filter = FilterFilesType.None,
                NewDownload = true,
                SingleRelease = false,
                IncludeExisting = true,
                AddNewAuthors = false,
                KeepAllEditions = true,
                IsCollection = true
            };

            var decisions = _importDecisionMaker.GetImportDecisions(bookFiles, null, null, config);

            var results = new List<CollectionPreviewItem>();

            foreach (var decision in decisions)
            {
                var localBook = decision.Item;
                var item = new CollectionPreviewItem
                {
                    FilePath = localBook.Path,
                    ParsedBookTitle = localBook.FileTrackInfo?.Title ?? localBook.FolderTrackInfo?.BookTitle ?? System.IO.Path.GetFileNameWithoutExtension(localBook.Path),
                    ParsedAuthor = localBook.FileTrackInfo?.AuthorTitle ?? localBook.FolderTrackInfo?.AuthorName ?? string.Empty,
                    NewQuality = localBook.Quality?.Quality?.Name ?? Quality.Unknown.Name
                };

                if (localBook.Book != null && localBook.Book.Id > 0)
                {
                    item.MatchedBook = new CollectionPreviewMatchedBook
                    {
                        Id = localBook.Book.Id,
                        Title = localBook.Book.Title,
                        AuthorName = localBook.Author?.Name ?? localBook.Book.AuthorMetadata?.Value?.Name ?? string.Empty
                    };

                    // Calculate match confidence from distance
                    if (localBook.Distance != null)
                    {
                        item.MatchConfidence = Math.Max(0, 1.0 - localBook.Distance.NormalizedDistance());
                    }
                    else
                    {
                        // If we have a match but no distance, assume decent confidence
                        item.MatchConfidence = 0.8;
                    }

                    // Determine library status
                    item.LibraryStatus = DetermineLibraryStatus(localBook);

                    // Get existing quality if the book already has files
                    item.ExistingQuality = GetExistingQuality(localBook.Book);
                }
                else
                {
                    item.MatchConfidence = 0;
                    item.LibraryStatus = "missing";
                    item.ExistingQuality = null;
                }

                // Determine if import is recommended
                item.Recommended = ShouldRecommendImport(item, decision);

                results.Add(item);
            }

            return results;
        }

        private string DetermineLibraryStatus(LocalBook localBook)
        {
            var book = localBook.Book;

            if (book == null || book.Id == 0)
            {
                return "missing";
            }

            if (!book.Monitored)
            {
                return "unmonitored";
            }

            var existingFiles = book.BookFiles?.Value;
            if (existingFiles == null || !existingFiles.Any())
            {
                return "missing";
            }

            // Book has files - check if this would be an upgrade
            if (localBook.Quality != null)
            {
                var existingBestQuality = existingFiles
                    .Select(f => f.Quality?.Quality)
                    .Where(q => q != null)
                    .OrderByDescending(q => q.Id)
                    .FirstOrDefault();

                if (existingBestQuality != null && localBook.Quality.Quality.Id > existingBestQuality.Id)
                {
                    return "upgrade";
                }
            }

            return "owned";
        }

        private string GetExistingQuality(Book book)
        {
            var existingFiles = book?.BookFiles?.Value;
            if (existingFiles == null || !existingFiles.Any())
            {
                return null;
            }

            var bestQuality = existingFiles
                .Select(f => f.Quality?.Quality)
                .Where(q => q != null)
                .OrderByDescending(q => q.Id)
                .FirstOrDefault();

            return bestQuality?.Name;
        }

        private bool ShouldRecommendImport(CollectionPreviewItem item, ImportDecision<LocalBook> decision)
        {
            // Don't recommend if no match found
            if (item.MatchedBook == null)
            {
                return false;
            }

            // Don't recommend if match confidence is too low
            if (item.MatchConfidence < 0.5)
            {
                return false;
            }

            // Don't recommend if book is unmonitored
            if (item.LibraryStatus == "unmonitored")
            {
                return false;
            }

            // Recommend if book is missing from library
            if (item.LibraryStatus == "missing")
            {
                return true;
            }

            // Recommend if it's an upgrade
            if (item.LibraryStatus == "upgrade")
            {
                return true;
            }

            // Already owned and not an upgrade - don't recommend
            return false;
        }
    }
}
