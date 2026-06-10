using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public interface IIdentificationService
    {
        List<LocalEdition> Identify(List<LocalBook> localTracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config);
    }

    public class IdentificationService : IIdentificationService
    {
        private readonly ITrackGroupingService _trackGroupingService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IAugmentingService _augmentingService;
        private readonly ICandidateService _candidateService;
        private readonly Logger _logger;

        public IdentificationService(ITrackGroupingService trackGroupingService,
                                     IMetadataTagService metadataTagService,
                                     IAugmentingService augmentingService,
                                     ICandidateService candidateService,
                                     Logger logger)
        {
            _trackGroupingService = trackGroupingService;
            _metadataTagService = metadataTagService;
            _augmentingService = augmentingService;
            _candidateService = candidateService;
            _logger = logger;
        }

        public List<LocalEdition> GetLocalBookReleases(List<LocalBook> localTracks, bool singleRelease)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<LocalEdition> releases;
            if (singleRelease)
            {
                releases = new List<LocalEdition> { new LocalEdition(localTracks) };
            }
            else
            {
                releases = _trackGroupingService.GroupTracks(localTracks);
            }

            _logger.Debug($"Sorted {localTracks.Count} tracks into {releases.Count} releases in {watch.ElapsedMilliseconds}ms");

            foreach (var localRelease in releases)
            {
                try
                {
                    _augmentingService.Augment(localRelease);
                }
                catch (AugmentingFailedException)
                {
                    _logger.Warn($"Augmentation failed for {localRelease}");
                }
            }

            return releases;
        }

        public List<LocalEdition> Identify(List<LocalBook> localTracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config)
        {
            // 1 group localTracks so that we think they represent a single release
            // 2 get candidates given specified author, book and release.  Candidates can include extra files already on disk.
            // 3 find best candidate
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Starting book identification");

            var releases = GetLocalBookReleases(localTracks, config.SingleRelease);

            // If this is a collection, use enhanced identification
            if (config.IsCollection)
            {
                _logger.Debug("Using collection-aware identification for {0} releases", releases.Count);
                IdentifyCollection(releases, idOverrides, config);
            }
            else
            {
                var i = 0;
                foreach (var localRelease in releases)
                {
                    i++;
                    _logger.ProgressInfo($"Identifying book {i}/{releases.Count}");
                    _logger.Debug($"Identifying book files:\n{localRelease.LocalBooks.Select(x => x.Path).ConcatToString("\n")}");

                    try
                    {
                        IdentifyRelease(localRelease, idOverrides, config);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Error identifying release");
                    }
                }
            }

            watch.Stop();

            _logger.Debug($"Track identification for {localTracks.Count} tracks took {watch.ElapsedMilliseconds}ms");

            return releases;
        }

        private void IdentifyCollection(List<LocalEdition> releases, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config)
        {
            // For collections, determine if all files share the same author
            var allBooks = releases.SelectMany(r => r.LocalBooks).ToList();
            var authorHint = GetCollectionAuthorHint(allBooks);

            List<CandidateEdition> collectionCandidates = null;
            if (authorHint.IsNotNullOrWhiteSpace())
            {
                _logger.Debug("Collection appears to be from single author: '{0}', pre-fetching all editions", authorHint);
                collectionCandidates = _candidateService.GetCollectionCandidates(authorHint);
                _logger.Debug("Got {0} collection candidates for author '{1}'", collectionCandidates.Count, authorHint);
            }

            var i = 0;
            foreach (var localRelease in releases)
            {
                i++;
                _logger.ProgressInfo($"Identifying book {i}/{releases.Count} (collection mode)");
                localRelease.IsCollection = true;

                // Use enhanced filename parsing to improve metadata for each file in the release
                EnhanceCollectionMetadata(localRelease, authorHint);

                try
                {
                    if (collectionCandidates != null && collectionCandidates.Any())
                    {
                        IdentifyReleaseFromCollectionCandidates(localRelease, collectionCandidates, idOverrides, config);
                    }
                    else
                    {
                        // Fall back to standard identification
                        IdentifyRelease(localRelease, idOverrides, config);
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Error identifying collection release");
                }
            }
        }

        private string GetCollectionAuthorHint(List<LocalBook> localBooks)
        {
            // Check file metadata for consensus author
            var authorTags = localBooks
                .Where(x => x.FileTrackInfo?.Authors != null && x.FileTrackInfo.Authors.Any())
                .Select(x => x.FileTrackInfo.Authors.FirstOrDefault())
                .Where(x => x.IsNotNullOrWhiteSpace())
                .ToList();

            if (!authorTags.Any())
            {
                return null;
            }

            // Find the most common author
            var mostCommonAuthor = authorTags
                .GroupBy(x => x.ToLowerInvariant().Trim())
                .OrderByDescending(x => x.Count())
                .First();

            // If at least half the files agree on author, use it as the hint
            if (mostCommonAuthor.Count() >= (localBooks.Count / 2.0))
            {
                return mostCommonAuthor.First();
            }

            return null;
        }

        private void EnhanceCollectionMetadata(LocalEdition localRelease, string authorHint)
        {
            foreach (var localBook in localRelease.LocalBooks)
            {
                var fileName = Path.GetFileName(localBook.Path);
                var parsedTitle = NzbDrone.Core.Parser.Parser.ParseCollectionBookTitle(fileName, authorHint);

                if (parsedTitle.IsNotNullOrWhiteSpace() && localBook.FileTrackInfo != null)
                {
                    // If the file has no book title from tags, use the parsed one
                    if (localBook.FileTrackInfo.BookTitle.IsNullOrWhiteSpace())
                    {
                        _logger.Trace("Setting book title from filename parsing: '{0}' for file '{1}'", parsedTitle, fileName);
                        localBook.FileTrackInfo.BookTitle = parsedTitle;
                    }

                    // If the file has no author from tags but we have a hint, use it
                    if (authorHint.IsNotNullOrWhiteSpace() && !localBook.FileTrackInfo.Authors.Any(a => a.IsNotNullOrWhiteSpace()))
                    {
                        _logger.Trace("Setting author from collection hint: '{0}' for file '{1}'", authorHint, fileName);
                        localBook.FileTrackInfo.Authors = new List<string> { authorHint };
                    }
                }
            }
        }

        private void IdentifyReleaseFromCollectionCandidates(LocalEdition localBookRelease, List<CandidateEdition> collectionCandidates, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Identifying collection release from {0} pre-fetched candidates", collectionCandidates.Count);

            // First try matching against the pre-fetched collection candidates
            GetBestRelease(localBookRelease, collectionCandidates, new List<LocalBook>(), out var seenCandidate);

            // If the match is not good enough, fall back to standard identification which includes remote search
            if (!seenCandidate || localBookRelease.Distance.NormalizedDistance() > 0.15)
            {
                _logger.Debug("Collection candidate match not good enough (distance: {0}), falling back to standard identification",
                    localBookRelease.Edition != null ? localBookRelease.Distance.NormalizedDistance().ToString("F3") : "N/A");
                IdentifyRelease(localBookRelease, idOverrides, config);
                return;
            }

            _logger.Debug($"Collection release identified in {watch.ElapsedMilliseconds}ms");
            localBookRelease.PopulateMatch(config.KeepAllEditions);
        }

        private List<LocalBook> ToLocalTrack(IEnumerable<BookFile> trackfiles, LocalEdition localRelease)
        {
            var scanned = trackfiles.Join(localRelease.LocalBooks, t => t.Path, l => l.Path, (track, localTrack) => localTrack);
            var toScan = trackfiles.ExceptBy(t => t.Path, scanned, s => s.Path, StringComparer.InvariantCulture);
            var localTracks = scanned.Concat(toScan.Select(x => new LocalBook
            {
                Path = x.Path,
                Size = x.Size,
                Modified = x.Modified,
                FileTrackInfo = _metadataTagService.ReadTags((FileInfoBase)new FileInfo(x.Path)),
                ExistingFile = true,
                AdditionalFile = true,
                Quality = x.Quality
            }))
            .ToList();

            localTracks.ForEach(x => _augmentingService.Augment(x, true));

            return localTracks;
        }

        private void IdentifyRelease(LocalEdition localBookRelease, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var usedRemote = false;

            IEnumerable<CandidateEdition> candidateReleases = _candidateService.GetDbCandidatesFromTags(localBookRelease, idOverrides, config.IncludeExisting);

            // convert all the TrackFiles that represent extra files to List<LocalTrack>
            // local candidates are actually a list so this is fine to enumerate
            var allLocalTracks = ToLocalTrack(candidateReleases
                .SelectMany(x => x.ExistingFiles)
                .DistinctBy(x => x.Path), localBookRelease);

            _logger.Debug($"Retrieved {allLocalTracks.Count} possible tracks in {watch.ElapsedMilliseconds}ms");

            if (!candidateReleases.Any())
            {
                _logger.Debug("No local candidates found, trying remote");
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides);
                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => x.Edition.Book.Value.Id > 0 && x.Edition.Book.Value.AuthorId > 0);
                }

                usedRemote = true;
            }

            GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, out var seenCandidate);

            if (!seenCandidate)
            {
                // can't find any candidates even after using remote search
                // populate the overrides and return
                foreach (var localTrack in localBookRelease.LocalBooks)
                {
                    localTrack.Edition = idOverrides.Edition;
                    localTrack.Book = idOverrides.Book;
                    localTrack.Author = idOverrides.Author;
                }

                return;
            }

            // If the result isn't great and we haven't tried remote candidates, try looking for remote candidates
            // Open Library may have a better edition of a local book
            if (localBookRelease.Distance.NormalizedDistance() > 0.15 && !usedRemote)
            {
                _logger.Debug("Match not good enough, trying remote candidates");
                candidateReleases = _candidateService.GetRemoteCandidates(localBookRelease, idOverrides);

                if (!config.AddNewAuthors)
                {
                    candidateReleases = candidateReleases.Where(x => x.Edition.Book.Value.Id > 0);
                }

                GetBestRelease(localBookRelease, candidateReleases, allLocalTracks, out _);
            }

            _logger.Debug($"Best release found in {watch.ElapsedMilliseconds}ms");

            localBookRelease.PopulateMatch(config.KeepAllEditions);

            _logger.Debug($"IdentifyRelease done in {watch.ElapsedMilliseconds}ms");
        }

        private void GetBestRelease(LocalEdition localBookRelease, IEnumerable<CandidateEdition> candidateReleases, List<LocalBook> extraTracksOnDisk, out bool seenCandidate)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            _logger.Debug("Matching {0} track files against candidates", localBookRelease.TrackCount);
            _logger.Trace("Processing files:\n{0}", string.Join("\n", localBookRelease.LocalBooks.Select(x => x.Path)));

            var bestDistance = localBookRelease.Edition != null ? localBookRelease.Distance.NormalizedDistance() : 1.0;
            seenCandidate = false;

            foreach (var candidateRelease in candidateReleases)
            {
                seenCandidate = true;

                var release = candidateRelease.Edition;
                _logger.Debug($"Trying Release {release}");
                var rwatch = System.Diagnostics.Stopwatch.StartNew();

                var extraTrackPaths = candidateRelease.ExistingFiles.Select(x => x.Path).ToList();
                var extraTracks = extraTracksOnDisk.Where(x => extraTrackPaths.Contains(x.Path)).ToList();
                var allLocalTracks = localBookRelease.LocalBooks.Concat(extraTracks).DistinctBy(x => x.Path).ToList();

                var distance = DistanceCalculator.BookDistance(allLocalTracks, release);
                var currDistance = distance.NormalizedDistance();

                rwatch.Stop();
                _logger.Debug("Release {0} has distance {1} vs best distance {2} [{3}ms]",
                              release,
                              currDistance,
                              bestDistance,
                              rwatch.ElapsedMilliseconds);
                if (currDistance < bestDistance)
                {
                    bestDistance = currDistance;
                    localBookRelease.Distance = distance;
                    localBookRelease.Edition = release;
                    localBookRelease.ExistingTracks = extraTracks;
                    if (currDistance == 0.0)
                    {
                        break;
                    }
                }
            }

            watch.Stop();
            _logger.Debug($"Best release: {localBookRelease.Edition} Distance {localBookRelease.Distance.NormalizedDistance()} found in {watch.ElapsedMilliseconds}ms");
        }
    }
}
