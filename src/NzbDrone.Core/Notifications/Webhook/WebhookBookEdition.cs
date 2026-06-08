using NzbDrone.Core.Books;

namespace NzbDrone.Core.Notifications.Webhook
{
    public class WebhookBookEdition
    {
        public WebhookBookEdition(Edition edition)
        {
            MetadataId = edition.ForeignEditionId;
            Title = edition.Title;
            Asin = edition.Asin;
            Isbn13 = edition.Isbn13;
        }

        public string Title { get; set; }
        public string MetadataId { get; set; }
        public string Asin { get; set; }
        public string Isbn13 { get; set; }
    }
}
