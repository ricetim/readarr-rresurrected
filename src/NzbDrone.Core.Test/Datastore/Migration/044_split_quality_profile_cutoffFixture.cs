using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class split_quality_profile_cutoffFixture : MigrationTest<split_quality_profile_cutoff>
    {
        private const string AudiobookItems = @"[
            { ""quality"": 1, ""allowed"": false },
            { ""quality"": 3, ""allowed"": false },
            { ""quality"": 10, ""allowed"": true },
            { ""quality"": 12, ""allowed"": true },
            { ""quality"": 11, ""allowed"": false }
        ]";

        private const string MixedItems = @"[
            { ""quality"": 1, ""allowed"": true },
            { ""quality"": 3, ""allowed"": true },
            { ""quality"": 4, ""allowed"": false },
            { ""quality"": 10, ""allowed"": true },
            { ""quality"": 11, ""allowed"": true }
        ]";

        private Profile44Row Migrate(int cutoff, string items)
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Test",
                    Cutoff = cutoff,
                    Items = items,
                    MinFormatScore = 0,
                    CutoffFormatScore = 0,
                    UpgradeAllowed = true,
                    FormatItems = "[]"
                });
            });

            return db.Query<Profile44Row>("SELECT \"EbookCutoff\", \"AudiobookCutoff\" FROM \"QualityProfiles\"").First();
        }

        [Test]
        public void should_keep_an_audiobook_cutoff_on_the_audiobook_side()
        {
            var row = Migrate(10, AudiobookItems);

            row.AudiobookCutoff.Should().Be(10);
        }

        [Test]
        public void should_fall_back_to_a_default_when_no_ebook_format_is_allowed()
        {
            // An audiobook-only profile has nothing to promote, so the unused cutoff still has
            // to name a real quality rather than land on zero.
            var row = Migrate(10, AudiobookItems);

            row.EbookCutoff.Should().Be(3);
        }

        [Test]
        public void should_take_the_best_allowed_format_for_the_other_media_type()
        {
            // Cutoff 3 is EPUB, so the audiobook side takes the best audiobook format allowed,
            // which is FLAC at the end of the list.
            var row = Migrate(3, MixedItems);

            row.EbookCutoff.Should().Be(3);
            row.AudiobookCutoff.Should().Be(11);
        }

        [Test]
        public void should_drop_the_old_cutoff_column()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("QualityProfiles").Row(new
                {
                    Name = "Test",
                    Cutoff = 10,
                    Items = AudiobookItems,
                    MinFormatScore = 0,
                    CutoffFormatScore = 0,
                    UpgradeAllowed = true,
                    FormatItems = "[]"
                });
            });

            var columns = db.Query<ColumnRow>("SELECT \"name\" AS \"Name\" FROM pragma_table_info('QualityProfiles')")
                .Select(c => c.Name)
                .ToList();

            columns.Should().NotContain("Cutoff");
            columns.Should().Contain("EbookCutoff");
            columns.Should().Contain("AudiobookCutoff");
        }

        private class ColumnRow
        {
            public string Name { get; set; }
        }

        private class Profile44Row
        {
            public int EbookCutoff { get; set; }
            public int AudiobookCutoff { get; set; }
        }
    }
}
