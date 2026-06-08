using System.Collections.Generic;
using System.Data;
using Dapper;
using FluentMigrator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(044)]
    public class add_new_audio_qualities : NzbDroneMigrationBase
    {
        // Quality ID to name mapping for legacy integer-only format
        private static readonly Dictionary<int, string> QualityNames = new Dictionary<int, string>
        {
            { 0, "Unknown" },
            { 1, "PDF" },
            { 2, "MOBI" },
            { 3, "EPUB" },
            { 4, "AZW3" },
            { 10, "MP3-320" },
            { 11, "FLAC" },
            { 12, "MP3-128" },
            { 13, "MP3-VBR" },
            { 14, "AAC" },
            { 15, "OGG" },
            { 16, "OPUS" },
        };

        protected override void MainDbUpgrade()
        {
            // Add new audio quality definitions (AAC=14, OGG=15, OPUS=16) to all existing profiles
            // Also normalizes legacy integer-only quality format to object format
            Execute.WithConnection(AddNewQualitiesToProfiles);
        }

        private void AddNewQualitiesToProfiles(IDbConnection conn, IDbTransaction tran)
        {
            var profiles = conn.Query<ProfileData>("SELECT \"Id\", \"Items\" FROM \"QualityProfiles\"", transaction: tran);

            foreach (var profile in profiles)
            {
                var items = ParseProfileItems(profile.Items);

                // Add new qualities if not already present
                var existingIds = new HashSet<int>();
                CollectIds(items, existingIds);

                if (!existingIds.Contains(14))
                {
                    items.Add(new QualityProfileItem { Quality = new QualityItem { Id = 14, Name = "AAC" }, Allowed = true });
                }

                if (!existingIds.Contains(15))
                {
                    items.Add(new QualityProfileItem { Quality = new QualityItem { Id = 15, Name = "OGG" }, Allowed = true });
                }

                if (!existingIds.Contains(16))
                {
                    items.Add(new QualityProfileItem { Quality = new QualityItem { Id = 16, Name = "OPUS" }, Allowed = true });
                }

                var updatedItems = JsonConvert.SerializeObject(items);
                conn.Execute(
                    "UPDATE \"QualityProfiles\" SET \"Items\" = @Items WHERE \"Id\" = @Id",
                    new { Items = updatedItems, profile.Id },
                    transaction: tran);
            }
        }

        private List<QualityProfileItem> ParseProfileItems(string json)
        {
            var result = new List<QualityProfileItem>();
            var array = JArray.Parse(json);

            foreach (var token in array)
            {
                var item = new QualityProfileItem();
                var qualityToken = token["quality"];

                if (qualityToken != null)
                {
                    if (qualityToken.Type == JTokenType.Integer)
                    {
                        // Legacy format: "quality": 3
                        var id = qualityToken.Value<int>();
                        var name = QualityNames.ContainsKey(id) ? QualityNames[id] : $"Quality {id}";
                        item.Quality = new QualityItem { Id = id, Name = name };
                    }
                    else if (qualityToken.Type == JTokenType.Object)
                    {
                        // New format: "quality": {"id": 3, "name": "EPUB"}
                        item.Quality = new QualityItem
                        {
                            Id = qualityToken["id"]?.Value<int>() ?? 0,
                            Name = qualityToken["name"]?.Value<string>() ?? "Unknown"
                        };
                    }
                }

                item.Allowed = token["allowed"]?.Value<bool>() ?? false;

                var itemsToken = token["items"];
                if (itemsToken != null && itemsToken.Type == JTokenType.Array)
                {
                    item.Items = ParseProfileItems(itemsToken.ToString());
                }
                else
                {
                    item.Items = new List<QualityProfileItem>();
                }

                result.Add(item);
            }

            return result;
        }

        private void CollectIds(List<QualityProfileItem> items, HashSet<int> ids)
        {
            foreach (var item in items)
            {
                if (item.Quality != null)
                {
                    ids.Add(item.Quality.Id);
                }

                if (item.Items != null)
                {
                    CollectIds(item.Items, ids);
                }
            }
        }

        private class ProfileData
        {
            public int Id { get; set; }
            public string Items { get; set; }
        }

        private class QualityProfileItem
        {
            public QualityItem Quality { get; set; }
            public List<QualityProfileItem> Items { get; set; }
            public bool Allowed { get; set; }
        }

        private class QualityItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
