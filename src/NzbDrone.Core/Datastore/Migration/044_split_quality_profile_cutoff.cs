using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Dapper;
using FluentMigrator;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    /// <summary>
    /// Ebooks and audiobooks are cut off independently, so the single Cutoff column becomes
    /// one per media type. The existing value keeps its meaning for whichever type it named,
    /// and the other type takes the best format the profile already allows.
    /// </summary>
    [Migration(044)]
    public class split_quality_profile_cutoff : NzbDroneMigrationBase
    {
        private const int EpubQualityId = 3;
        private const int Mp3QualityId = 10;

        private static readonly HashSet<int> AudiobookQualityIds = new HashSet<int> { 10, 11, 12, 13 };

        protected override void MainDbUpgrade()
        {
            Alter.Table("QualityProfiles").AddColumn("EbookCutoff").AsInt32().WithDefaultValue(EpubQualityId);
            Alter.Table("QualityProfiles").AddColumn("AudiobookCutoff").AsInt32().WithDefaultValue(Mp3QualityId);

            Execute.WithConnection(SplitCutoffs);

            Delete.Column("Cutoff").FromTable("QualityProfiles");
        }

        private void SplitCutoffs(IDbConnection conn, IDbTransaction tran)
        {
            var profiles = conn.Query<Profile44>("SELECT \"Id\", \"Cutoff\", \"Items\" FROM \"QualityProfiles\"", transaction: tran).ToList();

            foreach (var profile in profiles)
            {
                var items = ParseItems(profile.Items);

                // Whichever type the old cutoff named keeps it; the other falls back to the best
                // format already allowed, and to a sane default when the type is fully disallowed.
                var cutoffIsAudiobook = MediaTypeOfCutoff(items, profile.Cutoff);

                var ebookCutoff = cutoffIsAudiobook
                    ? BestAllowed(items, false) ?? EpubQualityId
                    : profile.Cutoff;

                var audiobookCutoff = cutoffIsAudiobook
                    ? profile.Cutoff
                    : BestAllowed(items, true) ?? Mp3QualityId;

                conn.Execute(
                    "UPDATE \"QualityProfiles\" SET \"EbookCutoff\" = @Ebook, \"AudiobookCutoff\" = @Audiobook WHERE \"Id\" = @Id",
                    new { Ebook = ebookCutoff, Audiobook = audiobookCutoff, profile.Id },
                    transaction: tran);
            }
        }

        private static List<Item44> ParseItems(string json)
        {
            if (json.IsNullOrWhiteSpace())
            {
                return new List<Item44>();
            }

            return JsonSerializer.Deserialize<List<Item44>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            }) ?? new List<Item44>();
        }

        private static bool IsAudiobook(Item44 item)
        {
            if (item.Quality.HasValue)
            {
                return AudiobookQualityIds.Contains(item.Quality.Value);
            }

            var first = item.Items?.FirstOrDefault(i => i.Quality.HasValue);

            return first != null && AudiobookQualityIds.Contains(first.Quality.Value);
        }

        /// <summary>
        /// The cutoff can name a quality or a group, so both are checked. An unresolvable
        /// cutoff is treated as an ebook one, matching the column default.
        /// </summary>
        private static bool MediaTypeOfCutoff(List<Item44> items, int cutoff)
        {
            foreach (var item in items)
            {
                if (item.Quality == cutoff || (item.Id > 0 && item.Id == cutoff))
                {
                    return IsAudiobook(item);
                }

                if (item.Items != null && item.Items.Any(i => i.Quality == cutoff))
                {
                    return IsAudiobook(item);
                }
            }

            return AudiobookQualityIds.Contains(cutoff);
        }

        private static int? BestAllowed(List<Item44> items, bool audiobook)
        {
            // Later in the list is better, so the last allowed entry is the best one.
            var best = items.LastOrDefault(i => i.Allowed && IsAudiobook(i) == audiobook);

            if (best == null)
            {
                return null;
            }

            return best.Quality ?? (best.Id > 0 ? best.Id : (int?)null);
        }

        private class Profile44
        {
            public int Id { get; set; }
            public int Cutoff { get; set; }
            public string Items { get; set; }
        }

        private class Item44
        {
            public int Id { get; set; }
            public int? Quality { get; set; }
            public bool Allowed { get; set; }
            public List<Item44> Items { get; set; }
        }
    }
}
