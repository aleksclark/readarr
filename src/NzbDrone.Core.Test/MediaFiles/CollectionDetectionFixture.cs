using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class CollectionDetectionFixture : CoreTest<DownloadedBooksImportService>
    {
        private string _rootFolder;
        private Mock<IDirectoryInfo> _directoryInfo;

        [SetUp]
        public void Setup()
        {
            _rootFolder = @"C:\drop\Author - Complete Works".AsOsAgnostic();

            _directoryInfo = new Mock<IDirectoryInfo>();
            _directoryInfo.SetupGet(d => d.FullName).Returns(_rootFolder);
            _directoryInfo.SetupGet(d => d.Name).Returns("Author - Complete Works");

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetDirectoryInfo(It.IsAny<string>()))
                  .Returns<string>(path =>
                  {
                      var dirMock = new Mock<IDirectoryInfo>();
                      dirMock.SetupGet(d => d.FullName).Returns(path);
                      dirMock.SetupGet(d => d.Name).Returns(Path.GetFileName(path));
                      return dirMock.Object;
                  });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetDirectoryInfos(It.IsAny<string>()))
                  .Returns(new List<IDirectoryInfo>());

            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.CollectionDetectionThreshold)
                  .Returns(3);

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(new IFileInfo[0]);

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                  .Returns<string, IEnumerable<IFileInfo>>((b, s) => s.ToList());
        }

        private Mock<IFileInfo> CreateMockFileInfo(string filePath)
        {
            var mock = new Mock<IFileInfo>();
            mock.SetupGet(f => f.FullName).Returns(filePath);
            mock.SetupGet(f => f.Name).Returns(Path.GetFileName(filePath));
            mock.SetupGet(f => f.Length).Returns(1024);
            mock.SetupGet(f => f.Exists).Returns(true);
            var dirMock = new Mock<IDirectoryInfo>();
            dirMock.SetupGet(d => d.FullName).Returns(Path.GetDirectoryName(filePath));
            mock.SetupGet(f => f.Directory).Returns(dirMock.Object);
            mock.SetupGet(f => f.DirectoryName).Returns(Path.GetDirectoryName(filePath));
            return mock;
        }

        private Mock<IDirectoryInfo> CreateMockDirectoryInfo(string dirPath)
        {
            var mock = new Mock<IDirectoryInfo>();
            mock.SetupGet(d => d.FullName).Returns(dirPath);
            mock.SetupGet(d => d.Name).Returns(Path.GetFileName(dirPath));
            mock.SetupGet(d => d.Exists).Returns(true);
            return mock;
        }

        private List<IFileInfo> GivenEpubFiles(int count)
        {
            var files = new List<IFileInfo>();
            for (var i = 0; i < count; i++)
            {
                var filePath = Path.Combine(_rootFolder, $"Book{i + 1}.epub");
                files.Add(CreateMockFileInfo(filePath).Object);
            }

            return files;
        }

        private List<IFileInfo> GivenAudioFilesInSubdirectories(int subdirCount, int filesPerSubdir)
        {
            var files = new List<IFileInfo>();
            var subdirs = new List<IDirectoryInfo>();

            for (var i = 0; i < subdirCount; i++)
            {
                var subdirPath = Path.Combine(_rootFolder, $"Book {i + 1}");
                var subdirMock = CreateMockDirectoryInfo(subdirPath);

                var fileNames = new List<string>();
                for (var j = 0; j < filesPerSubdir; j++)
                {
                    var filePath = Path.Combine(subdirPath, $"chapter{j + 1}.mp3");
                    files.Add(CreateMockFileInfo(filePath).Object);
                    fileNames.Add(filePath);
                }

                subdirs.Add(subdirMock.Object);
            }

            // Setup subdirectory enumeration
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetDirectoryInfos(_rootFolder))
                  .Returns(subdirs);

            foreach (var subdir in subdirs)
            {
                var subdirFiles = files
                    .Where(f => f.FullName.StartsWith(subdir.FullName))
                    .Select(f => f.FullName)
                    .ToArray();

                Mocker.GetMock<IDiskProvider>()
                      .Setup(d => d.GetFiles(subdir.FullName, false))
                      .Returns(subdirFiles);
            }

            return files;
        }

        [Test]
        public void should_detect_collection_with_3_plus_epub_files()
        {
            var files = GivenEpubFiles(5);

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(files.ToArray());

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                  .Returns(files);

            // The IsCollectionDownload method is private, so we test through ProcessPath
            // which sets IsCollection flag. We verify through the ImportDecisionMakerConfig.
            ImportDecisionMakerConfig capturedConfig = null;
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) => capturedConfig = c)
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>(), It.IsAny<bool>()))
                  .Returns(new List<ImportResult>());

            Mocker.GetMock<NzbDrone.Core.Parser.IParsingService>()
                  .Setup(s => s.GetAuthor(It.IsAny<string>()))
                  .Returns((NzbDrone.Core.Books.Author)null);

            Mocker.GetMock<NzbDrone.Core.Books.IAuthorService>()
                  .Setup(s => s.AuthorPathExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ProcessPath(_rootFolder);

            capturedConfig.Should().NotBeNull();
            capturedConfig.IsCollection.Should().BeTrue();
        }

        [Test]
        public void should_detect_collection_with_folder_name_pattern()
        {
            // "Complete Works" in folder name should trigger collection detection
            var files = GivenEpubFiles(1);

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(files.ToArray());

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                  .Returns(files);

            ImportDecisionMakerConfig capturedConfig = null;
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) => capturedConfig = c)
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>(), It.IsAny<bool>()))
                  .Returns(new List<ImportResult>());

            Mocker.GetMock<NzbDrone.Core.Parser.IParsingService>()
                  .Setup(s => s.GetAuthor(It.IsAny<string>()))
                  .Returns((NzbDrone.Core.Books.Author)null);

            Mocker.GetMock<NzbDrone.Core.Books.IAuthorService>()
                  .Setup(s => s.AuthorPathExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ProcessPath(_rootFolder);

            capturedConfig.Should().NotBeNull();
            capturedConfig.IsCollection.Should().BeTrue();
        }

        [Test]
        public void should_not_detect_collection_with_fewer_than_threshold_files()
        {
            // Only 2 files, threshold is 3
            _rootFolder = @"C:\drop\Author - New Book".AsOsAgnostic();

            var files = new List<IFileInfo>();
            for (var i = 0; i < 2; i++)
            {
                var filePath = Path.Combine(_rootFolder, $"Book{i + 1}.epub");
                files.Add(CreateMockFileInfo(filePath).Object);
            }

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(files.ToArray());

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                  .Returns(files);

            // No subdirectories with audio
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetDirectoryInfos(It.IsAny<string>()))
                  .Returns(new List<IDirectoryInfo>());

            ImportDecisionMakerConfig capturedConfig = null;
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) => capturedConfig = c)
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>(), It.IsAny<bool>()))
                  .Returns(new List<ImportResult>());

            Mocker.GetMock<NzbDrone.Core.Parser.IParsingService>()
                  .Setup(s => s.GetAuthor(It.IsAny<string>()))
                  .Returns((NzbDrone.Core.Books.Author)null);

            Mocker.GetMock<NzbDrone.Core.Books.IAuthorService>()
                  .Setup(s => s.AuthorPathExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ProcessPath(_rootFolder);

            capturedConfig.Should().NotBeNull();
            capturedConfig.IsCollection.Should().BeFalse();
        }

        [Test]
        public void should_not_detect_single_audiobook_with_consistent_tags()
        {
            // Multiple subdirs with audio, but all share same book title = not a collection
            _rootFolder = @"C:\drop\Author - Single Audiobook".AsOsAgnostic();

            var files = new List<IFileInfo>();
            var subdirs = new List<IDirectoryInfo>();

            for (var i = 0; i < 3; i++)
            {
                var subdirPath = Path.Combine(_rootFolder, $"CD{i + 1}");
                var subdirMock = CreateMockDirectoryInfo(subdirPath);

                var fileNames = new List<string>();
                for (var j = 0; j < 5; j++)
                {
                    var filePath = Path.Combine(subdirPath, $"chapter{j + 1}.mp3");
                    files.Add(CreateMockFileInfo(filePath).Object);
                    fileNames.Add(filePath);
                }

                subdirs.Add(subdirMock.Object);
            }

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(files.ToArray());

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                  .Returns(files);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetDirectoryInfos(_rootFolder))
                  .Returns(subdirs);

            foreach (var subdir in subdirs)
            {
                var subdirFiles = files
                    .Where(f => f.FullName.StartsWith(subdir.FullName))
                    .Select(f => f.FullName)
                    .ToArray();

                Mocker.GetMock<IDiskProvider>()
                      .Setup(d => d.GetFiles(subdir.FullName, false))
                      .Returns(subdirFiles);
            }

            // All audio files have the same book title (consistent = single audiobook)
            Mocker.GetMock<IMetadataTagService>()
                  .Setup(s => s.ReadTags(It.IsAny<IFileInfo>()))
                  .Returns(new ParsedTrackInfo { BookTitle = "Single Audiobook Title" });

            ImportDecisionMakerConfig capturedConfig = null;
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) => capturedConfig = c)
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>(), It.IsAny<bool>()))
                  .Returns(new List<ImportResult>());

            Mocker.GetMock<NzbDrone.Core.Parser.IParsingService>()
                  .Setup(s => s.GetAuthor(It.IsAny<string>()))
                  .Returns((NzbDrone.Core.Books.Author)null);

            Mocker.GetMock<NzbDrone.Core.Books.IAuthorService>()
                  .Setup(s => s.AuthorPathExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ProcessPath(_rootFolder);

            capturedConfig.Should().NotBeNull();
            capturedConfig.IsCollection.Should().BeFalse();
        }

        [Test]
        public void should_detect_audiobook_collection_with_multiple_subdirectories()
        {
            // Multiple subdirs with audio, different book titles = collection
            _rootFolder = @"C:\drop\Author - Anthology".AsOsAgnostic();

            var files = new List<IFileInfo>();
            var subdirs = new List<IDirectoryInfo>();

            for (var i = 0; i < 4; i++)
            {
                var subdirPath = Path.Combine(_rootFolder, $"Book {i + 1}");
                var subdirMock = CreateMockDirectoryInfo(subdirPath);

                var fileNames = new List<string>();
                for (var j = 0; j < 3; j++)
                {
                    var filePath = Path.Combine(subdirPath, $"chapter{j + 1}.mp3");
                    files.Add(CreateMockFileInfo(filePath).Object);
                    fileNames.Add(filePath);
                }

                subdirs.Add(subdirMock.Object);
            }

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(files.ToArray());

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                  .Returns(files);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.GetDirectoryInfos(_rootFolder))
                  .Returns(subdirs);

            foreach (var subdir in subdirs)
            {
                var subdirFiles = files
                    .Where(f => f.FullName.StartsWith(subdir.FullName))
                    .Select(f => f.FullName)
                    .ToArray();

                Mocker.GetMock<IDiskProvider>()
                      .Setup(d => d.GetFiles(subdir.FullName, false))
                      .Returns(subdirFiles);
            }

            // Each subdirectory has different book titles
            var bookIndex = 0;
            Mocker.GetMock<IMetadataTagService>()
                  .Setup(s => s.ReadTags(It.IsAny<IFileInfo>()))
                  .Returns<IFileInfo>(f =>
                  {
                      // Different titles per subdirectory
                      var dirName = Path.GetDirectoryName(f.FullName);
                      return new ParsedTrackInfo { BookTitle = $"Different Book {dirName?.GetHashCode() ?? bookIndex++}" };
                  });

            ImportDecisionMakerConfig capturedConfig = null;
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) => capturedConfig = c)
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>(), It.IsAny<bool>()))
                  .Returns(new List<ImportResult>());

            Mocker.GetMock<NzbDrone.Core.Parser.IParsingService>()
                  .Setup(s => s.GetAuthor(It.IsAny<string>()))
                  .Returns((NzbDrone.Core.Books.Author)null);

            Mocker.GetMock<NzbDrone.Core.Books.IAuthorService>()
                  .Setup(s => s.AuthorPathExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ProcessPath(_rootFolder);

            capturedConfig.Should().NotBeNull();
            capturedConfig.IsCollection.Should().BeTrue();
        }

        [TestCase("Author - Collection")]
        [TestCase("Author Anthology")]
        [TestCase("Author Complete Works")]
        [TestCase("Author - Box Set")]
        [TestCase("Author Complete Series")]
        [TestCase("Author - Omnibus")]
        public void should_detect_various_collection_folder_patterns(string folderName)
        {
            var folderPath = Path.Combine(@"C:\drop".AsOsAgnostic(), folderName);

            var files = new List<IFileInfo>();
            var filePath = Path.Combine(folderPath, "Book1.epub");
            files.Add(CreateMockFileInfo(filePath).Object);

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetBookFiles(It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns(files.ToArray());

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                  .Returns(files);

            ImportDecisionMakerConfig capturedConfig = null;
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                  .Callback<List<IFileInfo>, IdentificationOverrides, ImportDecisionMakerInfo, ImportDecisionMakerConfig>((f, o, i, c) => capturedConfig = c)
                  .Returns(new List<ImportDecision<LocalBook>>());

            Mocker.GetMock<IImportApprovedBooks>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>(), It.IsAny<bool>()))
                  .Returns(new List<ImportResult>());

            Mocker.GetMock<NzbDrone.Core.Parser.IParsingService>()
                  .Setup(s => s.GetAuthor(It.IsAny<string>()))
                  .Returns((NzbDrone.Core.Books.Author)null);

            Mocker.GetMock<NzbDrone.Core.Books.IAuthorService>()
                  .Setup(s => s.AuthorPathExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ProcessPath(folderPath);

            capturedConfig.Should().NotBeNull();
            capturedConfig.IsCollection.Should().BeTrue();
        }
    }
}
