using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class UpgradeSpecificationFixture : CoreTest<UpgradeSpecification>
    {
        private LocalBook GivenLocalBook(Quality incoming, params Quality[] existing)
        {
            var author = Builder<Author>.CreateNew()
                .With(e => e.QualityProfile = new QualityProfile
                {
                    Items = Core.Test.Qualities.QualityFixture.GetDefaultQualities()
                })
                .Build();

            var files = existing
                .Select((q, i) => new BookFile { Id = i + 1, Quality = new QualityModel(q) })
                .ToList();

            var book = Builder<Book>.CreateNew()
                .With(b => b.BookFiles = new LazyLoaded<List<BookFile>>(files))
                .Build();

            return new LocalBook
            {
                Path = @"C:\Test\book.epub",
                Quality = new QualityModel(incoming),
                Author = author,
                Book = book
            };
        }

        [Test]
        public void should_accept_an_ebook_when_only_an_audiobook_exists()
        {
            // Audio outranks text in the default profile, so comparing across formats
            // rejected the ebook outright instead of letting the two sit side by side.
            var localBook = GivenLocalBook(Quality.EPUB, Quality.MP3, Quality.MP3);

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_an_ebook_format_ranked_below_the_one_on_disk()
        {
            // One ebook at a time, so the ranking decides. PDF sits below EPUB by default.
            var localBook = GivenLocalBook(Quality.PDF, Quality.EPUB);

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_an_ebook_format_ranked_above_the_one_on_disk()
        {
            var localBook = GivenLocalBook(Quality.AZW3, Quality.EPUB);

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_replacement_of_the_same_format()
        {
            var localBook = GivenLocalBook(Quality.EPUB, Quality.EPUB);

            Subject.IsSatisfiedBy(localBook, null).Accepted.Should().BeTrue();
        }

        /*
        private Author _author;
        private Book _book;
        private LocalTrack _localTrack;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                                     .With(e => e.QualityProfile = new QualityProfile
                                     {
                                         Items = Qualities.QualityFixture.GetDefaultQualities(),
                                     }).Build();

            _book = Builder<Book>.CreateNew().Build();

            _localTrack = new LocalTrack
            {
                Path = @"C:\Test\Imagine Dragons\Imagine.Dragons.Song.1.mp3",
                Quality = new QualityModel(Quality.MP3, new Revision(version: 1)),
                Author = _author,
                Book = _book
            };
        }

        [Test]
        public void should_return_true_if_no_existing_trackFile()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 0)
                                                     .With(e => e.TrackFile = null)
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_no_existing_trackFile_for_multi_tracks()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .All()
                                                     .With(e => e.TrackFileId = 0)
                                                     .With(e => e.TrackFile = null)
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_upgrade_for_existing_trackFile()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.MP3, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_upgrade_for_existing_trackFile_for_multi_tracks()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.MP3, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_not_an_upgrade_for_existing_trackFile()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.FLAC, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_not_an_upgrade_for_existing_trackFile_for_multi_tracks()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.FLAC, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_not_an_upgrade_for_one_existing_trackFile_for_multi_track()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .TheFirst(1)
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.MP3, new Revision(version: 1))
                                                                                }))
                                                     .TheNext(1)
                                                     .With(e => e.TrackFileId = 2)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                                                new TrackFile
                                                                                {
                                                                                    Quality = new QualityModel(Quality.FLAC, new Revision(version: 1))
                                                                                }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_not_a_revision_upgrade_and_prefers_propers()
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DownloadPropersAndRepacks)
                  .Returns(ProperDownloadTypes.PreferAndUpgrade);

            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                         new TrackFile
                                                         {
                                                             Quality = new QualityModel(Quality.MP3, new Revision(version: 2))
                                                         }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_not_a_revision_upgrade_and_does_not_prefer_propers()
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DownloadPropersAndRepacks)
                  .Returns(ProperDownloadTypes.DoNotPrefer);

            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                         new TrackFile
                                                         {
                                                             Quality = new QualityModel(Quality.MP3, new Revision(version: 2))
                                                         }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_when_comparing_to_a_lower_quality_proper()
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DownloadPropersAndRepacks)
                  .Returns(ProperDownloadTypes.DoNotPrefer);

            _localTrack.Quality = new QualityModel(Quality.FLAC);

            _localTrack.Tracks = Builder<Track>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(
                                                         new TrackFile
                                                         {
                                                             Quality = new QualityModel(Quality.FLAC, new Revision(version: 2))
                                                         }))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_track_file_is_null()
        {
            _localTrack.Tracks = Builder<Track>.CreateListOfSize(2)
                                                     .All()
                                                     .With(e => e.TrackFileId = 1)
                                                     .With(e => e.TrackFile = new LazyLoaded<TrackFile>(null))
                                                     .Build()
                                                     .ToList();

            Subject.IsSatisfiedBy(_localTrack, null).Accepted.Should().BeTrue();
        }
        */
    }
}
