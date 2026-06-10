using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport
{
    [TestFixture]
    public class CollectionPreviewServiceFixture : CoreTest<CollectionPreviewService>
    {
        private string _collectionPath;
        private List<IFileInfo> _bookFiles;

        [SetUp]
        public void Setup()
        {
            _collectionPath = @"C:\Collections\MyBooks".AsOsAgnostic();

            var mockFileSystem = new MockFileSystem();
            var file1 = new MockFileInfo(mockFileSystem, @"C:\Collections\MyBooks\Author - Book One.epub".AsOsAgnostic());
            var file2 = new MockFileInfo(mockFileSystem, @"C:\Collections\MyBooks\Author - Book Two.epub".AsOsAgnostic());

            _bookFiles = new List<IFileInfo> { file1, file2 };

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FolderExists(_collectionPath))
                .Returns(true);

            Mocker.GetMock<IDiskScanService>()
                .Setup(d => d.GetBookFiles(_collectionPath, true))
                .Returns(_bookFiles.ToArray());
        }

        private ImportDecision<LocalBook> GivenDecisionWithMatch(string filePath, Book book, Author author, double normalizedDistance, bool monitored = true)
        {
            var distance = new Distance();

            // Add a penalty so NormalizedDistance() returns the expected value.
            // Distance with a single "book" penalty of normalizedDistance gives NormalizedDistance() = normalizedDistance.
            distance.Add("book", normalizedDistance);

            var localBook = new LocalBook
            {
                Path = filePath,
                Book = book,
                Author = author,
                Distance = distance,
                Quality = new QualityModel(Quality.EPUB),
                FileTrackInfo = new ParsedTrackInfo
                {
                    Title = book.Title,
                    Authors = new List<string> { author.Name }
                }
            };

            return new ImportDecision<LocalBook>(localBook);
        }

        private ImportDecision<LocalBook> GivenDecisionWithNoMatch(string filePath)
        {
            var localBook = new LocalBook
            {
                Path = filePath,
                Book = null,
                Author = null,
                Distance = null,
                Quality = new QualityModel(Quality.EPUB),
                FileTrackInfo = new ParsedTrackInfo
                {
                    Title = "Unknown Book",
                    Authors = new List<string> { "Unknown Author" }
                }
            };

            return new ImportDecision<LocalBook>(localBook);
        }

        private Book GivenBook(int id, string title, bool monitored = true, List<BookFile> existingFiles = null)
        {
            return new Book
            {
                Id = id,
                Title = title,
                Monitored = monitored,
                BookFiles = new LazyLoaded<List<BookFile>>(existingFiles ?? new List<BookFile>()),
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata { Name = "Test Author" })
            };
        }

        private Author GivenAuthor(string name)
        {
            return new Author { Id = 1, Name = name };
        }

        [Test]
        public void should_scan_folder_for_book_files()
        {
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(d => d.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(new List<ImportDecision<LocalBook>>());

            Subject.GetCollectionPreview(_collectionPath);

            Mocker.GetMock<IDiskScanService>()
                .Verify(d => d.GetBookFiles(_collectionPath, true), Times.Once());
        }

        [Test]
        public void should_return_preview_items_with_match_confidence()
        {
            var book = GivenBook(1, "Book One", monitored: true);
            var author = GivenAuthor("Test Author");

            // Distance of 0.1 means confidence = 1.0 - 0.1 = 0.9
            var decisions = new List<ImportDecision<LocalBook>>
            {
                GivenDecisionWithMatch(_bookFiles[0].FullName, book, author, 0.1)
            };

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(d => d.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(decisions);

            var results = Subject.GetCollectionPreview(_collectionPath);

            results.Should().HaveCount(1);
            results[0].MatchConfidence.Should().BeGreaterThan(0);
            results[0].MatchedBook.Should().NotBeNull();
            results[0].MatchedBook.Title.Should().Be("Book One");
        }

        [Test]
        public void should_identify_library_status_as_missing_for_books_not_in_library()
        {
            // Book with Id > 0 but no existing files = missing
            var book = GivenBook(1, "Missing Book", monitored: true, existingFiles: new List<BookFile>());
            var author = GivenAuthor("Test Author");

            var decisions = new List<ImportDecision<LocalBook>>
            {
                GivenDecisionWithMatch(_bookFiles[0].FullName, book, author, 0.1)
            };

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(d => d.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(decisions);

            var results = Subject.GetCollectionPreview(_collectionPath);

            results.Should().HaveCount(1);
            results[0].LibraryStatus.Should().Be("missing");
        }

        [Test]
        public void should_identify_library_status_as_owned_for_books_already_imported()
        {
            var existingFiles = new List<BookFile>
            {
                new BookFile
                {
                    Id = 10,
                    Path = @"C:\Library\Author\book.epub".AsOsAgnostic(),
                    Quality = new QualityModel(Quality.EPUB)
                }
            };

            var book = GivenBook(1, "Owned Book", monitored: true, existingFiles: existingFiles);
            var author = GivenAuthor("Test Author");

            // Use same quality so it's not an upgrade - just "owned"
            var decisions = new List<ImportDecision<LocalBook>>
            {
                GivenDecisionWithMatch(_bookFiles[0].FullName, book, author, 0.1)
            };

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(d => d.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(decisions);

            var results = Subject.GetCollectionPreview(_collectionPath);

            results.Should().HaveCount(1);
            results[0].LibraryStatus.Should().Be("owned");
        }

        [Test]
        public void should_recommend_import_for_missing_monitored_books()
        {
            var book = GivenBook(1, "Missing Monitored Book", monitored: true, existingFiles: new List<BookFile>());
            var author = GivenAuthor("Test Author");

            // Good match confidence (low distance)
            var decisions = new List<ImportDecision<LocalBook>>
            {
                GivenDecisionWithMatch(_bookFiles[0].FullName, book, author, 0.1)
            };

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(d => d.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(decisions);

            var results = Subject.GetCollectionPreview(_collectionPath);

            results.Should().HaveCount(1);
            results[0].Recommended.Should().BeTrue();
            results[0].LibraryStatus.Should().Be("missing");
        }

        [Test]
        public void should_not_recommend_import_for_unmonitored_books()
        {
            var book = GivenBook(1, "Unmonitored Book", monitored: false, existingFiles: new List<BookFile>());
            var author = GivenAuthor("Test Author");

            var decisions = new List<ImportDecision<LocalBook>>
            {
                GivenDecisionWithMatch(_bookFiles[0].FullName, book, author, 0.1)
            };

            Mocker.GetMock<IMakeImportDecision>()
                .Setup(d => d.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(decisions);

            var results = Subject.GetCollectionPreview(_collectionPath);

            results.Should().HaveCount(1);
            results[0].Recommended.Should().BeFalse();
            results[0].LibraryStatus.Should().Be("unmonitored");
        }
    }
}
