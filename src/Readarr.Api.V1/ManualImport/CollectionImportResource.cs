using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MediaFiles.BookImport;
using Readarr.Http.REST;

namespace Readarr.Api.V1.ManualImport
{
    public class CollectionImportResource : RestResource
    {
        public string FilePath { get; set; }
        public string ParsedBookTitle { get; set; }
        public string ParsedAuthor { get; set; }
        public CollectionImportMatchedBookResource MatchedBook { get; set; }
        public double MatchConfidence { get; set; }
        public string LibraryStatus { get; set; }
        public string ExistingQuality { get; set; }
        public string NewQuality { get; set; }
        public bool Recommended { get; set; }
    }

    public class CollectionImportMatchedBookResource
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string AuthorName { get; set; }
    }

    public class CollectionImportRequestResource
    {
        public string Path { get; set; }
    }

    public static class CollectionImportResourceMapper
    {
        public static CollectionImportResource ToResource(this CollectionPreviewItem model)
        {
            if (model == null)
            {
                return null;
            }

            return new CollectionImportResource
            {
                FilePath = model.FilePath,
                ParsedBookTitle = model.ParsedBookTitle,
                ParsedAuthor = model.ParsedAuthor,
                MatchedBook = model.MatchedBook?.ToResource(),
                MatchConfidence = model.MatchConfidence,
                LibraryStatus = model.LibraryStatus,
                ExistingQuality = model.ExistingQuality,
                NewQuality = model.NewQuality,
                Recommended = model.Recommended
            };
        }

        public static CollectionImportMatchedBookResource ToResource(this CollectionPreviewMatchedBook model)
        {
            if (model == null)
            {
                return null;
            }

            return new CollectionImportMatchedBookResource
            {
                Id = model.Id,
                Title = model.Title,
                AuthorName = model.AuthorName
            };
        }

        public static List<CollectionImportResource> ToResource(this IEnumerable<CollectionPreviewItem> models)
        {
            return models.Select(ToResource).ToList();
        }
    }
}
