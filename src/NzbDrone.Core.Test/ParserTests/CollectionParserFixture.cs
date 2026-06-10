using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class CollectionParserFixture : CoreTest
    {
        [TestCase("01 - The Great Gatsby.epub", null, "The Great Gatsby")]
        [TestCase("01. The Great Gatsby.epub", null, "The Great Gatsby")]
        [TestCase("01_The Great Gatsby.epub", null, "The Great Gatsby")]
        [TestCase("1 - The Great Gatsby.epub", null, "The Great Gatsby")]
        [TestCase("001 - The Great Gatsby.epub", null, "The Great Gatsby")]
        public void should_parse_numbered_title_extracting_title(string fileName, string authorHint, string expectedTitle)
        {
            var result = Parser.Parser.ParseCollectionBookTitle(fileName, authorHint);

            result.Should().Be(expectedTitle);
        }

        [TestCase("Book 1 - The Great Gatsby.epub", null, "The Great Gatsby")]
        [TestCase("Book 01 - The Great Gatsby.epub", null, "The Great Gatsby")]
        [TestCase("Vol 1 - The Great Gatsby.epub", null, "The Great Gatsby")]
        [TestCase("Volume 2 - Tender Is the Night.epub", null, "Tender Is the Night")]
        [TestCase("Part 3 - The Last Tycoon.epub", null, "The Last Tycoon")]
        public void should_parse_book_number_prefix_extracting_title(string fileName, string authorHint, string expectedTitle)
        {
            var result = Parser.Parser.ParseCollectionBookTitle(fileName, authorHint);

            result.Should().Be(expectedTitle);
        }

        [TestCase("F. Scott Fitzgerald - The Great Gatsby.epub", "F. Scott Fitzgerald", "The Great Gatsby")]
        [TestCase("Stephen King - It.epub", "Stephen King", "It")]
        [TestCase("J.R.R. Tolkien - The Hobbit.epub", "J.R.R. Tolkien", "The Hobbit")]
        public void should_parse_author_name_dash_title_removing_author_prefix(string fileName, string authorHint, string expectedTitle)
        {
            var result = Parser.Parser.ParseCollectionBookTitle(fileName, authorHint);

            result.Should().Be(expectedTitle);
        }

        [TestCase("The Great Gatsby (2019).epub", null, "The Great Gatsby")]
        [TestCase("The Great Gatsby [2004].epub", null, "The Great Gatsby")]
        [TestCase("01 - The Great Gatsby (1925).epub", null, "The Great Gatsby")]
        public void should_parse_title_without_year_suffix(string fileName, string authorHint, string expectedTitle)
        {
            var result = Parser.Parser.ParseCollectionBookTitle(fileName, authorHint);

            result.Should().Be(expectedTitle);
        }

        [Test]
        public void should_parse_series_from_directory_structure()
        {
            var rootPath = @"C:\ebooks\Author Name".AsOsAgnostic();
            var filePath = Path.Combine(rootPath, "Dark Tower Series", "01 - The Gunslinger.epub");

            var result = Parser.Parser.ParseCollectionSeriesFromPath(filePath, rootPath);

            result.Should().Be("Dark Tower Series");
        }

        [Test]
        public void should_return_null_for_file_in_root_directory()
        {
            var rootPath = @"C:\ebooks\Author Name".AsOsAgnostic();
            var filePath = Path.Combine(rootPath, "The Great Gatsby.epub");

            var result = Parser.Parser.ParseCollectionSeriesFromPath(filePath, rootPath);

            result.Should().BeNull();
        }

        [Test]
        public void should_return_null_for_null_inputs()
        {
            var result = Parser.Parser.ParseCollectionSeriesFromPath(null, null);

            result.Should().BeNull();
        }

        [Test]
        public void should_extract_first_directory_component_as_series()
        {
            var rootPath = @"C:\ebooks\Author Name".AsOsAgnostic();
            var filePath = Path.Combine(rootPath, "Discworld", "Witches", "01 - Equal Rites.epub");

            var result = Parser.Parser.ParseCollectionSeriesFromPath(filePath, rootPath);

            result.Should().Be("Discworld");
        }

        [TestCase("Title (epub).epub", null, "Title")]
        [TestCase("Title [mobi].epub", null, "Title")]
        public void should_strip_format_tags_in_brackets(string fileName, string authorHint, string expectedTitle)
        {
            var result = Parser.Parser.ParseCollectionBookTitle(fileName, authorHint);

            result.Should().Be(expectedTitle);
        }

        [Test]
        public void should_return_null_for_empty_filename()
        {
            var result = Parser.Parser.ParseCollectionBookTitle("", null);

            result.Should().BeNull();
        }

        [Test]
        public void should_return_null_for_whitespace_filename()
        {
            var result = Parser.Parser.ParseCollectionBookTitle("   ", null);

            result.Should().BeNull();
        }
    }
}
