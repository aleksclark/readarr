using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    public class ListResource
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public List<ListBookItem> Books { get; set; } = new ();
        public int TotalItems { get; set; }
        public int CurrentPage { get; set; }
    }

    public class ListBookItem
    {
        public string WorkId { get; set; }
        public string Title { get; set; }
        public string AuthorName { get; set; }
    }

    public interface IProvideListInfo
    {
        ListResource GetListInfo(int id, int page, bool useCache = true);
    }
}
