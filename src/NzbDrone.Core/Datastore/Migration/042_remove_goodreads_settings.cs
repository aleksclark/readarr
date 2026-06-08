using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(042)]
    public class remove_goodreads_settings : NzbDroneMigrationBase
    {
        protected override void Up()
        {
            // Remove Goodreads OAuth tokens and API keys from config
            Execute.Sql("DELETE FROM \"Config\" WHERE \"Key\" LIKE '%goodreads%'");

            // Remove Goodreads import lists
            Execute.Sql("DELETE FROM \"ImportLists\" WHERE \"Implementation\" LIKE '%Goodreads%'");

            // Remove Goodreads notifications
            Execute.Sql("DELETE FROM \"Notifications\" WHERE \"Implementation\" LIKE '%Goodreads%'");
        }
    }
}
