using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(043)]
    public class add_audiobook_edition_fields : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // EditionFormat: 0=Unknown, 1=Text, 2=Audio
            Alter.Table("Editions").AddColumn("EditionFormat").AsInt32().WithDefaultValue(0);
            Alter.Table("Editions").AddColumn("DurationMinutes").AsInt32().Nullable();
            Alter.Table("Editions").AddColumn("Narrator").AsString().Nullable();
            Alter.Table("Editions").AddColumn("IsAbridged").AsBoolean().WithDefaultValue(false);

            // Backfill: classify existing editions based on file extensions
            // If an edition has only audio book files, mark it as Audio
            // Editions with text files get marked as Text
            // This is a best-effort migration — user can reclassify later
            Execute.Sql(@"
                UPDATE ""Editions"" SET ""EditionFormat"" = 1
                WHERE ""IsEbook"" = true OR ""Format"" LIKE '%book%' OR ""Format"" LIKE '%paper%'
                    OR ""Format"" LIKE '%hard%' OR ""Format"" = '';

                UPDATE ""Editions"" SET ""EditionFormat"" = 2
                WHERE ""Format"" LIKE '%audio%' OR ""Format"" LIKE '%cd%' OR ""Format"" LIKE '%cassette%';
            ");

            // Index for quick format-based queries
            Create.Index("IX_Editions_EditionFormat")
                .OnTable("Editions")
                .OnColumn("EditionFormat");
        }
    }
}
