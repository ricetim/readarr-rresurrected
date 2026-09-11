using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    [TestFixture]
    public class QualityProfileMediaTypeFixture : CoreTest
    {
        private QualityProfile GivenProfile(params Quality[] allowed)
        {
            var order = new[]
            {
                Quality.PDF, Quality.MOBI, Quality.EPUB, Quality.AZW3,
                Quality.MP3, Quality.M4B, Quality.FLAC
            };

            return new QualityProfile
            {
                UpgradeAllowed = true,
                EbookCutoff = Quality.EPUB.Id,
                AudiobookCutoff = Quality.MP3.Id,
                Items = order
                    .Select(q => new QualityProfileQualityItem { Quality = q, Allowed = allowed.Contains(q) })
                    .ToList()
            };
        }

        [Test]
        public void quality_knows_its_media_type()
        {
            Quality.EPUB.MediaType.Should().Be(QualityMediaType.Ebook);
            Quality.PDF.MediaType.Should().Be(QualityMediaType.Ebook);
            Quality.Unknown.MediaType.Should().Be(QualityMediaType.Ebook);
            Quality.MP3.MediaType.Should().Be(QualityMediaType.Audiobook);
            Quality.FLAC.MediaType.Should().Be(QualityMediaType.Audiobook);
            Quality.UnknownAudio.MediaType.Should().Be(QualityMediaType.Audiobook);
        }

        [Test]
        public void should_return_the_cutoff_for_each_media_type()
        {
            var profile = GivenProfile(Quality.EPUB, Quality.MP3);

            profile.GetCutoff(QualityMediaType.Ebook).Should().Be(Quality.EPUB.Id);
            profile.GetCutoff(QualityMediaType.Audiobook).Should().Be(Quality.MP3.Id);
        }

        [Test]
        public void should_fall_back_to_the_first_allowed_format_when_upgrades_are_off()
        {
            var profile = GivenProfile(Quality.MOBI, Quality.EPUB, Quality.M4B, Quality.FLAC);
            profile.UpgradeAllowed = false;

            profile.EffectiveCutoff(QualityMediaType.Ebook).Should().Be(Quality.MOBI.Id);
            profile.EffectiveCutoff(QualityMediaType.Audiobook).Should().Be(Quality.M4B.Id);
        }

        [Test]
        public void should_report_no_cutoff_for_a_media_type_that_allows_nothing()
        {
            // An audiobook-only profile: nothing on the ebook side is wanted at all.
            var profile = GivenProfile(Quality.MP3, Quality.M4B);
            profile.UpgradeAllowed = false;

            profile.AllowsAnything(QualityMediaType.Ebook).Should().BeFalse();
            profile.FirstAllowedQuality(QualityMediaType.Ebook).Should().BeNull();
            profile.EffectiveCutoff(QualityMediaType.Ebook).Should().BeNull();
        }

        [Test]
        public void should_rank_within_a_media_type_only()
        {
            var profile = GivenProfile(Quality.EPUB, Quality.AZW3, Quality.MP3, Quality.FLAC);

            profile.ItemsFor(QualityMediaType.Ebook)
                .Select(i => i.Quality)
                .Should().NotContain(Quality.MP3);

            profile.LastAllowedQuality(QualityMediaType.Ebook).Should().Be(Quality.AZW3);
            profile.LastAllowedQuality(QualityMediaType.Audiobook).Should().Be(Quality.FLAC);
        }
    }
}
