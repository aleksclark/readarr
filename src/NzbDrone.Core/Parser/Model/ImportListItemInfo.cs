using System;

namespace NzbDrone.Core.Parser.Model
{
    public class ImportListItemInfo
    {
        public int ImportListId { get; set; }
        public string ImportList { get; set; }
        public string Author { get; set; }
        public string AuthorMetadataId { get; set; }
        public string Book { get; set; }
        public string BookMetadataId { get; set; }
        public string EditionMetadataId { get; set; }
        public DateTime ReleaseDate { get; set; }

        public override string ToString()
        {
            return string.Format("[{0}] {1} [{2}]", ReleaseDate, Author, Book);
        }
    }
}
