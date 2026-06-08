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
        protected override void MainDbUpgrade()
        {
            // Add new audio quality definitions (AAC=14, OGG=15, OPUS=16) to all existing profiles.
            // The DB stores quality profile items as JSON with quality as a plain integer:
            //   [{"quality": 3, "items": [], "allowed": true}, ...]
            Execute.WithConnection(AddNewQualitiesToProfiles);
        }

        private void AddNewQualitiesToProfiles(IDbConnection conn, IDbTransaction tran)
        {
            var profiles = conn.Query<ProfileData>("SELECT \"Id\", \"Items\" FROM \"QualityProfiles\"", transaction: tran);

            foreach (var profile in profiles)
            {
                var array = JArray.Parse(profile.Items);

                // Collect existing quality IDs
                var existingIds = new HashSet<int>();
                foreach (var token in array)
                {
                    var qualityToken = token["quality"];
                    if (qualityToken != null && qualityToken.Type == JTokenType.Integer)
                    {
                        existingIds.Add(qualityToken.Value<int>());
                    }
                }

                var modified = false;

                if (!existingIds.Contains(14))
                {
                    array.Add(JObject.Parse("{\"quality\": 14, \"items\": [], \"allowed\": true}"));
                    modified = true;
                }

                if (!existingIds.Contains(15))
                {
                    array.Add(JObject.Parse("{\"quality\": 15, \"items\": [], \"allowed\": true}"));
                    modified = true;
                }

                if (!existingIds.Contains(16))
                {
                    array.Add(JObject.Parse("{\"quality\": 16, \"items\": [], \"allowed\": true}"));
                    modified = true;
                }

                if (modified)
                {
                    var updatedItems = array.ToString(Formatting.None);
                    conn.Execute(
                        "UPDATE \"QualityProfiles\" SET \"Items\" = @Items WHERE \"Id\" = @Id",
                        new { Items = updatedItems, profile.Id },
                        transaction: tran);
                }
            }
        }

        private class ProfileData
        {
            public int Id { get; set; }
            public string Items { get; set; }
        }
    }
}
