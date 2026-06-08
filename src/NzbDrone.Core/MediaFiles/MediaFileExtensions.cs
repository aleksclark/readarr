using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles
{
    public static class MediaFileExtensions
    {
        private static readonly Dictionary<string, Quality> _textExtensions;
        private static readonly Dictionary<string, Quality> _audioExtensions;

        static MediaFileExtensions()
        {
            _textExtensions = new Dictionary<string, Quality>(StringComparer.OrdinalIgnoreCase)
            {
                { ".epub", Quality.EPUB },
                { ".kepub", Quality.EPUB },
                { ".mobi", Quality.MOBI },
                { ".azw3", Quality.AZW3 },
                { ".pdf", Quality.PDF },
            };

            _audioExtensions = new Dictionary<string, Quality>(StringComparer.OrdinalIgnoreCase)
            {
                { ".flac", Quality.FLAC },
                { ".ape", Quality.FLAC },
                { ".wavpack", Quality.FLAC },
                { ".wav", Quality.FLAC },
                { ".alac", Quality.FLAC },
                { ".mp2", Quality.MP3 },
                { ".mp3", Quality.MP3 },
                { ".wma", Quality.MP3 },
                { ".m4a", Quality.AAC },
                { ".m4p", Quality.AAC },
                { ".m4b", Quality.M4B },
                { ".aac", Quality.AAC },
                { ".mp4a", Quality.AAC },
                { ".ogg", Quality.OGG },
                { ".oga", Quality.OGG },
                { ".opus", Quality.OPUS },
                { ".vorbis", Quality.OGG },
            };
        }

        public static HashSet<string> TextExtensions => new HashSet<string>(_textExtensions.Keys, StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> AudioExtensions => new HashSet<string>(_audioExtensions.Keys, StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> AllExtensions => new HashSet<string>(_textExtensions.Keys.Concat(_audioExtensions.Keys), StringComparer.OrdinalIgnoreCase);

        public static Quality GetQualityForExtension(string extension)
        {
            if (_textExtensions.ContainsKey(extension))
            {
                return _textExtensions[extension];
            }

            if (_audioExtensions.ContainsKey(extension))
            {
                return _audioExtensions[extension];
            }

            return Quality.Unknown;
        }
    }
}
