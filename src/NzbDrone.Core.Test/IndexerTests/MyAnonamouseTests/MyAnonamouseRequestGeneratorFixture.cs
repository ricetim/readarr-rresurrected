using System;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.MyAnonamouse;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonamouseTests
{
    [TestFixture]
    public class MyAnonamouseRequestGeneratorFixture : CoreTest<MyAnonamouseRequestGenerator>
    {
        private BookSearchCriteria _bookSearchCriteria;
        private AuthorSearchCriteria _authorSearchCriteria;

        [SetUp]
        public void SetUp()
        {
            Subject.Settings = new MyAnonamouseSettings
            {
                Cookie = "test_cookie",
                SearchType = (int)MyAnonamouseSearchType.All
            };

            _bookSearchCriteria = new BookSearchCriteria
            {
                Author = new Author { Name = "Patrick Rothfuss" },
                BookTitle = "The Name of the Wind",
                BookIsbn = "9780756404079"
            };

            _authorSearchCriteria = new AuthorSearchCriteria
            {
                Author = new Author { Name = "Brandon Sanderson" },
                Books = new System.Collections.Generic.List<Book>()
            };
        }

        private JObject GetRequestBody(NzbDrone.Common.Http.HttpRequest request)
        {
            var body = System.Text.Encoding.UTF8.GetString(request.ContentData);
            return JObject.Parse(body);
        }

        [Test]
        public void recent_request_should_have_correct_categories()
        {
            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            var mainCat = body["tor"]["main_cat"].ToObject<int[]>();
            mainCat.Should().BeEquivalentTo(new[] { 13, 14 });
        }

        [Test]
        public void recent_request_should_sort_by_date_descending()
        {
            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["sortType"].Value<string>().Should().Be("dateDesc");
        }

        [Test]
        public void recent_request_should_not_include_start_date_when_last_sync_is_null()
        {
            Subject.LastRssSyncDate = null;

            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["startDate"].Should().BeNull();
        }

        [Test]
        public void recent_request_should_include_start_date_when_last_sync_is_set()
        {
            var syncDate = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
            Subject.LastRssSyncDate = syncDate;

            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["startDate"].Value<string>().Should().Be("2024-06-15 10:30:00");
        }

        [Test]
        public void recent_request_should_set_cookie()
        {
            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;

            request.Cookies["mam_id"].Should().Be("test_cookie");
        }

        [Test]
        public void book_search_should_include_text_and_search_in_title_and_author()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            var srchIn = body["tor"]["srchIn"].ToObject<string[]>();
            srchIn.Should().Contain("title");
            srchIn.Should().Contain("author");
            body["tor"]["text"].Should().NotBeNull();
        }

        [Test]
        public void book_search_text_should_include_author_name_to_narrow_results()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            var text = body["tor"]["text"].Value<string>();
            text.Should().Contain("Patrick Rothfuss");
            text.Should().Contain("Name of the Wind");
        }

        private string SearchTextFor(string author, string bookTitle)
        {
            _bookSearchCriteria.Author = new Author { Name = author };
            _bookSearchCriteria.BookTitle = bookTitle;
            _bookSearchCriteria.BookIsbn = null;

            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;

            return GetRequestBody(request)["tor"]["text"].Value<string>();
        }

        [TestCase("Whose Body?", "Dorothy Sayers Whose Body")]
        [TestCase("Hard-Boiled Wonderland", "Haruki Murakami Hard Boiled Wonderland")]
        [TestCase("Who Goes There!", "Dorothy Sayers Who Goes There")]
        [TestCase("Crime & Punishment", "Dorothy Sayers Crime Punishment")]
        public void book_search_text_should_drop_characters_mam_reads_as_query_operators(string title, string expected)
        {
            // MAM parses the text as a boolean query, so title punctuation is read as an
            // operator: '!' errors outright, '?' and '-' silently match nothing. Verified
            // against the live search, which returned no results for "Whose Body?" despite
            // holding a release titled exactly that.
            var author = title.StartsWith("Hard-Boiled") ? "Haruki Murakami" : "Dorothy Sayers";

            SearchTextFor(author, title).Should().Be(expected);
        }

        [Test]
        public void book_search_text_should_drop_the_subtitle_before_searching()
        {
            // SplitBookTitle removes anything after a colon, since release names rarely carry
            // the subtitle. Colons themselves are safe for MAM's parser; this is a separate
            // narrowing step that happens first.
            SearchTextFor("Dorothy Sayers", "Gaudy Night: A Mystery")
                .Should().Be("Dorothy Sayers Gaudy Night");
        }

        [Test]
        public void book_search_text_should_keep_apostrophes()
        {
            SearchTextFor("Dorothy Sayers", "Busman's Honeymoon")
                .Should().Be("Dorothy Sayers Busman's Honeymoon");
        }

        [Test]
        public void book_search_text_should_space_out_initials()
        {
            // MAM stores initialed names with spaces, so "C.J." must not stay glued together.
            SearchTextFor("C.J. Cherryh", "Downbelow Station")
                .Should().Be("C J Cherryh Downbelow Station");
        }

        [Test]
        public void author_search_text_should_drop_query_operators()
        {
            _authorSearchCriteria.Author = new Author { Name = "Jean-Paul Sartre" };

            var results = Subject.GetSearchRequests(_authorSearchCriteria);
            var body = GetRequestBody(results.GetAllTiers().First().First().HttpRequest);

            body["tor"]["text"].Value<string>().Should().Be("Jean Paul Sartre");
        }

        [Test]
        public void book_search_with_isbn_should_produce_two_requests()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var tier = results.GetTier(0).ToList();
            var allRequests = tier.SelectMany(r => r).ToList();

            allRequests.Should().HaveCount(2);
        }

        [Test]
        public void book_search_isbn_request_should_include_isbn()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var tier = results.GetTier(0).ToList();
            var requests = tier.SelectMany(r => r).ToList();

            var isbnRequest = requests[1].HttpRequest;
            var body = GetRequestBody(isbnRequest);

            body["isbn"].Value<string>().Should().Be("9780756404079");
        }

        [Test]
        public void book_search_without_isbn_should_produce_one_request()
        {
            _bookSearchCriteria.BookIsbn = null;

            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var allRequests = results.GetAllTiers().SelectMany<IndexerPageableRequest, IndexerRequest>(r => r).ToList();

            allRequests.Should().HaveCount(1);
        }

        [Test]
        public void author_search_should_restrict_to_author_field_only()
        {
            var results = Subject.GetSearchRequests(_authorSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            var srchIn = body["tor"]["srchIn"].ToObject<string[]>();
            srchIn.Should().BeEquivalentTo(new[] { "author" });
        }

        // MAM stores initialed names with spaces ("C J Cherryh", not "C.J. Cherryh"). Periods in the
        // outgoing search text never match, so book and author searches for these authors return zero
        // — even though the books exist on the tracker. Normalize "." to " " in the author name.
        [TestCase("C.J. Cherryh", "C J Cherryh")]
        [TestCase("J.K. Rowling", "J K Rowling")]
        [TestCase("J.R.R. Tolkien", "J R R Tolkien")]
        [TestCase("H.P. Lovecraft", "H P Lovecraft")]
        public void book_search_should_normalize_periods_in_author_name(string authorName, string expectedAuthorPart)
        {
            _bookSearchCriteria.Author = new Author { Name = authorName };
            _bookSearchCriteria.BookTitle = "Downbelow Station";

            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var text = GetRequestBody(request)["tor"]["text"].Value<string>();

            text.Should().Be($"{expectedAuthorPart} Downbelow Station");
        }

        [TestCase("C.J. Cherryh", "C J Cherryh")]
        [TestCase("J.K. Rowling", "J K Rowling")]
        public void author_search_should_normalize_periods_in_author_name(string authorName, string expected)
        {
            _authorSearchCriteria.Author = new Author { Name = authorName };

            var results = Subject.GetSearchRequests(_authorSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var text = GetRequestBody(request)["tor"]["text"].Value<string>();

            text.Should().Be(expected);
        }

        [Test]
        public void search_type_active_should_map_to_active()
        {
            Subject.Settings = new MyAnonamouseSettings
            {
                Cookie = "test_cookie",
                SearchType = (int)MyAnonamouseSearchType.Active
            };

            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["searchType"].Value<string>().Should().Be("active");
        }

        [Test]
        public void search_type_inactive_should_map_to_inactive()
        {
            Subject.Settings = new MyAnonamouseSettings
            {
                Cookie = "test_cookie",
                SearchType = (int)MyAnonamouseSearchType.Inactive
            };

            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["searchType"].Value<string>().Should().Be("inactive");
        }

        [Test]
        public void search_type_all_should_map_to_all()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["searchType"].Value<string>().Should().Be("all");
        }

        // MAM only returns the description field when this flag is present on the request.
        // It is request-level, not per-release: every result carries it, or none do.
        [Test]
        public void recent_request_should_request_description()
        {
            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;

            GetRequestBody(request)["description"].Should().NotBeNull();
        }

        [Test]
        public void book_search_requests_should_all_request_description()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var requests = results.GetTier(0).SelectMany(r => r).ToList();

            requests.Should().HaveCount(2);
            requests.Should().OnlyContain(r => GetRequestBody(r.HttpRequest)["description"] != null);
        }

        [Test]
        public void author_search_request_should_request_description()
        {
            var results = Subject.GetSearchRequests(_authorSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;

            GetRequestBody(request)["description"].Should().NotBeNull();
        }
    }
}
