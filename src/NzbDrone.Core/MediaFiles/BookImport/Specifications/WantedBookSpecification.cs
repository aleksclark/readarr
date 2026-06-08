using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class WantedBookSpecification : IImportDecisionEngineSpecification<LocalEdition>
    {
        private readonly IAuthorService _authorService;
        private readonly Logger _logger;

        public WantedBookSpecification(IAuthorService authorService,
                                       Logger logger)
        {
            _authorService = authorService;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalEdition item, DownloadClientItem downloadClientItem)
        {
            // Only apply this specification for collection downloads
            if (!item.IsCollection)
            {
                return Decision.Accept();
            }

            var edition = item.Edition;
            if (edition == null)
            {
                _logger.Debug("No edition matched, skipping wanted book check");
                return Decision.Accept();
            }

            var book = edition.Book.Value;
            var author = book.Author.Value;

            // Check if the author exists in the library
            if (author.Id <= 0)
            {
                _logger.Debug("Author '{0}' is not in the library, rejecting", author.Name);
                return Decision.Reject("Author '{0}' is not in the library", author.Name);
            }

            // Verify the author still exists in the database (in case of stale data)
            var dbAuthor = _authorService.FindById(author.ForeignAuthorId);
            if (dbAuthor == null)
            {
                _logger.Debug("Author '{0}' not found in the library, rejecting", author.Name);
                return Decision.Reject("Author '{0}' is not in the library", author.Name);
            }

            // Check if the book is monitored
            if (!book.Monitored)
            {
                _logger.Debug("Book '{0}' is not monitored, rejecting", book.Title);
                return Decision.Reject("Book '{0}' is not monitored", book.Title);
            }

            // Check if existing files are at or above quality cutoff
            var bookFiles = book.BookFiles?.Value;
            if (bookFiles != null && bookFiles.Any())
            {
                var qualityProfile = author.QualityProfile.Value;
                if (qualityProfile != null)
                {
                    var cutoff = qualityProfile.UpgradeAllowed ? qualityProfile.Cutoff : qualityProfile.FirstAllowedQuality().Id;
                    var qualityComparer = new QualityModelComparer(qualityProfile);

                    var allAtCutoff = bookFiles.All(f =>
                    {
                        var cutoffCompare = qualityComparer.Compare(f.Quality.Quality.Id, cutoff);
                        return cutoffCompare >= 0;
                    });

                    if (allAtCutoff)
                    {
                        _logger.Debug("Book '{0}' already has files at or above quality cutoff, rejecting", book.Title);
                        return Decision.Reject("Book '{0}' already meets quality cutoff", book.Title);
                    }
                }
            }

            return Decision.Accept();
        }
    }
}
