using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class WantedBookSpecificationFixture : CoreTest<WantedBookSpecification>
    {
        private LocalEdition _localEdition;
        private Author _author;
        private Book _book;
        private Edition _edition;
        private QualityProfile _qualityProfile;

        [SetUp]
        public void Setup()
        {
            _qualityProfile = new QualityProfile
            {
                UpgradeAllowed = true,
                Cutoff = Quality.EPUB.Id,
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Quality = Quality.PDF, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.MOBI, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.EPUB, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.AZW3, Allowed = true },
                    new QualityProfileQualityItem { Quality = Quality.FLAC, Allowed = true },
                }
            };

            _author = Builder<Author>.CreateNew()
                .With(a => a.Id = 1)
                .With(a => a.QualityProfile = new LazyLoaded<QualityProfile>(_qualityProfile))
                .With(a => a.Metadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata { ForeignAuthorId = "author-123", Name = "Test Author" }))
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(b => b.Monitored = true)
                .With(b => b.Title = "Test Book")
                .With(b => b.Author = new LazyLoaded<Author>(_author))
                .With(b => b.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile>()))
                .Build();

            _edition = Builder<Edition>.CreateNew()
                .With(e => e.Book = new LazyLoaded<Book>(_book))
                .Build();

            _localEdition = new LocalEdition
            {
                Edition = _edition,
                IsCollection = true
            };

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindById(It.IsAny<string>()))
                  .Returns(_author);
        }

        [Test]
        public void should_accept_when_not_a_collection()
        {
            _localEdition.IsCollection = false;

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_book_is_not_monitored()
        {
            _book.Monitored = false;

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_when_author_not_in_library()
        {
            // Author has Id = 0 (not persisted)
            _author.Id = 0;

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_when_author_not_found_in_database()
        {
            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.FindById(It.IsAny<string>()))
                  .Returns((Author)null);

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_when_existing_files_meet_quality_cutoff()
        {
            var existingFile = new BookFile
            {
                Quality = new QualityModel(Quality.EPUB)
            };

            _book.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile> { existingFile });

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_when_book_is_monitored_and_missing()
        {
            // Book is monitored, no existing files
            _book.Monitored = true;
            _book.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile>());

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_book_exists_but_below_cutoff()
        {
            // Existing file is PDF, cutoff is EPUB (higher in the profile)
            var existingFile = new BookFile
            {
                Quality = new QualityModel(Quality.PDF)
            };

            _book.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile> { existingFile });

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_edition_is_null()
        {
            _localEdition.Edition = null;

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeTrue();
        }
    }
}
