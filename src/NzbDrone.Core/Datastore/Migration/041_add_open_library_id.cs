using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(041)]
    public class add_open_library_id : NzbDroneMigrationBase
    {
        protected override void Up()
        {
            // Add OpenLibrary ID tracking to authors and books for cross-reference
            Alter.Table("AuthorMetadata").AddColumn("OpenLibraryId").AsString().Nullable();
            Alter.Table("Books").AddColumn("OpenLibraryWorkId").AsString().Nullable();
            Alter.Table("Editions").AddColumn("OpenLibraryEditionId").AsString().Nullable();

            // Index for lookup by OL ID
            Create.Index("IX_AuthorMetadata_OpenLibraryId")
                .OnTable("AuthorMetadata")
                .OnColumn("OpenLibraryId");

            Create.Index("IX_Books_OpenLibraryWorkId")
                .OnTable("Books")
                .OnColumn("OpenLibraryWorkId");

            Create.Index("IX_Editions_OpenLibraryEditionId")
                .OnTable("Editions")
                .OnColumn("OpenLibraryEditionId");
        }
    }
}
