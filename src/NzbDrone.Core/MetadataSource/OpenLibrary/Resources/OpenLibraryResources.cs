using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    public class OpenLibrarySearchResponse
    {
        [JsonPropertyName("numFound")]
        public int NumFound { get; set; }

        [JsonPropertyName("start")]
        public int Start { get; set; }

        [JsonPropertyName("docs")]
        public List<OpenLibrarySearchDoc> Docs { get; set; } = new ();
    }

    public class OpenLibrarySearchDoc
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("author_name")]
        public List<string> AuthorNames { get; set; }

        [JsonPropertyName("author_key")]
        public List<string> AuthorKeys { get; set; }

        [JsonPropertyName("first_publish_year")]
        public int? FirstPublishYear { get; set; }

        [JsonPropertyName("edition_count")]
        public int? EditionCount { get; set; }

        [JsonPropertyName("isbn")]
        public List<string> Isbns { get; set; }

        [JsonPropertyName("cover_i")]
        public int? CoverId { get; set; }

        [JsonPropertyName("subject")]
        public List<string> Subjects { get; set; }

        [JsonPropertyName("language")]
        public List<string> Languages { get; set; }

        [JsonPropertyName("publisher")]
        public List<string> Publishers { get; set; }

        [JsonPropertyName("number_of_pages_median")]
        public int? NumberOfPagesMedian { get; set; }

        [JsonPropertyName("ratings_average")]
        public double? RatingsAverage { get; set; }

        [JsonPropertyName("ratings_count")]
        public int? RatingsCount { get; set; }

        [JsonPropertyName("edition_key")]
        public List<string> EditionKeys { get; set; }

        /// <summary>
        /// Extracts the Work OLID from the key (e.g., "/works/OL45804W" → "OL45804W")
        /// </summary>
        public string WorkId => Key?.Replace("/works/", "") ?? string.Empty;
    }

    public class OpenLibraryWorkResource
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("description")]
        public object Description { get; set; }

        [JsonPropertyName("authors")]
        public List<OpenLibraryAuthorRole> Authors { get; set; }

        [JsonPropertyName("covers")]
        public List<int> Covers { get; set; }

        [JsonPropertyName("subjects")]
        public List<string> Subjects { get; set; }

        [JsonPropertyName("subject_places")]
        public List<string> SubjectPlaces { get; set; }

        [JsonPropertyName("subject_times")]
        public List<string> SubjectTimes { get; set; }

        [JsonPropertyName("subject_people")]
        public List<string> SubjectPeople { get; set; }

        [JsonPropertyName("first_publish_date")]
        public string FirstPublishDate { get; set; }

        [JsonPropertyName("links")]
        public List<OpenLibraryLink> Links { get; set; }

        [JsonPropertyName("created")]
        public OpenLibraryDateValue Created { get; set; }

        [JsonPropertyName("last_modified")]
        public OpenLibraryDateValue LastModified { get; set; }

        public string WorkId => Key?.Replace("/works/", "") ?? string.Empty;

        /// <summary>
        /// OL description can be a plain string OR an object with { "type": "/type/text", "value": "..." }
        /// </summary>
        public string GetDescription()
        {
            if (Description == null)
            {
                return string.Empty;
            }

            if (Description is string s)
            {
                return s;
            }

            // Handle the { "value": "..." } case via System.Text.Json
            try
            {
                var element = (System.Text.Json.JsonElement)Description;
                if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return element.GetString() ?? string.Empty;
                }

                if (element.TryGetProperty("value", out var valueProp))
                {
                    return valueProp.GetString() ?? string.Empty;
                }
            }
            catch
            {
                return Description.ToString() ?? string.Empty;
            }

            return string.Empty;
        }
    }

    public class OpenLibraryAuthorRole
    {
        [JsonPropertyName("author")]
        [JsonConverter(typeof(OpenLibraryReferenceConverter))]
        public OpenLibraryReference Author { get; set; }

        [JsonPropertyName("type")]
        [JsonConverter(typeof(OpenLibraryReferenceConverter))]
        public OpenLibraryReference Type { get; set; }
    }

    public class OpenLibraryReference
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }
    }

    /// <summary>
    /// Handles OL's inconsistent serialization where a reference can be either
    /// a string ("/type/author_role") or an object ({"key": "/type/author_role"})
    /// </summary>
    public class OpenLibraryReferenceConverter : System.Text.Json.Serialization.JsonConverter<OpenLibraryReference>
    {
        public override OpenLibraryReference Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.String)
            {
                return new OpenLibraryReference { Key = reader.GetString() };
            }

            if (reader.TokenType == System.Text.Json.JsonTokenType.StartObject)
            {
                var element = System.Text.Json.JsonElement.ParseValue(ref reader);
                if (element.TryGetProperty("key", out var keyProp))
                {
                    return new OpenLibraryReference { Key = keyProp.GetString() };
                }

                return new OpenLibraryReference();
            }

            // Skip unexpected token types
            reader.Skip();
            return null;
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, OpenLibraryReference value, System.Text.Json.JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("key", value.Key);
            writer.WriteEndObject();
        }
    }

    public class OpenLibraryLink
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }
    }

    public class OpenLibraryDateValue
    {
        [JsonPropertyName("value")]
        public string Value { get; set; }
    }

    public class OpenLibraryEditionResource
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("publishers")]
        public List<string> Publishers { get; set; }

        [JsonPropertyName("publish_date")]
        public string PublishDate { get; set; }

        [JsonPropertyName("number_of_pages")]
        public int? NumberOfPages { get; set; }

        [JsonPropertyName("isbn_13")]
        public List<string> Isbn13 { get; set; }

        [JsonPropertyName("isbn_10")]
        public List<string> Isbn10 { get; set; }

        [JsonPropertyName("covers")]
        public List<int> Covers { get; set; }

        [JsonPropertyName("languages")]
        public List<OpenLibraryReference> Languages { get; set; }

        [JsonPropertyName("physical_format")]
        public string PhysicalFormat { get; set; }

        [JsonPropertyName("works")]
        public List<OpenLibraryReference> Works { get; set; }

        [JsonPropertyName("identifiers")]
        public Dictionary<string, List<string>> Identifiers { get; set; }

        [JsonPropertyName("classifications")]
        public Dictionary<string, List<string>> Classifications { get; set; }

        [JsonPropertyName("links")]
        public List<OpenLibraryLink> Links { get; set; }

        [JsonPropertyName("authors")]
        public List<OpenLibraryReference> Authors { get; set; }

        [JsonPropertyName("description")]
        public object Description { get; set; }

        [JsonPropertyName("full_title")]
        public string FullTitle { get; set; }

        [JsonPropertyName("subtitle")]
        public string Subtitle { get; set; }

        /// <summary>
        /// Extracts the Edition OLID from the key (e.g., "/books/OL7353617M" → "OL7353617M")
        /// </summary>
        public string EditionId => Key?.Replace("/books/", "") ?? string.Empty;

        public string GetIsbn13()
        {
            if (Isbn13 != null && Isbn13.Count > 0)
            {
                return Isbn13[0];
            }

            return null;
        }

        public string GetIsbn10()
        {
            if (Isbn10 != null && Isbn10.Count > 0)
            {
                return Isbn10[0];
            }

            return null;
        }

        public string GetLanguageCode()
        {
            if (Languages == null || Languages.Count == 0)
            {
                return null;
            }

            // Key is like "/languages/eng"
            return Languages[0].Key?.Replace("/languages/", "");
        }

        public bool IsEbook()
        {
            if (string.IsNullOrWhiteSpace(PhysicalFormat))
            {
                return false;
            }

            var format = PhysicalFormat.ToLowerInvariant();
            return format.Contains("ebook") || format.Contains("electronic") || format.Contains("kindle");
        }

        public bool IsAudiobook()
        {
            if (string.IsNullOrWhiteSpace(PhysicalFormat))
            {
                return false;
            }

            var format = PhysicalFormat.ToLowerInvariant();
            return format.Contains("audio") || format.Contains("cd") || format.Contains("cassette");
        }
    }

    public class OpenLibraryAuthorResource
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("personal_name")]
        public string PersonalName { get; set; }

        [JsonPropertyName("bio")]
        public object Bio { get; set; }

        [JsonPropertyName("birth_date")]
        public string BirthDate { get; set; }

        [JsonPropertyName("death_date")]
        public string DeathDate { get; set; }

        [JsonPropertyName("alternate_names")]
        public List<string> AlternateNames { get; set; }

        [JsonPropertyName("photos")]
        public List<int> Photos { get; set; }

        [JsonPropertyName("links")]
        public List<OpenLibraryLink> Links { get; set; }

        [JsonPropertyName("wikipedia")]
        public string Wikipedia { get; set; }

        [JsonPropertyName("created")]
        public OpenLibraryDateValue Created { get; set; }

        [JsonPropertyName("last_modified")]
        public OpenLibraryDateValue LastModified { get; set; }

        public string AuthorId => Key?.Replace("/authors/", "") ?? string.Empty;

        public string GetBio()
        {
            if (Bio == null)
            {
                return string.Empty;
            }

            if (Bio is string s)
            {
                return s;
            }

            try
            {
                var element = (System.Text.Json.JsonElement)Bio;
                if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return element.GetString() ?? string.Empty;
                }

                if (element.TryGetProperty("value", out var valueProp))
                {
                    return valueProp.GetString() ?? string.Empty;
                }
            }
            catch
            {
                return Bio.ToString() ?? string.Empty;
            }

            return string.Empty;
        }
    }

    public class OpenLibraryAuthorWorksResponse
    {
        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("entries")]
        public List<OpenLibraryWorkResource> Entries { get; set; } = new ();
    }

    public class OpenLibraryEditionsResponse
    {
        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("entries")]
        public List<OpenLibraryEditionResource> Entries { get; set; } = new ();
    }
}
