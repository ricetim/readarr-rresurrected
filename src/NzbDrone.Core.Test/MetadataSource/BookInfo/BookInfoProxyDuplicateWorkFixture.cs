using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    // A search can hit two editions of one work: "Project Hail Mary" returns editions
    // 54493401 and 240732259, both of work 79106958. The bulk payload then holds that work twice.
    [TestFixture]
    public class BookInfoProxyDuplicateWorkFixture : CoreTest<BookInfoProxy>
    {
        private const string Author = @"{""ForeignId"": 100, ""Name"": ""Andy Weir"", ""Works"": [], ""Series"": []}";

        [SetUp]
        public void SetUp()
        {
            var factory = new HttpRequestBuilder("http://test/{route}").CreateFactory();
            Mocker.GetMock<IMetadataRequestBuilder>()
                  .Setup(x => x.GetRequestBuilder())
                  .Returns(factory);

            Mocker.GetMock<IAuthorMetadataService>()
                  .Setup(x => x.FindById(It.IsAny<string>()))
                  .Returns((AuthorMetadata)null);
        }

        private void GivenSearchHits(params (int Edition, int Work)[] hits)
        {
            Mocker.GetMock<IGoodreadsSearchProxy>()
                  .Setup(x => x.Search(It.IsAny<string>()))
                  .Returns(hits.Select(h => new SearchJsonResource
                  {
                      BookId = h.Edition,
                      WorkId = h.Work,
                      Author = new AuthorJsonResource { Id = 100 }
                  }).ToList());
        }

        private void GivenBulk(string json)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Post<BulkBookResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse<BulkBookResource>(new HttpResponse(r, new HttpHeader(), json, HttpStatusCode.OK)));
        }

        private static string Edition(int id, int ratings)
        {
            return $@"{{""ForeignId"": {id}, ""Title"": ""Book"", ""RatingCount"": {ratings}, ""AverageRating"": 4.5, ""Contributors"": [{{""ForeignId"": 100, ""Role"": ""Author""}}]}}";
        }

        private static string Work(int id, params string[] editions)
        {
            return $@"{{""ForeignId"": {id}, ""Title"": ""Book {id}"", ""Books"": [{string.Join(",", editions)}], ""Series"": [{{""ForeignId"": 7, ""Title"": ""Series"", ""PositionInSeries"": ""1""}}], ""Authors"": [{Author}]}}";
        }

        [Test]
        public void search_hitting_two_editions_of_one_work_should_return_that_work_once()
        {
            GivenSearchHits((54493401, 79106958), (119255205, 91054370), (240732259, 79106958));
            GivenBulk($@"{{""Works"": [{Work(79106958, Edition(54493401, 900000))}, {Work(91054370, Edition(119255205, 10))}, {Work(79106958, Edition(240732259, 5), Edition(54493401, 900000))}],
                          ""Series"": [{{""ForeignId"": 7, ""Title"": ""Series"", ""LinkItems"": [{{""ForeignWorkId"": 79106958, ""PositionInSeries"": ""1"", ""SeriesPosition"": 1, ""Primary"": true}}]}}],
                          ""Authors"": [{Author}]}}");

            var books = Subject.SearchForNewBook("project hail mary", null, false);

            books.Select(x => x.ForeignBookId).Should().Equal("79106958", "91054370");
            books.First().SeriesLinks.Value.Should().ContainSingle();
        }
    }
}
