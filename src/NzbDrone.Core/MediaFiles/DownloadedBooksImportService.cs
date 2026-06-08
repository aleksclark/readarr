using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDownloadedBooksImportService
    {
        List<ImportResult> ProcessRootFolder(IDirectoryInfo directoryInfo);
        List<ImportResult> ProcessPath(string path, ImportMode importMode = ImportMode.Auto, Author author = null, DownloadClientItem downloadClientItem = null);
        bool ShouldDeleteFolder(IDirectoryInfo directoryInfo);
    }

    public class DownloadedBooksImportService : IDownloadedBooksImportService
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskScanService _diskScanService;
        private readonly IAuthorService _authorService;
        private readonly IParsingService _parsingService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedBooks _importApprovedTracks;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRuntimeInfo _runtimeInfo;
        private readonly Logger _logger;

        private static readonly Regex CollectionFolderPattern = new Regex(
            @"\b(Complete\s*Works|Discography|Collection|Anthology|Box\s*Set|Boxset|Omnibus|Collected|Complete\s*Series|Complete\s*Edition)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public DownloadedBooksImportService(IDiskProvider diskProvider,
                                             IDiskScanService diskScanService,
                                             IAuthorService authorService,
                                             IParsingService parsingService,
                                             IMakeImportDecision importDecisionMaker,
                                             IImportApprovedBooks importApprovedTracks,
                                             IMetadataTagService metadataTagService,
                                             IConfigService configService,
                                             IEventAggregator eventAggregator,
                                             IRuntimeInfo runtimeInfo,
                                             Logger logger)
        {
            _diskProvider = diskProvider;
            _diskScanService = diskScanService;
            _authorService = authorService;
            _parsingService = parsingService;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedTracks = importApprovedTracks;
            _metadataTagService = metadataTagService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _runtimeInfo = runtimeInfo;
            _logger = logger;
        }

        public List<ImportResult> ProcessRootFolder(IDirectoryInfo directoryInfo)
        {
            var results = new List<ImportResult>();

            foreach (var subFolder in _diskProvider.GetDirectoryInfos(directoryInfo.FullName))
            {
                var folderResults = ProcessFolder(subFolder, ImportMode.Auto, null);
                results.AddRange(folderResults);
            }

            foreach (var audioFile in _diskScanService.GetBookFiles(directoryInfo.FullName, false))
            {
                var fileResults = ProcessFile(audioFile, ImportMode.Auto, null);
                results.AddRange(fileResults);
            }

            return results;
        }

        public List<ImportResult> ProcessPath(string path, ImportMode importMode = ImportMode.Auto, Author author = null, DownloadClientItem downloadClientItem = null)
        {
            _logger.Debug("Processing path: {0}", path);

            if (_diskProvider.FolderExists(path))
            {
                var directoryInfo = _diskProvider.GetDirectoryInfo(path);

                if (author == null)
                {
                    return ProcessFolder(directoryInfo, importMode, downloadClientItem);
                }

                return ProcessFolder(directoryInfo, importMode, author, downloadClientItem);
            }

            if (_diskProvider.FileExists(path))
            {
                var fileInfo = _diskProvider.GetFileInfo(path);

                if (author == null)
                {
                    return ProcessFile(fileInfo, importMode, downloadClientItem);
                }

                return ProcessFile(fileInfo, importMode, author, downloadClientItem);
            }

            LogInaccessiblePathError(path);
            _eventAggregator.PublishEvent(new TrackImportFailedEvent(null, null, true, downloadClientItem));

            return new List<ImportResult>();
        }

        public bool ShouldDeleteFolder(IDirectoryInfo directoryInfo)
        {
            try
            {
                var bookFiles = _diskScanService.GetBookFiles(directoryInfo.FullName);
                var rarFiles = _diskProvider.GetFiles(directoryInfo.FullName, true).Where(f =>
                    Path.GetExtension(f).Equals(".rar",
                        StringComparison.OrdinalIgnoreCase));

                foreach (var bookFile in bookFiles)
                {
                    var bookParseResult = Parser.Parser.ParseTitle(bookFile.Name);

                    if (bookParseResult == null)
                    {
                        _logger.Warn("Unable to parse file on import: [{0}]", bookFile);
                        return false;
                    }

                    _logger.Warn("Book file detected: [{0}]", bookFile);
                    return false;
                }

                if (rarFiles.Any(f => _diskProvider.GetFileSize(f) > 10.Megabytes()))
                {
                    _logger.Warn("RAR file detected, will require manual cleanup");
                    return false;
                }

                return true;
            }
            catch (DirectoryNotFoundException e)
            {
                _logger.Debug(e, "Folder {0} has already been removed", directoryInfo.FullName);
                return false;
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Unable to determine whether folder {0} should be removed", directoryInfo.FullName);
                return false;
            }
        }

        private List<ImportResult> ProcessFolder(IDirectoryInfo directoryInfo, ImportMode importMode, DownloadClientItem downloadClientItem)
        {
            var cleanedUpName = GetCleanedUpFolderName(directoryInfo.Name);
            var author = _parsingService.GetAuthor(cleanedUpName);

            return ProcessFolder(directoryInfo, importMode, author, downloadClientItem);
        }

        private List<ImportResult> ProcessFolder(IDirectoryInfo directoryInfo, ImportMode importMode, Author author, DownloadClientItem downloadClientItem)
        {
            if (_authorService.AuthorPathExists(directoryInfo.FullName))
            {
                _logger.Warn("Unable to process folder that is mapped to an existing author");
                return new List<ImportResult>();
            }

            var cleanedUpName = GetCleanedUpFolderName(directoryInfo.Name);
            var folderInfo = Parser.Parser.ParseBookTitle(directoryInfo.Name);
            var trackInfo = new ParsedTrackInfo { };

            if (folderInfo != null)
            {
                _logger.Debug("{0} folder quality: {1}", cleanedUpName, folderInfo.Quality);

                trackInfo = new ParsedTrackInfo
                {
                    BookTitle = folderInfo.BookTitle,
                    Authors = new List<string> { folderInfo.AuthorName },
                    Quality = folderInfo.Quality,
                    ReleaseGroup = folderInfo.ReleaseGroup,
                    ReleaseHash = folderInfo.ReleaseHash,
                };
            }
            else
            {
                trackInfo = null;
            }

            var audioFiles = _diskScanService.FilterFiles(directoryInfo.FullName, _diskScanService.GetBookFiles(directoryInfo.FullName));

            if (downloadClientItem == null)
            {
                foreach (var audioFile in audioFiles)
                {
                    if (_diskProvider.IsFileLocked(audioFile.FullName))
                    {
                        return new List<ImportResult>
                               {
                                   FileIsLockedResult(audioFile.FullName)
                               };
                    }
                }
            }

            var isCollection = IsCollectionDownload(directoryInfo, audioFiles, out var collectionItemCount);

            var idOverrides = new IdentificationOverrides
            {
                Author = author
            };
            var idInfo = new ImportDecisionMakerInfo
            {
                DownloadClientItem = downloadClientItem,
                ParsedBookInfo = folderInfo
            };
            var idConfig = new ImportDecisionMakerConfig
            {
                Filter = FilterFilesType.None,
                NewDownload = true,
                SingleRelease = false,
                IncludeExisting = false,
                AddNewAuthors = false,
                IsCollection = isCollection
            };

            if (isCollection)
            {
                _logger.Info("Detected collection download with {0} items", collectionItemCount);

                // For collections, default to Copy mode to preserve the source
                if (importMode == ImportMode.Auto)
                {
                    importMode = ImportMode.Copy;
                }
            }

            var decisions = _importDecisionMaker.GetImportDecisions(audioFiles, idOverrides, idInfo, idConfig);
            var importResults = _importApprovedTracks.Import(decisions, true, downloadClientItem, importMode);

            if (!isCollection && importMode == ImportMode.Auto)
            {
                importMode = (downloadClientItem == null || downloadClientItem.CanMoveFiles) ? ImportMode.Move : ImportMode.Copy;
            }

            if (importMode == ImportMode.Move &&
                importResults.Any(i => i.Result == ImportResultType.Imported) &&
                ShouldDeleteFolder(directoryInfo))
            {
                _logger.Debug("Deleting folder after importing valid files");

                try
                {
                    _diskProvider.DeleteFolder(directoryInfo.FullName, true);
                }
                catch (IOException e)
                {
                    _logger.Debug(e, "Unable to delete folder after importing: {0}", e.Message);
                }
            }

            return importResults;
        }

        private List<ImportResult> ProcessFile(IFileInfo fileInfo, ImportMode importMode, DownloadClientItem downloadClientItem)
        {
            var author = _parsingService.GetAuthor(Path.GetFileNameWithoutExtension(fileInfo.Name));

            if (author == null)
            {
                _logger.Debug("Unknown Author for file: {0}", fileInfo.Name);

                return new List<ImportResult>
                       {
                           UnknownAuthorResult(string.Format("Unknown Author for file: {0}", fileInfo.Name), fileInfo.FullName)
                       };
            }

            return ProcessFile(fileInfo, importMode, author, downloadClientItem);
        }

        private List<ImportResult> ProcessFile(IFileInfo fileInfo, ImportMode importMode, Author author, DownloadClientItem downloadClientItem)
        {
            if (Path.GetFileNameWithoutExtension(fileInfo.Name).StartsWith("._"))
            {
                _logger.Debug("[{0}] starts with '._', skipping", fileInfo.FullName);

                return new List<ImportResult>
                       {
                           new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = fileInfo.FullName }, new Rejection("Invalid music file, filename starts with '._'")), "Invalid music file, filename starts with '._'")
                       };
            }

            if (downloadClientItem == null)
            {
                if (_diskProvider.IsFileLocked(fileInfo.FullName))
                {
                    return new List<ImportResult>
                           {
                               FileIsLockedResult(fileInfo.FullName)
                           };
                }
            }

            var idOverrides = new IdentificationOverrides
            {
                Author = author
            };
            var idInfo = new ImportDecisionMakerInfo
            {
                DownloadClientItem = downloadClientItem
            };
            var idConfig = new ImportDecisionMakerConfig
            {
                Filter = FilterFilesType.None,
                NewDownload = true,
                SingleRelease = false,
                IncludeExisting = false,
                AddNewAuthors = false
            };

            var decisions = _importDecisionMaker.GetImportDecisions(new List<IFileInfo>() { fileInfo }, idOverrides, idInfo, idConfig);

            return _importApprovedTracks.Import(decisions, true, downloadClientItem, importMode);
        }

        private string GetCleanedUpFolderName(string folder)
        {
            folder = folder.Replace("_UNPACK_", "")
                           .Replace("_FAILED_", "");

            return folder;
        }

        private bool IsCollectionDownload(IDirectoryInfo directoryInfo, List<IFileInfo> audioFiles, out int itemCount)
        {
            itemCount = 0;
            var threshold = _configService.CollectionDetectionThreshold;

            // Check 1: Folder name patterns suggesting a collection
            if (CollectionFolderPattern.IsMatch(directoryInfo.Name))
            {
                _logger.Debug("Folder name '{0}' matches collection pattern", directoryInfo.Name);

                // Count items for reporting - use text files + audio subdirs as the count
                var textFileCount = audioFiles.Count(f => MediaFileExtensions.TextExtensions.Contains(Path.GetExtension(f.FullName)));
                var audioDirCount = CountSubdirectoriesWithAudioContent(directoryInfo);
                itemCount = Math.Max(textFileCount, audioDirCount);

                if (itemCount < threshold)
                {
                    itemCount = threshold;
                }

                return true;
            }

            // Check 2: Number of text book files (epub, pdf, mobi, azw3, kepub)
            var textFiles = audioFiles.Where(f => MediaFileExtensions.TextExtensions.Contains(Path.GetExtension(f.FullName))).ToList();

            if (textFiles.Count >= threshold)
            {
                itemCount = textFiles.Count;
                _logger.Debug("Found {0} text book files in folder, meets collection threshold of {1}", textFiles.Count, threshold);
                return true;
            }

            // Check 3: Number of subdirectories containing audio files
            var audioSubdirCount = CountSubdirectoriesWithAudioContent(directoryInfo);

            if (audioSubdirCount >= threshold)
            {
                // Check 4: Tag consistency - if all audio files share the same book title, it's NOT a collection
                // (it's likely a single audiobook split across multiple disc/part folders)
                if (HasConsistentBookTitles(audioFiles))
                {
                    _logger.Debug("Audio files across {0} subdirectories all share the same book title - not a collection", audioSubdirCount);
                    return false;
                }

                itemCount = audioSubdirCount;
                _logger.Debug("Found {0} subdirectories with audio content, meets collection threshold of {1}", audioSubdirCount, threshold);
                return true;
            }

            return false;
        }

        private int CountSubdirectoriesWithAudioContent(IDirectoryInfo directoryInfo)
        {
            var count = 0;

            try
            {
                var subdirectories = _diskProvider.GetDirectoryInfos(directoryInfo.FullName);

                foreach (var subdir in subdirectories)
                {
                    try
                    {
                        var files = _diskProvider.GetFiles(subdir.FullName, false);
                        var hasAudioFiles = files.Any(f => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(f)));

                        if (hasAudioFiles)
                        {
                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Unable to check subdirectory for audio content: {0}", subdir.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to enumerate subdirectories: {0}", directoryInfo.FullName);
            }

            return count;
        }

        private bool HasConsistentBookTitles(List<IFileInfo> audioFiles)
        {
            var audioOnlyFiles = audioFiles
                .Where(f => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(f.FullName)))
                .ToList();

            if (!audioOnlyFiles.Any())
            {
                return false;
            }

            try
            {
                string firstTitle = null;

                foreach (var file in audioOnlyFiles)
                {
                    var tagInfo = _metadataTagService.ReadTags(file);

                    if (tagInfo == null || string.IsNullOrWhiteSpace(tagInfo.BookTitle))
                    {
                        continue;
                    }

                    if (firstTitle == null)
                    {
                        firstTitle = tagInfo.BookTitle;
                    }
                    else if (!string.Equals(firstTitle, tagInfo.BookTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        // Different book titles found - this IS a collection
                        return false;
                    }
                }

                // All audio files share the same title (or no titles found)
                return firstTitle != null;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read tags for collection consistency check");
                return false;
            }
        }

        private ImportResult FileIsLockedResult(string audioFile)
        {
            _logger.Debug("[{0}] is currently locked by another process, skipping", audioFile);
            return new ImportResult(new ImportDecision<LocalBook>(new LocalBook { Path = audioFile }, new Rejection("Locked file, try again later")), "Locked file, try again later");
        }

        private ImportResult UnknownAuthorResult(string message, string bookFile = null)
        {
            var localTrack = bookFile == null ? null : new LocalBook { Path = bookFile };

            return new ImportResult(new ImportDecision<LocalBook>(localTrack, new Rejection("Unknown Author")), message);
        }

        private void LogInaccessiblePathError(string path)
        {
            if (_runtimeInfo.IsWindowsService)
            {
                var mounts = _diskProvider.GetMounts();
                var mount = mounts.FirstOrDefault(m => m.RootDirectory == Path.GetPathRoot(path));

                if (mount == null)
                {
                    _logger.Error("Import failed, path does not exist or is not accessible by Readarr: {0}. Unable to find a volume mounted for the path. If you're using a mapped network drive see the FAQ for more info", path);
                    return;
                }

                if (mount.DriveType == DriveType.Network)
                {
                    _logger.Error("Import failed, path does not exist or is not accessible by Readarr: {0}. It's recommended to avoid mapped network drives when running as a Windows service. See the FAQ for more info", path);
                    return;
                }
            }

            if (OsInfo.IsWindows)
            {
                if (path.StartsWith(@"\\"))
                {
                    _logger.Error("Import failed, path does not exist or is not accessible by Readarr: {0}. Ensure the user running Readarr has access to the network share", path);
                    return;
                }
            }

            _logger.Error("Import failed, path does not exist or is not accessible by Readarr: {0}. Ensure the path exists and the user running Readarr has the correct permissions to access this file/folder", path);
        }
    }
}
