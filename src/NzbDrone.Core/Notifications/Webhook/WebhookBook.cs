using System;
using System.Linq;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Notifications.Webhook
{
    public class WebhookBook
    {
        public WebhookBook()
        {
        }

        public WebhookBook(Book book)
        {
            Id = book.Id;
            MetadataId = book.ForeignBookId;
            Title = book.Title;
            ReleaseDate = book.ReleaseDate;
            Edition = new WebhookBookEdition(book.Editions.Value.SingleOrDefault(e => e.Monitored) ?? book.Editions.Value.First());
        }

        public int Id { get; set; }
        public string MetadataId { get; set; }
        public string Title { get; set; }
        public WebhookBookEdition Edition { get; set; }
        public DateTime? ReleaseDate { get; set; }
    }
}
