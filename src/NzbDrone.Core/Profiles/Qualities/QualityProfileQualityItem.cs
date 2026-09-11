using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Profiles.Qualities
{
    public class QualityProfileQualityItem : IEmbeddedDocument
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Id { get; set; }

        public string Name { get; set; }
        public Quality Quality { get; set; }
        public List<QualityProfileQualityItem> Items { get; set; }
        public bool Allowed { get; set; }

        public QualityProfileQualityItem()
        {
            Items = new List<QualityProfileQualityItem>();
        }

        /// <summary>
        /// A group takes the media type of the qualities inside it. Groups are only ever
        /// built from one type, since the editor keeps the two rankings separate.
        /// </summary>
        [JsonIgnore]
        public QualityMediaType MediaType =>
            (Quality ?? Items.FirstOrDefault()?.Quality ?? Core.Qualities.Quality.Unknown).MediaType;

        public List<Quality> GetQualities()
        {
            if (Quality == null)
            {
                return Items.Select(s => s.Quality).ToList();
            }

            return new List<Quality> { Quality };
        }

        public override string ToString()
        {
            var qualitiesString = string.Join(", ", GetQualities());

            if (Name.IsNotNullOrWhiteSpace())
            {
                return $"{Name} ({qualitiesString})";
            }

            return qualitiesString;
        }
    }
}
