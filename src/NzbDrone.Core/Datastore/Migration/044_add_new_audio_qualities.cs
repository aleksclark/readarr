using System.Collections.Generic;
using System.Data;
using Dapper;
using FluentMigrator;
using Newtonsoft.Json;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(044)]
    public class add_new_audio_qualities : NzbDroneMigrationBase
    {
        protected override void Up()
        {
            // Add new audio quality definitions (AAC=14, OGG=15, OPUS=16) to all existing profiles
            Execute.WithConnection(AddNewQualitiesToProfiles);
        }

        private void AddNewQualitiesToProfiles(IDbConnection conn, IDbTransaction tran)
        {
            var profiles = conn.Query<ProfileData>("SELECT \"Id\", \"Items\" FROM \"QualityProfiles\"", transaction: tran);

            foreach (var profile in profiles)
            {
                var items = JsonConvert.DeserializeObject<List<QualityProfileItem>>(profile.Items);

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
                conn.Execute("UPDATE \"QualityProfiles\" SET \"Items\" = @Items WHERE \"Id\" = @Id",
                    new { Items = updatedItems, profile.Id }, transaction: tran);
            }
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
