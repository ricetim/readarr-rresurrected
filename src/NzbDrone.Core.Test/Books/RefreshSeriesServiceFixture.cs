using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Books
{
    // The author payload lists series sA (holding book 1 only). Locally, sA also links
    // book 2, and series sB exists too. Both are missing from the payload, which is what a
    // partial payload looks like before the later pages of works have arrived.
    [TestFixture]
    public class RefreshSeriesServiceFixture : CoreTest<RefreshSeriesService>
    {
        private const int AuthorMetadataId = 7;

        private Book _book1;
        private Book _book2;
        private Series _seriesA;
        private Series _seriesB;
        private SeriesBookLink _linkA1;
        private SeriesBookLink _linkA2;
        private SeriesBookLink _linkB2;
        private Author _remoteData;

        [SetUp]
        public void Setup()
        {
            _book1 = new Book { Id = 1, ForeignBookId = "b1", Title = "Book One" };
            _book2 = new Book { Id = 2, ForeignBookId = "b2", Title = "Book Two" };

            _seriesA = new Series { Id = 10, ForeignSeriesId = "sA", Title = "Series A" };
            _seriesB = new Series { Id = 11, ForeignSeriesId = "sB", Title = "Series B" };

            _linkA1 = new SeriesBookLink { Id = 100, SeriesId = 10, BookId = 1, Book = new LazyLoaded<Book>(_book1) };
            _linkA2 = new SeriesBookLink { Id = 101, SeriesId = 10, BookId = 2, Book = new LazyLoaded<Book>(_book2) };
            _linkB2 = new SeriesBookLink { Id = 102, SeriesId = 11, BookId = 2, Book = new LazyLoaded<Book>(_book2) };

            var remoteSeriesA = new Series
            {
                ForeignSeriesId = "sA",
                Title = "Series A",
                LinkItems = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink { Book = new LazyLoaded<Book>(_book1) }
                })
            };

            _remoteData = new Author
            {
                AuthorMetadataId = AuthorMetadataId,
                Metadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata { ForeignAuthorId = "a1", Name = "Author" }),
                Series = new LazyLoaded<List<Series>>(new List<Series> { remoteSeriesA })
            };

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetByAuthorMetadataId(AuthorMetadataId))
                  .Returns(new List<Series> { _seriesA, _seriesB });

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindById(It.IsAny<List<string>>()))
                  .Returns(new List<Series>());

            Mocker.GetMock<IBookService>()
                  .Setup(s => s.GetBooksByAuthorMetadataId(AuthorMetadataId))
                  .Returns(new List<Book> { _book1, _book2 });

            Mocker.GetMock<ISeriesBookLinkService>()
                  .Setup(s => s.GetLinksBySeriesAndAuthor(10, It.IsAny<string>()))
                  .Returns(() => new List<SeriesBookLink> { _linkA1, _linkA2 });

            Mocker.GetMock<ISeriesBookLinkService>()
                  .Setup(s => s.GetLinksBySeriesAndAuthor(11, It.IsAny<string>()))
                  .Returns(() => new List<SeriesBookLink> { _linkB2 });

            Mocker.GetMock<ISeriesBookLinkService>()
                  .Setup(s => s.GetLinksBySeries(It.IsAny<int>()))
                  .Returns(new List<SeriesBookLink>());
        }

        [TearDown]
        public void TearDown()
        {
            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }

        private void Refresh(bool partial)
        {
            _remoteData.IsPartial = partial;
            Subject.RefreshSeriesInfo(AuthorMetadataId, _remoteData.Series.Value, _remoteData, false, false, null);
        }

        private List<SeriesBookLink> LinksDeletedFromSeriesA()
        {
            var deleted = new List<List<SeriesBookLink>>();

            Mocker.GetMock<IRefreshSeriesBookLinkService>()
                  .Verify(s => s.RefreshSeriesBookLinkInfo(
                      It.IsAny<List<SeriesBookLink>>(),
                      It.IsAny<List<SeriesBookLink>>(),
                      It.IsAny<List<Tuple<SeriesBookLink, SeriesBookLink>>>(),
                      Capture.In(deleted),
                      It.IsAny<List<SeriesBookLink>>(),
                      It.IsAny<List<SeriesBookLink>>(),
                      It.IsAny<bool>()),
                      Times.Once());

            return deleted.Single();
        }

        [Test]
        public void should_not_delete_a_series_missing_from_partial_data()
        {
            Refresh(partial: true);

            Mocker.GetMock<ISeriesService>().Verify(s => s.Delete(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<ISeriesBookLinkService>().Verify(s => s.DeleteMany(It.IsAny<List<SeriesBookLink>>()), Times.Never());
        }

        [Test]
        public void should_not_delete_a_link_missing_from_partial_data()
        {
            Refresh(partial: true);

            LinksDeletedFromSeriesA().Should().BeEmpty();
        }

        [Test]
        public void should_delete_a_series_missing_from_complete_data()
        {
            Refresh(partial: false);

            Mocker.GetMock<ISeriesBookLinkService>().Verify(s => s.DeleteMany(It.Is<List<SeriesBookLink>>(l => l.Single() == _linkB2)), Times.Once());
            Mocker.GetMock<ISeriesService>().Verify(s => s.Delete(11), Times.Once());
        }

        [Test]
        public void should_delete_a_link_missing_from_complete_data()
        {
            Refresh(partial: false);

            LinksDeletedFromSeriesA().Should().ContainSingle().Which.Should().BeSameAs(_linkA2);
        }
    }
}
