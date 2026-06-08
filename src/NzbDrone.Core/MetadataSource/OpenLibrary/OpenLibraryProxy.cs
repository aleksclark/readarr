using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public interface IOpenLibraryProxy
    {
        List<OpenLibrarySearchDoc> Search(string query, int limit = 20);
        OpenLibraryWorkResource GetWork(string workId);
        OpenLibraryAuthorResource GetAuthor(string authorId);
        List<OpenLibraryWorkResource> GetAuthorWorks(string authorId, int limit = 50);
        List<OpenLibraryEditionResource> GetWorkEditions(string workId, int limit = 50);
        OpenLibraryEditionResource GetEdition(string editionId);
        OpenLibraryEditionResource GetEditionByIsbn(string isbn);
    }

    public class OpenLibraryProxy : IOpenLibraryProxy,
                                     IProvideAuthorInfo,
                                     IProvideBookInfo,
                                     ISearchForNewAuthor,
                                     ISearchForNewBook,
                                     ISearchForNewEntity
    {
        private const string BaseUrl = "https://openlibrary.org";
        private const string CoversBaseUrl = "https://covers.openlibrary.org";

        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly Logger _logger;
        private readonly IHttpRequestBuilderFactory _requestBuilder;

        public OpenLibraryProxy(ICachedHttpResponseService cachedHttpClient, Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _logger = logger;

            _requestBuilder = new HttpRequestBuilder(BaseUrl + "/{route}")
                .SetHeader("User-Agent", "Readarr/1.0 (https://github.com/aleksclark/readarr)")
                .SetHeader("Accept", "application/json")
                .KeepAlive()
                .CreateFactory();
        }

        public List<OpenLibrarySearchDoc> Search(string query, int limit = 20)
        {
            _logger.Debug("Searching Open Library for: {0}", query);

            var httpRequest = new HttpRequestBuilder(BaseUrl + "/search.json")
                .AddQueryParam("q", query)
                .AddQueryParam("limit", limit)
                .AddQueryParam("fields", "key,title,author_name,author_key,first_publish_year,edition_count,isbn,cover_i,subject,language,publisher,number_of_pages_median,ratings_average,ratings_count,edition_key")
                .SetHeader("User-Agent", "Readarr/1.0 (https://github.com/aleksclark/readarr)")
                .SetHeader("Accept", "application/json")
                .Build();

            var response = ExecuteRequest<OpenLibrarySearchResponse>(httpRequest, TimeSpan.FromHours(6));
            return response?.Docs ?? new List<OpenLibrarySearchDoc>();
        }

        public OpenLibraryWorkResource GetWork(string workId)
        {
            _logger.Debug("Getting Work: {0}", workId);

            var httpRequest = _requestBuilder.Create()
                .SetSegment("route", $"works/{workId}.json")
                .Build();

            return ExecuteRequest<OpenLibraryWorkResource>(httpRequest, TimeSpan.FromDays(1));
        }

        public OpenLibraryAuthorResource GetAuthor(string authorId)
        {
            _logger.Debug("Getting Author: {0}", authorId);

            var httpRequest = _requestBuilder.Create()
                .SetSegment("route", $"authors/{authorId}.json")
                .Build();

            return ExecuteRequest<OpenLibraryAuthorResource>(httpRequest, TimeSpan.FromDays(1));
        }

        public List<OpenLibraryWorkResource> GetAuthorWorks(string authorId, int limit = 50)
        {
            _logger.Debug("Getting works for author: {0}", authorId);

            var httpRequest = new HttpRequestBuilder(BaseUrl + $"/authors/{authorId}/works.json")
                .AddQueryParam("limit", limit)
                .SetHeader("User-Agent", "Readarr/1.0 (https://github.com/aleksclark/readarr)")
                .SetHeader("Accept", "application/json")
                .Build();

            var response = ExecuteRequest<OpenLibraryAuthorWorksResponse>(httpRequest, TimeSpan.FromDays(1));
            return response?.Entries ?? new List<OpenLibraryWorkResource>();
        }

        public List<OpenLibraryEditionResource> GetWorkEditions(string workId, int limit = 50)
        {
            _logger.Debug("Getting editions for work: {0}", workId);

            var httpRequest = new HttpRequestBuilder(BaseUrl + $"/works/{workId}/editions.json")
                .AddQueryParam("limit", limit)
                .SetHeader("User-Agent", "Readarr/1.0 (https://github.com/aleksclark/readarr)")
                .SetHeader("Accept", "application/json")
                .Build();

            var response = ExecuteRequest<OpenLibraryEditionsResponse>(httpRequest, TimeSpan.FromDays(1));
            return response?.Entries ?? new List<OpenLibraryEditionResource>();
        }

        public OpenLibraryEditionResource GetEdition(string editionId)
        {
            _logger.Debug("Getting Edition: {0}", editionId);

            var httpRequest = _requestBuilder.Create()
                .SetSegment("route", $"books/{editionId}.json")
                .Build();

            return ExecuteRequest<OpenLibraryEditionResource>(httpRequest, TimeSpan.FromDays(1));
        }

        public OpenLibraryEditionResource GetEditionByIsbn(string isbn)
        {
            _logger.Debug("Getting Edition by ISBN: {0}", isbn);

            var httpRequest = _requestBuilder.Create()
                .SetSegment("route", $"isbn/{isbn}.json")
                .Build();

            httpRequest.AllowAutoRedirect = true;
            return ExecuteRequest<OpenLibraryEditionResource>(httpRequest, TimeSpan.FromDays(7));
        }

        public Author GetAuthorInfo(string readarrId, bool useCache = true)
        {
            // readarrId is the OL Author ID, e.g. "OL23919A"
            var olAuthor = GetAuthor(readarrId);
            if (olAuthor == null)
            {
                throw new AuthorNotFoundException(readarrId);
            }

            var works = GetAuthorWorks(readarrId);
            return MapAuthor(olAuthor, works);
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime)
        {
            // Open Library doesn't have a changes API that's practical to poll.
            // Return empty set — rely on periodic full refresh.
            return new HashSet<string>();
        }

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignEditionId)
        {
            var edition = GetEdition(foreignEditionId);
            if (edition == null)
            {
                throw new BookNotFoundException(foreignEditionId);
            }

            // Get the parent work
            var workKey = edition.Works?.FirstOrDefault()?.Key?.Replace("/works/", "");
            if (workKey.IsNullOrWhiteSpace())
            {
                throw new BookNotFoundException(foreignEditionId);
            }

            var work = GetWork(workKey);
            var editions = GetWorkEditions(workKey);

            var book = MapWork(work, editions);
            var authors = new List<AuthorMetadata>();

            if (work.Authors != null)
            {
                foreach (var authorRole in work.Authors)
                {
                    var authorId = authorRole.Author?.Key?.Replace("/authors/", "");
                    if (authorId.IsNotNullOrWhiteSpace())
                    {
                        var olAuthor = GetAuthor(authorId);
                        if (olAuthor != null)
                        {
                            authors.Add(MapAuthorMetadata(olAuthor));
                        }
                    }
                }
            }

            return new Tuple<string, Book, List<AuthorMetadata>>(workKey, book, authors);
        }

        public List<Author> SearchForNewAuthor(string title)
        {
            var results = Search(title, 10);
            var authorMap = new Dictionary<string, Author>();

            foreach (var doc in results)
            {
                if (doc.AuthorKeys == null || doc.AuthorNames == null)
                {
                    continue;
                }

                for (var i = 0; i < Math.Min(doc.AuthorKeys.Count, doc.AuthorNames.Count); i++)
                {
                    var authorKey = doc.AuthorKeys[i];
                    if (!authorMap.ContainsKey(authorKey))
                    {
                        var author = new Author
                        {
                            Metadata = new AuthorMetadata
                            {
                                ForeignAuthorId = authorKey,
                                Name = doc.AuthorNames[i],
                                Status = AuthorStatusType.Continuing,
                            },
                            CleanName = doc.AuthorNames[i].CleanAuthorName(),
                        };

                        authorMap[authorKey] = author;
                    }
                }
            }

            return authorMap.Values.ToList();
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            var query = author.IsNotNullOrWhiteSpace() ? $"{title} {author}" : title;
            return SearchForNewBook(query);
        }

        public List<Book> SearchForNewBook(string title)
        {
            var results = Search(title, 20);
            return results.Select(MapSearchDocToBook).Where(b => b != null).ToList();
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            var edition = GetEditionByIsbn(isbn);
            if (edition == null)
            {
                return new List<Book>();
            }

            var workKey = edition.Works?.FirstOrDefault()?.Key?.Replace("/works/", "");
            if (workKey.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            var work = GetWork(workKey);
            var editions = GetWorkEditions(workKey);
            var book = MapWork(work, editions);
            return new List<Book> { book };
        }

        public List<Book> SearchByAsin(string asin)
        {
            // Open Library doesn't have great ASIN search — fall back to general search
            var results = Search(asin, 5);
            return results.Select(MapSearchDocToBook).Where(b => b != null).ToList();
        }

        public List<Book> SearchByOpenLibraryWorkId(string workId, bool getAllEditions = true)
        {
            var work = GetWork(workId);
            if (work == null)
            {
                return new List<Book>();
            }

            var editions = getAllEditions ? GetWorkEditions(workId) : new List<OpenLibraryEditionResource>();
            var book = MapWork(work, editions);
            return new List<Book> { book };
        }

        public List<object> SearchForNewEntity(string title)
        {
            var books = SearchForNewBook(title);
            return books.Cast<object>().ToList();
        }

        private Author MapAuthor(OpenLibraryAuthorResource olAuthor, List<OpenLibraryWorkResource> works)
        {
            var metadata = MapAuthorMetadata(olAuthor);

            var author = new Author
            {
                Metadata = new LazyLoaded<AuthorMetadata>(metadata),
                CleanName = metadata.Name.CleanAuthorName(),
                ForeignAuthorId = olAuthor.AuthorId,
            };

            return author;
        }

        private AuthorMetadata MapAuthorMetadata(OpenLibraryAuthorResource olAuthor)
        {
            var images = new List<MediaCover.MediaCover>();
            if (olAuthor.Photos != null && olAuthor.Photos.Count > 0)
            {
                var photoId = olAuthor.Photos.First(p => p > 0);
                images.Add(new MediaCover.MediaCover
                {
                    Url = $"{CoversBaseUrl}/a/id/{photoId}-L.jpg",
                    CoverType = MediaCoverTypes.Poster
                });
            }

            var links = new List<Links>();
            if (olAuthor.Wikipedia.IsNotNullOrWhiteSpace())
            {
                links.Add(new Links { Url = olAuthor.Wikipedia, Name = "Wikipedia" });
            }

            links.Add(new Links
            {
                Url = $"{BaseUrl}/authors/{olAuthor.AuthorId}",
                Name = "Open Library"
            });

            return new AuthorMetadata
            {
                ForeignAuthorId = olAuthor.AuthorId,
                Name = olAuthor.Name ?? olAuthor.PersonalName ?? "Unknown",
                Overview = olAuthor.GetBio(),
                Images = images,
                Links = links,
                Status = AuthorStatusType.Continuing,
            };
        }

        private Book MapWork(OpenLibraryWorkResource work, List<OpenLibraryEditionResource> olEditions)
        {
            var genres = new List<string>();
            if (work.Subjects != null)
            {
                genres = work.Subjects
                    .Take(10)
                    .Select(s => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()))
                    .ToList();
            }

            DateTime? releaseDate = null;
            if (work.FirstPublishDate.IsNotNullOrWhiteSpace())
            {
                releaseDate = ParseDate(work.FirstPublishDate);
            }

            var book = new Book
            {
                ForeignBookId = work.WorkId,
                Title = work.Title,
                CleanTitle = work.Title?.CleanAuthorName(),
                ReleaseDate = releaseDate,
                Genres = genres,
                Links = new List<Links>
                {
                    new Links { Url = $"{BaseUrl}/works/{work.WorkId}", Name = "Open Library" }
                },
                Ratings = new Ratings(),
            };

            // Map editions
            var editions = new List<Edition>();
            foreach (var olEdition in olEditions ?? Enumerable.Empty<OpenLibraryEditionResource>())
            {
                var edition = MapEdition(olEdition, book);
                if (edition != null)
                {
                    editions.Add(edition);
                }
            }

            // Set the first edition with an ISBN as the preferred/foreign edition
            var preferredEdition = editions.FirstOrDefault(e => e.Isbn13.IsNotNullOrWhiteSpace())
                                  ?? editions.FirstOrDefault();
            if (preferredEdition != null)
            {
                book.ForeignEditionId = preferredEdition.ForeignEditionId;
            }

            book.Editions = new LazyLoaded<List<Edition>>(editions);

            return book;
        }

        private Edition MapEdition(OpenLibraryEditionResource olEdition, Book parentBook)
        {
            if (olEdition == null)
            {
                return null;
            }

            var images = new List<MediaCover.MediaCover>();
            if (olEdition.Covers != null && olEdition.Covers.Count > 0)
            {
                var coverId = olEdition.Covers.First(c => c > 0);
                images.Add(new MediaCover.MediaCover
                {
                    Url = $"{CoversBaseUrl}/b/id/{coverId}-L.jpg",
                    CoverType = MediaCoverTypes.Cover
                });
            }

            var format = olEdition.PhysicalFormat ?? "";
            var isEbook = olEdition.IsEbook();

            return new Edition
            {
                ForeignEditionId = olEdition.EditionId,
                Title = olEdition.FullTitle ?? olEdition.Title ?? parentBook?.Title ?? "Unknown Edition",
                Isbn13 = olEdition.GetIsbn13() ?? ConvertIsbn10ToIsbn13(olEdition.GetIsbn10()),
                Asin = GetAsin(olEdition),
                Language = olEdition.GetLanguageCode() ?? "eng",
                Publisher = olEdition.Publishers?.FirstOrDefault() ?? "",
                PageCount = olEdition.NumberOfPages ?? 0,
                ReleaseDate = ParseDate(olEdition.PublishDate),
                Format = format,
                IsEbook = isEbook,
                Images = images,
                Links = new List<Links>
                {
                    new Links { Url = $"{BaseUrl}/books/{olEdition.EditionId}", Name = "Open Library" }
                },
                Ratings = new Ratings(),
                Monitored = false,
            };
        }

        private Book MapSearchDocToBook(OpenLibrarySearchDoc doc)
        {
            if (doc == null || doc.Title.IsNullOrWhiteSpace())
            {
                return null;
            }

            DateTime? releaseDate = null;
            if (doc.FirstPublishYear.HasValue)
            {
                releaseDate = new DateTime(doc.FirstPublishYear.Value, 1, 1);
            }

            var images = new List<MediaCover.MediaCover>();
            if (doc.CoverId.HasValue && doc.CoverId.Value > 0)
            {
                images.Add(new MediaCover.MediaCover
                {
                    Url = $"{CoversBaseUrl}/b/id/{doc.CoverId.Value}-L.jpg",
                    CoverType = MediaCoverTypes.Cover
                });
            }

            var book = new Book
            {
                ForeignBookId = doc.WorkId,
                Title = doc.Title,
                CleanTitle = doc.Title.CleanAuthorName(),
                ReleaseDate = releaseDate,
                Genres = doc.Subjects?.Take(5).ToList() ?? new List<string>(),
                Ratings = new Ratings
                {
                    Value = (decimal)(doc.RatingsAverage ?? 0),
                    Votes = doc.RatingsCount ?? 0,
                },
                Links = new List<Links>
                {
                    new Links { Url = $"{BaseUrl}/works/{doc.WorkId}", Name = "Open Library" }
                },
            };

            // Set author metadata
            if (doc.AuthorNames != null && doc.AuthorNames.Count > 0)
            {
                var authorId = doc.AuthorKeys?.FirstOrDefault() ?? "";
                book.AuthorMetadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata
                {
                    ForeignAuthorId = authorId,
                    Name = doc.AuthorNames.First(),
                });
            }

            // Create a synthetic edition from search data
            var edition = new Edition
            {
                ForeignEditionId = doc.EditionKeys?.FirstOrDefault() ?? doc.WorkId,
                Title = doc.Title,
                Isbn13 = doc.Isbns?.FirstOrDefault(i => i.Length == 13),
                PageCount = doc.NumberOfPagesMedian ?? 0,
                Publisher = doc.Publishers?.FirstOrDefault() ?? "",
                Images = images,
                Monitored = false,
                Ratings = book.Ratings,
            };

            book.Editions = new LazyLoaded<List<Edition>>(new List<Edition> { edition });
            book.ForeignEditionId = edition.ForeignEditionId;

            return book;
        }

        private T ExecuteRequest<T>(HttpRequest httpRequest, TimeSpan cacheDuration)
            where T : class
        {
            httpRequest.AllowAutoRedirect = true;
            httpRequest.SuppressHttpError = true;

            var httpResponse = _cachedHttpClient.Get(httpRequest, true, cacheDuration);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.Warn("Open Library rate limit hit. Waiting...");
                    System.Threading.Thread.Sleep(5000);
                    httpResponse = _cachedHttpClient.Get(httpRequest, false, cacheDuration);
                }

                if (httpResponse.HasHttpError)
                {
                    throw new HttpException(httpRequest, httpResponse);
                }
            }

            try
            {
                var content = httpResponse.Content;
                return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "Failed to deserialize Open Library response for {0}", httpRequest.Url);
                return null;
            }
        }

        private static DateTime? ParseDate(string dateStr)
        {
            if (dateStr.IsNullOrWhiteSpace())
            {
                return null;
            }

            // OL dates come in various formats: "2005", "March 2005", "Mar 15, 2005", etc.
            string[] formats =
            {
                "yyyy",
                "MMMM yyyy",
                "MMM yyyy",
                "MMMM d, yyyy",
                "MMM d, yyyy",
                "yyyy-MM-dd",
                "d MMMM yyyy",
                "d MMM yyyy",
            };

            if (DateTime.TryParseExact(
                dateStr.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result))
            {
                return result;
            }

            // Last resort: try general parse
            if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                return result;
            }

            // If it's just a 4-digit year
            if (int.TryParse(dateStr.Trim(), out var year) && year > 1000 && year < 3000)
            {
                return new DateTime(year, 1, 1);
            }

            return null;
        }

        private static string ConvertIsbn10ToIsbn13(string isbn10)
        {
            if (isbn10.IsNullOrWhiteSpace() || isbn10.Length != 10)
            {
                return null;
            }

            var isbn13 = "978" + isbn10.Substring(0, 9);
            var sum = 0;
            for (var i = 0; i < 12; i++)
            {
                var digit = isbn13[i] - '0';
                sum += (i % 2 == 0) ? digit : digit * 3;
            }

            var checkDigit = (10 - (sum % 10)) % 10;
            return isbn13 + checkDigit.ToString();
        }

        private static string GetAsin(OpenLibraryEditionResource edition)
        {
            if (edition.Identifiers == null)
            {
                return null;
            }

            if (edition.Identifiers.TryGetValue("amazon", out var amazonIds) && amazonIds.Count > 0)
            {
                return amazonIds[0];
            }

            return null;
        }
    }
}
