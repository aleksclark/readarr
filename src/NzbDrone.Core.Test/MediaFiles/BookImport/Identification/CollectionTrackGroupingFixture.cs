using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class CollectionTrackGroupingFixture : CoreTest<TrackGroupingService>
    {
        private List<LocalBook> GivenTracks(string root, string author, string book, int count, string extension = ".mp3")
        {
            var fileInfos = Builder<ParsedTrackInfo>
                .CreateListOfSize(count)
                .All()
                .With(f => f.Authors = new List<string> { author })
                .With(f => f.BookTitle = book)
                .With(f => f.BookMBId = null)
                .With(f => f.ReleaseMBId = null)
                .Build();

            var tracks = fileInfos.Select((x, i) => Builder<LocalBook>
                                          .CreateNew()
                                          .With(y => y.FileTrackInfo = x)
                                          .With(y => y.Path = Path.Combine(root, $"{i + 1:D2} - {x.Title}{extension}"))
                                          .Build()).ToList();

            return tracks;
        }

        private List<LocalBook> GivenTextTracks(string root, string author, string book, int count)
        {
            var fileInfos = Builder<ParsedTrackInfo>
                .CreateListOfSize(count)
                .All()
                .With(f => f.Authors = new List<string> { author })
                .With(f => f.BookTitle = book)
                .With(f => f.BookMBId = null)
                .With(f => f.ReleaseMBId = null)
                .Build();

            var tracks = fileInfos.Select((x, i) => Builder<LocalBook>
                                          .CreateNew()
                                          .With(y => y.FileTrackInfo = x)
                                          .With(y => y.Path = Path.Combine(root, $"{x.Title}.epub"))
                                          .Build()).ToList();

            return tracks;
        }

        [Test]
        public void should_group_audio_files_by_top_level_subdirectory()
        {
            var root = @"C:\audiobooks\author collection".AsOsAgnostic();

            // Create two groups of tracks in different subdirectories
            var book1Dir = Path.Combine(root, "Book One");
            var book2Dir = Path.Combine(root, "Book Two");

            var tracks = GivenTracks(book1Dir, "author", "Book One", 5);
            tracks.AddRange(GivenTracks(book2Dir, "author", "Book Two", 3));

            var output = Subject.GroupTracks(tracks);

            // Should produce 2 groups since they are in different subdirectories with different book tags
            output.Count.Should().Be(2);
            output.Should().Contain(g => g.LocalBooks.Count == 5);
            output.Should().Contain(g => g.LocalBooks.Count == 3);
        }

        [Test]
        public void should_not_split_single_audiobook_with_disc_markers()
        {
            var root = @"C:\audiobooks\author - book".AsOsAgnostic();

            // Create tracks in CD1 and CD2 subdirectories - same book title
            var cd1Dir = Path.Combine(root, "cd 1");
            var cd2Dir = Path.Combine(root, "cd 2");

            var tracks = GivenTracks(cd1Dir, "author", "book", 10);
            tracks.AddRange(GivenTracks(cd2Dir, "author", "book", 5));

            var output = Subject.GroupTracks(tracks);

            // CD1/CD2 should be merged into a single group
            output.Count.Should().Be(1);
            output[0].LocalBooks.Count.Should().Be(15);
        }

        [Test]
        public void should_handle_mixed_formats_m4b_and_mp3()
        {
            var root = @"C:\audiobooks\author - collection".AsOsAgnostic();

            // Book 1 is a single m4b file
            var book1Dir = Path.Combine(root, "Book One");
            var tracks = GivenTracks(book1Dir, "author", "Book One", 1, ".m4b");

            // Book 2 has mp3 chapters
            var book2Dir = Path.Combine(root, "Book Two");
            tracks.AddRange(GivenTracks(book2Dir, "author", "Book Two", 8, ".mp3"));

            var output = Subject.GroupTracks(tracks);

            // Should produce 2 groups (different directories, different book tags)
            output.Count.Should().Be(2);
        }

        [Test]
        public void should_fall_through_to_standard_grouping_when_not_a_collection()
        {
            // All files in a single directory with the same book title
            var dir = @"C:\audiobooks\incoming".AsOsAgnostic();
            var tracks = GivenTracks(dir, "author", "book", 10);

            var output = Subject.GroupTracks(tracks);

            // Single directory with consistent tags should be one group
            output.Count.Should().Be(1);
            output[0].LocalBooks.Count.Should().Be(10);
        }

        [Test]
        public void should_group_text_files_as_individual_releases()
        {
            // Text files (epub) are always individual releases
            var root = @"C:\ebooks\collection".AsOsAgnostic();
            var tracks = GivenTextTracks(root, "author", "book", 5);

            var output = Subject.GroupTracks(tracks);

            // Each text file should be its own group
            output.Count.Should().Be(5);
            output.Should().OnlyContain(g => g.LocalBooks.Count == 1);
        }

        [Test]
        public void should_group_audio_in_nested_disc_directories_under_book()
        {
            // Collection structure: root/Book1/Disc1/*.mp3, root/Book1/Disc2/*.mp3, root/Book2/*.mp3
            var root = @"C:\audiobooks\author anthology".AsOsAgnostic();

            var book1Disc1 = Path.Combine(root, "Book1", "disc 1");
            var book1Disc2 = Path.Combine(root, "Book1", "disc 2");
            var book2Dir = Path.Combine(root, "Book2");

            var tracks = GivenTracks(book1Disc1, "author", "Book One", 5);
            tracks.AddRange(GivenTracks(book1Disc2, "author", "Book One", 5));
            tracks.AddRange(GivenTracks(book2Dir, "author", "Book Two", 4));

            var output = Subject.GroupTracks(tracks);

            // The collection detection should group Book1/disc1 and Book1/disc2 together,
            // and Book2 separately. Total should be 2 groups.
            output.Count.Should().Be(2);
            output.Should().Contain(g => g.LocalBooks.Count == 10);
            output.Should().Contain(g => g.LocalBooks.Count == 4);
        }
    }
}
