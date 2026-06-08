using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    public class SeriesResource
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public List<SeriesWorkLink> Works { get; set; } = new();
    }

    public class SeriesWorkLink
    {
        public string WorkId { get; set; }
        public string PositionInSeries { get; set; }
    }

    public interface IProvideSeriesInfo
    {
        SeriesResource GetSeriesInfo(int id, bool useCache = true);
    }
}
