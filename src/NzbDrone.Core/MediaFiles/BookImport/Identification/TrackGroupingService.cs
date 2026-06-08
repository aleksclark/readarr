using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public interface ITrackGroupingService
    {
        List<LocalEdition> GroupTracks(List<LocalBook> localTracks);
    }

    public class TrackGroupingService : ITrackGroupingService
    {
        private static readonly Logger _logger = NzbDroneLogger.GetLogger(typeof(TrackGroupingService));

        private static readonly List<string> MultiDiscMarkers = new () { @"dis[ck]", @"cd" };
        private static readonly string MultiDiscPatternFormat = @"^(?<root>.*%s[\W_]*)\d";
        private static readonly List<string> VariousAuthorTitles = new () { "", "various authors", "various", "va", "unknown" };

        public List<LocalEdition> GroupTracks(List<LocalBook> localTracks)
        {
            _logger.ProgressInfo($"Grouping {localTracks.Count} tracks");

            var releases = new List<LocalEdition>();

            // text files are always single file releases
            var textFiles = localTracks.Where(x => MediaFileExtensions.TextExtensions.Contains(Path.GetExtension(x.Path))).ToList();

            foreach (var file in textFiles)
            {
                releases.Add(new LocalEdition(new List<LocalBook> { file }));
            }

            var audioFiles = localTracks.Except(textFiles).ToList();

            // Before standard grouping, check if this looks like an audiobook collection
            // (multiple book-level subdirectories under a common root).
            // If so, split by book directory first, then apply per-book grouping to each.
            var collectionGroups = GroupByCollectionDirectory(audioFiles);
            if (collectionGroups != null)
            {
                _logger.Debug($"Detected audiobook collection with {collectionGroups.Count} book directories");

                foreach (var bookGroup in collectionGroups)
                {
                    var bookTracks = bookGroup.ToList();
                    _logger.Debug($"Processing collection book group: {Path.GetDirectoryName(bookTracks.First().Path)} ({bookTracks.Count} files)");

                    // Apply standard within-book grouping (disc detection, etc.) to each book group
                    var bookReleases = GroupTracksWithinBook(bookTracks);
                    releases.AddRange(bookReleases);
                }

                return releases;
            }

            // Standard grouping: first attempt, assume grouped by folder
            var unprocessed = new List<LocalBook>();
            foreach (var group in GroupTracksByDirectory(audioFiles))
            {
                var tracks = group.ToList();
                if (LooksLikeSingleRelease(tracks))
                {
                    releases.Add(new LocalEdition(tracks));
                }
                else
                {
                    unprocessed.AddRange(tracks);
                }
            }

            // If anything didn't get grouped correctly, try grouping by Book (to pick up VA)
            var unprocessed2 = new List<LocalBook>();
            foreach (var group in unprocessed.GroupBy(x => x.FileTrackInfo.BookTitle))
            {
                _logger.Debug("Falling back to grouping by book tag");
                var tracks = group.ToList();
                if (LooksLikeSingleRelease(tracks))
                {
                    releases.Add(new LocalEdition(tracks));
                }
                else
                {
                    unprocessed2.AddRange(tracks);
                }
            }

            // Finally fall back to grouping by Book/Author pair
            foreach (var group in unprocessed2.GroupBy(x => new { x.FileTrackInfo.AuthorTitle, x.FileTrackInfo.BookTitle }))
            {
                _logger.Debug("Falling back to grouping by book+author tag");
                releases.Add(new LocalEdition(group.ToList()));
            }

            return releases;
        }

        /// <summary>
        /// Detects audiobook collection directory structures where multiple books live
        /// under a common parent. Patterns detected:
        ///   root/Author/BookTitle1/*.mp3
        ///   root/Author/BookTitle2/*.m4b
        ///   root/BookTitle1/CD1/*.mp3, root/BookTitle1/CD2/*.mp3
        ///   root/BookTitle2/single.m4b
        ///
        /// Returns null if this does NOT look like a collection (single book or flat structure).
        /// Returns grouped tracks by book-level directory when it IS a collection.
        /// </summary>
        private List<List<LocalBook>> GroupByCollectionDirectory(List<LocalBook> tracks)
        {
            if (tracks.Count == 0)
            {
                return null;
            }

            // Find the common root directory for all files
            var directories = tracks.Select(x => Path.GetDirectoryName(x.Path)).Distinct().ToList();

            if (directories.Count <= 1)
            {
                // All files in a single directory - not a collection
                return null;
            }

            var commonRoot = GetCommonRootDirectory(directories);
            if (commonRoot == null)
            {
                return null;
            }

            _logger.Trace($"Collection detection: common root is '{commonRoot}'");

            // Get the first-level subdirectories relative to the common root
            // These represent potential book-level directories
            var bookDirectories = GetBookLevelDirectories(tracks, commonRoot);

            if (bookDirectories.Count < 2)
            {
                // Only one book-level directory (or none) - not a collection
                return null;
            }

            // Verify this looks like a collection and not just a multi-disc single book
            // A multi-disc single book has subdirs like CD1, CD2, Disc 1, Disc 2
            // A collection has subdirs that look like different book titles
            if (AllSubdirsAreDiscMarkers(bookDirectories.Keys.ToList(), commonRoot))
            {
                _logger.Trace("Collection detection: subdirectories look like disc markers, not a collection");
                return null;
            }

            _logger.Debug($"Collection detection: found {bookDirectories.Count} book-level directories under '{commonRoot}'");

            return bookDirectories.Values.ToList();
        }

        /// <summary>
        /// Gets the common root directory shared by all file paths.
        /// </summary>
        private string GetCommonRootDirectory(List<string> directories)
        {
            if (directories.Count == 0)
            {
                return null;
            }

            var sorted = directories.OrderBy(x => x.Length).ToList();
            var shortest = sorted.First();

            // Walk up from the shortest path to find a common ancestor
            var candidate = shortest;
            while (candidate != null)
            {
                if (directories.All(d => d.StartsWith(candidate, DiskProviderBase.PathStringComparison)))
                {
                    return candidate;
                }

                candidate = Path.GetDirectoryName(candidate);
            }

            return null;
        }

        /// <summary>
        /// Groups tracks by their book-level directory. The book-level directory is the
        /// first subdirectory below the common root. Files deeper in the tree (e.g. in
        /// CD1/CD2 subdirs) are grouped with their parent book directory.
        /// </summary>
        private Dictionary<string, List<LocalBook>> GetBookLevelDirectories(List<LocalBook> tracks, string commonRoot)
        {
            var result = new Dictionary<string, List<LocalBook>>(StringComparer.OrdinalIgnoreCase);
            var rootWithSep = commonRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (var track in tracks)
            {
                var trackDir = Path.GetDirectoryName(track.Path);
                var bookDir = GetBookLevelDirectory(trackDir, rootWithSep);

                if (bookDir == null)
                {
                    // File is directly in root - use root as its book dir
                    bookDir = commonRoot;
                }

                if (!result.ContainsKey(bookDir))
                {
                    result[bookDir] = new List<LocalBook>();
                }

                result[bookDir].Add(track);
            }

            return result;
        }

        /// <summary>
        /// Given a file's directory and the common root, returns the first-level subdirectory
        /// (the book-level directory). For example:
        ///   root = /media/audiobooks/Author
        ///   trackDir = /media/audiobooks/Author/Book1/CD1
        ///   returns: /media/audiobooks/Author/Book1
        /// </summary>
        private string GetBookLevelDirectory(string trackDir, string rootWithSep)
        {
            if (!trackDir.StartsWith(rootWithSep, DiskProviderBase.PathStringComparison))
            {
                return null;
            }

            var relative = trackDir.Substring(rootWithSep.Length);
            var sepIndex = relative.IndexOf(Path.DirectorySeparatorChar);

            if (sepIndex < 0)
            {
                // trackDir is directly one level below root
                return trackDir;
            }

            // Return root + first subdirectory component
            return rootWithSep + relative.Substring(0, sepIndex);
        }

        /// <summary>
        /// Checks whether all subdirectories look like disc/CD markers (indicating a
        /// multi-disc single book rather than a collection of different books).
        /// </summary>
        private bool AllSubdirsAreDiscMarkers(List<string> bookDirs, string commonRoot)
        {
            var rootWithSep = commonRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            var subdirNames = bookDirs
                .Where(d => d.StartsWith(rootWithSep, DiskProviderBase.PathStringComparison))
                .Select(d => d.Substring(rootWithSep.Length))
                .ToList();

            if (subdirNames.Count == 0)
            {
                return false;
            }

            // Check if ALL subdirectory names match disc marker patterns
            foreach (var marker in MultiDiscMarkers)
            {
                var pattern = $@"^{marker}[\W_]*\d+$";
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);

                if (subdirNames.All(name => regex.IsMatch(name)))
                {
                    return true;
                }
            }

            // Also check for bare numeric names like "1", "2", "3" (part/disc numbers)
            if (subdirNames.All(name => Regex.IsMatch(name, @"^\d+$")))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Applies standard within-book grouping to a set of tracks that belong to the same
        /// audiobook. This handles multi-disc detection (CD1/CD2) and produces one or more
        /// LocalEdition objects, each representing a single release.
        /// </summary>
        private List<LocalEdition> GroupTracksWithinBook(List<LocalBook> tracks)
        {
            var releases = new List<LocalEdition>();

            // Use existing directory-based grouping (handles CD1/CD2 within a book)
            foreach (var group in GroupTracksByDirectory(tracks))
            {
                var groupTracks = group.ToList();
                if (LooksLikeSingleRelease(groupTracks))
                {
                    releases.Add(new LocalEdition(groupTracks));
                }
                else
                {
                    // Try grouping by book tag within this book directory
                    var handledByTag = false;
                    foreach (var tagGroup in groupTracks.GroupBy(x => x.FileTrackInfo.BookTitle))
                    {
                        var tagTracks = tagGroup.ToList();
                        if (LooksLikeSingleRelease(tagTracks))
                        {
                            releases.Add(new LocalEdition(tagTracks));
                            handledByTag = true;
                        }
                    }

                    if (!handledByTag)
                    {
                        // Last resort: treat the whole group as one edition
                        releases.Add(new LocalEdition(groupTracks));
                    }
                }
            }

            // If no groups were produced (shouldn't happen), create one from all tracks
            if (releases.Count == 0 && tracks.Count > 0)
            {
                releases.Add(new LocalEdition(tracks));
            }

            return releases;
        }

        private static bool HasCommonEntry(IEnumerable<string> values, double threshold, double fuzz)
        {
            var groups = values.GroupBy(x => x).OrderByDescending(x => x.Count());
            var distinctCount = groups.Count();
            var mostCommonCount = groups.First().Count();
            var mostCommonEntry = groups.First().Key;
            var totalCount = values.Count();

            // merge groups that are close to the most common value
            foreach (var group in groups.Skip(1))
            {
                if (mostCommonEntry.IsNotNullOrWhiteSpace() &&
                    group.Key.IsNotNullOrWhiteSpace() &&
                    mostCommonEntry.LevenshteinCoefficient(group.Key) > fuzz)
                {
                    distinctCount--;
                    mostCommonCount += group.Count();
                }
            }

            _logger.Trace($"DistinctCount {distinctCount} MostCommonCount {mostCommonCount} TotalCout {totalCount}");

            if (distinctCount > 1 &&
                (distinctCount / (double)totalCount > threshold ||
                 mostCommonCount / (double)totalCount < 1 - threshold))
            {
                return false;
            }

            return true;
        }

        public static bool LooksLikeSingleRelease(List<LocalBook> tracks)
        {
            // returns true if we think all the tracks belong to a single release

            // author/book tags must be the same for 75% of tracks, with no more than 25% having different values
            // (except in the case of various authors)
            const double bookTagThreshold = 0.25;
            const double authorTagThreshold = 0.25;
            const double tagFuzz = 0.9;

            // check that any Book/Release MBID is unique
            if (tracks.Select(x => x.FileTrackInfo.BookMBId).Distinct().Count(x => x.IsNotNullOrWhiteSpace()) > 1 ||
                tracks.Select(x => x.FileTrackInfo.ReleaseMBId).Distinct().Count(x => x.IsNotNullOrWhiteSpace()) > 1)
            {
                _logger.Trace("LooksLikeSingleRelease: MBIDs are not unique");
                return false;
            }

            // check that there's a common book tag.
            var bookTags = tracks.Select(x => x.FileTrackInfo.BookTitle);
            if (!HasCommonEntry(bookTags, bookTagThreshold, tagFuzz))
            {
                _logger.Trace("LooksLikeSingleRelease: No common book tag");
                return false;
            }

            // If not various authors, make sure authors are sensible
            if (!IsVariousAuthors(tracks))
            {
                var authorTags = tracks.Select(x => x.FileTrackInfo.AuthorTitle);
                if (!HasCommonEntry(authorTags, authorTagThreshold, tagFuzz))
                {
                    _logger.Trace("LooksLikeSingleRelease: No common author tag");
                    return false;
                }
            }

            return true;
        }

        public static bool IsVariousAuthors(List<LocalBook> tracks)
        {
            // checks whether most common title is a known VA title
            // Also checks whether more than 75% of tracks have a distinct author and that the most common author
            // is responsible for < 25% of tracks
            const double authorTagThreshold = 0.75;
            const double tagFuzz = 0.9;

            var authorTags = tracks.Select(x => x.FileTrackInfo.AuthorTitle).ToList();

            if (!HasCommonEntry(authorTags, authorTagThreshold, tagFuzz))
            {
                return true;
            }

            if (VariousAuthorTitles.Contains(authorTags.GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private IEnumerable<List<LocalBook>> GroupTracksByDirectory(List<LocalBook> tracks)
        {
            // we want to check for layouts like:
            // xx/CD1/1.mp3
            // xx/CD2/1.mp3
            // or
            // yy Disc 1/1.mp3
            // yy Disc 2/1.mp3
            // and group them.

            // we only bother doing this for the immediate parent directory.
            var trackFolders = tracks.Select(x => Tuple.Create(x, Path.GetDirectoryName(x.Path))).ToList();

            var distinctFolders = trackFolders.Select(x => x.Item2).Distinct().ToList();
            distinctFolders.Sort();

            _logger.Trace("Folders:\n{0}", string.Join("\n", distinctFolders));

            Regex subdirRegex = null;
            var output = new List<LocalBook>();
            foreach (var folder in distinctFolders)
            {
                if (subdirRegex != null)
                {
                    if (subdirRegex.IsMatch(folder))
                    {
                        // current folder continues match, so append output
                        output.AddRange(tracks.Where(x => x.Path.StartsWith(folder)));
                        continue;
                    }
                }

                // we have finished a multi disc match.  yield the previous output
                // and check current folder
                if (output.Count > 0)
                {
                    _logger.Trace("Yielding from 1:\n{0}", string.Join("\n", output));
                    yield return output;

                    output = new List<LocalBook>();
                }

                // reset and put current folder into output
                subdirRegex = null;
                var currentTracks = trackFolders.Where(x => x.Item2.Equals(folder, DiskProviderBase.PathStringComparison))
                    .Select(x => x.Item1);
                output.AddRange(currentTracks);

                // check if the start of another multi disc match
                foreach (var marker in MultiDiscMarkers)
                {
                    // check if this is the first of a multi-disc set of folders
                    var pattern = MultiDiscPatternFormat.Replace("%s", marker);
                    var multiStartRegex = new Regex(pattern, RegexOptions.IgnoreCase);

                    var match = multiStartRegex.Match(folder);
                    if (match.Success)
                    {
                        var subdirPattern = $"^{Regex.Escape(match.Groups["root"].ToString())}\\d+$";
                        subdirRegex = new Regex(subdirPattern, RegexOptions.IgnoreCase);
                        break;
                    }
                }

                if (subdirRegex == null)
                {
                    // not the start of a multi-disc match, yield
                    _logger.Trace("Yielding from 2:\n{0}", string.Join("\n", output));
                    yield return output;

                    // reset output
                    output = new List<LocalBook>();
                }
            }

            // return the final stored output
            if (output.Count > 0)
            {
                _logger.Trace("Yielding final:\n{0}", string.Join("\n", output));
                yield return output;
            }
        }
    }
}
