using System;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Exception thrown when a metadata source operation fails.
    /// Replaces the legacy GoodreadsException.
    /// </summary>
    public class MetadataSourceException : Exception
    {
        public MetadataSourceException(string message)
            : base(message)
        {
        }

        public MetadataSourceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public MetadataSourceException(string message, Exception innerException, params object[] args)
            : base(string.Format(message, args), innerException)
        {
        }
    }
}
