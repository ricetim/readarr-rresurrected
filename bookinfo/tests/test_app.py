"""Test the FastAPI app routes (stateless version).

Tests focus on routing behavior (correct status codes, response structure)
and mocked GoodreadsClient. No SQLite or cache_module involved.
"""
from __future__ import annotations

import importlib
import time
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest
import pytest_asyncio
import respx
from httpx import ASGITransport, AsyncClient

from goodreads import GRAPHQL_URL, XML_BASE, GoodreadsClient


@pytest_asyncio.fixture
async def app_and_client():
    mock_gr = MagicMock()
    mock_gr.resolve_author_xml = AsyncMock(
        return_value=("kca://author/amzn1.gr.author.v1.Test", "C.S. Lewis", "", "")
    )
    mock_gr.fetch_author_fast_path = AsyncMock(
        return_value=(
            {
                "ForeignId": 3389,
                "Name": "C.S. Lewis",
                "Kca": "kca://author/amzn1.gr.author.v1.Test",
                "Works": [{"ForeignId": 111, "Books": [], "Title": "Narnia"}],
                "Series": [],
            },
            "next_token_abc",
        )
    )
    mock_gr.complete_author_background = AsyncMock(
        return_value={
            "ForeignId": 3389,
            "Name": "C.S. Lewis",
            "Kca": "kca://author/amzn1.gr.author.v1.Test",
            "Works": [
                {"ForeignId": 111, "Books": [], "Title": "Narnia"},
                {"ForeignId": 222, "Books": [], "Title": "Perelandra"},
            ],
            "Series": [],
        }
    )
    mock_gr.batch_graphql = AsyncMock(return_value=[])
    mock_gr.search = AsyncMock(return_value=[])
    mock_gr.get_series = AsyncMock(return_value=None)
    mock_gr.close = AsyncMock()

    # Reload app module for clean state, then inject the mock directly.
    # We bypass the lifespan (which would open real connections) by setting
    # goodreads_client directly on the module after reload.
    import app as app_module
    importlib.reload(app_module)
    app_module.goodreads_client = mock_gr
    app_module._pending_complete.clear()

    transport = ASGITransport(app=app_module.app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        yield client, mock_gr

    # Cleanup after test
    app_module._pending_complete.clear()


@pytest.mark.asyncio
async def test_get_author_returns_partial_immediately(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/author/3389")
    assert response.status_code == 200
    data = response.json()
    assert data["ForeignId"] == 3389
    assert data["Kca"] == "kca://author/amzn1.gr.author.v1.Test"


@pytest.mark.asyncio
async def test_get_author_passes_kca_query_param(app_and_client):
    client, mock_gr = app_and_client
    kca = "kca://author/amzn1.gr.author.v1.Test"
    response = await client.get(f"/author/3389?kca={kca}")
    assert response.status_code == 200
    call_kwargs = mock_gr.fetch_author_fast_path.call_args
    assert call_kwargs.kwargs.get("author_kca") == kca or (
        len(call_kwargs.args) >= 3 and call_kwargs.args[2] == kca
    )


@pytest.mark.asyncio
async def test_get_author_skips_xml_when_kca_provided(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/author/3389?kca=kca://author/amzn1.gr.author.v1.Test")
    assert response.status_code == 200
    mock_gr.resolve_author_xml.assert_not_called()


@pytest.mark.asyncio
async def test_get_author_calls_xml_when_no_kca(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/author/3389")
    assert response.status_code == 200
    mock_gr.resolve_author_xml.assert_called_once_with(3389)


@pytest.mark.asyncio
async def test_get_author_complete_available_via_pending(app_and_client):
    client, mock_gr = app_and_client
    import app as app_module

    complete_data = {
        "ForeignId": 3389,
        "Name": "C.S. Lewis",
        "Kca": "kca://author/amzn1.gr.author.v1.Test",
        "Works": [
            {"ForeignId": 111, "Books": [], "Title": "Narnia"},
            {"ForeignId": 222, "Books": [], "Title": "Perelandra"},
        ],
        "Series": [],
    }
    app_module._pending_complete[3389] = (complete_data, time.monotonic() + 3600)

    response = await client.get("/author/3389")
    assert response.status_code == 200
    data = response.json()
    # Should return complete data (2 works) from _pending_complete
    assert len(data["Works"]) == 2
    assert data["Works"][1]["Title"] == "Perelandra"
    # Entry should be popped (one-time pickup)
    assert 3389 not in app_module._pending_complete


@pytest.mark.asyncio
async def test_author_changed_returns_limited(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/author/changed")
    assert response.status_code == 200
    assert response.json() == {"Limited": True, "Ids": []}


@pytest.mark.asyncio
async def test_work_returns_404_for_unknown_work(app_and_client):
    client, mock_gr = app_and_client
    mock_gr.get_work = AsyncMock(return_value=None)
    response = await client.get("/work/12345")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_recommended_returns_empty(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/recommended")
    assert response.status_code == 200
    assert response.json() == {"WorkIds": []}


@pytest.mark.asyncio
async def test_search_delegates_to_goodreads_client(app_and_client):
    client, mock_gr = app_and_client
    mock_gr.search = AsyncMock(
        return_value=[{"BookId": 17910048, "WorkId": 24241248, "Author": {"Id": 3389}}]
    )
    response = await client.get("/search?q=Narnia")
    assert response.status_code == 200
    results = response.json()
    assert len(results) == 1
    assert results[0]["BookId"] == 17910048


@pytest.mark.asyncio
async def test_series_not_found_returns_404(app_and_client):
    client, mock_gr = app_and_client
    # mock_gr.get_series already returns None by default
    response = await client.get("/series/99999")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_series_found_returns_data(app_and_client):
    client, mock_gr = app_and_client
    mock_gr.get_series = AsyncMock(
        return_value={
            "ForeignId": 330959,
            "Title": "The Chronicles of Narnia",
            "Description": "",
            "LinkItems": [],
        }
    )
    response = await client.get("/series/330959")
    assert response.status_code == 200
    assert response.json()["Title"] == "The Chronicles of Narnia"


@pytest.mark.asyncio
async def test_book_edition_not_found_returns_404(app_and_client):
    client, mock_gr = app_and_client
    # batch_graphql returns [] by default
    response = await client.get("/book/99999")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_book_edition_found_redirects_to_author(app_and_client):
    client, mock_gr = app_and_client
    mock_gr.batch_graphql = AsyncMock(
        return_value=[
            {
                "primaryContributorEdge": {
                    "node": {"legacyId": 3389, "name": "C.S. Lewis"}
                }
            }
        ]
    )
    response = await client.get("/book/42640737", follow_redirects=False)
    assert response.status_code == 302
    assert "/author/3389" in response.headers["location"]


@pytest.mark.asyncio
async def test_delete_cache_author_removes_pending(app_and_client):
    client, mock_gr = app_and_client
    import app as app_module

    app_module._pending_complete[3389] = ({"ForeignId": 3389}, time.monotonic() + 3600)
    response = await client.delete("/cache/author/3389")
    assert response.status_code == 204
    assert 3389 not in app_module._pending_complete


@pytest.mark.asyncio
async def test_delete_cache_author_noop_when_missing(app_and_client):
    client, mock_gr = app_and_client
    # Should not error if author not in pending
    response = await client.delete("/cache/author/99999")
    assert response.status_code == 204


@pytest.mark.asyncio
async def test_get_book_bulk_empty(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/book/bulk")
    assert response.status_code == 200
    data = response.json()
    assert data == {"Works": [], "Series": [], "Authors": []}


@pytest.mark.asyncio
async def test_get_book_bulk_returns_works(app_and_client):
    client, mock_gr = app_and_client
    mock_gr.batch_graphql = AsyncMock(
        return_value=[
            {
                "work": {"legacyId": 111, "editions": {"edges": []}},
                "primaryContributorEdge": {
                    "node": {"legacyId": 3389, "name": "C.S. Lewis"}
                },
                "legacyId": 42640737,
                "title": "The Lion, the Witch and the Wardrobe",
            }
        ]
    )
    response = await client.get("/book/bulk?id=42640737")
    assert response.status_code == 200
    data = response.json()
    assert len(data["Works"]) == 1
    assert len(data["Authors"]) == 1
    assert data["Authors"][0]["ForeignId"] == 3389


@pytest.mark.asyncio
async def test_on_progress_series_covers_all_pages(app_and_client):
    """on_progress should produce Series covering works from all pages fetched so far."""
    import app as app_module

    client, mock_gr = app_and_client
    intermediate = []

    async def mock_complete(**kwargs):
        on_progress = kwargs.get("on_progress")
        if on_progress:
            works_by_id = {
                999: {
                    "ForeignId": 999,
                    "Title": "Narnia",
                    "Books": [],
                    "Series": [{"ForeignId": 1001, "Title": "Chronicles of Narnia"}],
                },
                888: {
                    "ForeignId": 888,
                    "Title": "Perelandra",
                    "Books": [],
                    "Series": [{"ForeignId": 1002, "Title": "Space Trilogy"}],
                },
            }
            on_progress(works_by_id, total_count=2)
            # Capture what Readarr would read now; the completed payload replaces it below.
            intermediate.append(app_module._pending_complete[3389][0])
        return kwargs.get("partial_data", {})

    mock_gr.complete_author_background = AsyncMock(side_effect=mock_complete)

    response = await client.get("/author/3389")
    assert response.status_code == 200

    assert len(intermediate) == 1
    data = intermediate[0]
    assert data["Partial"] is True
    series_ids = {s["ForeignId"] for s in data.get("Series", [])}
    assert 1001 in series_ids
    assert 1002 in series_ids


@pytest.mark.asyncio
async def test_post_book_bulk_redirects_to_get(app_and_client):
    client, mock_gr = app_and_client
    response = await client.post("/book/bulk", json=[42640737, 12345], follow_redirects=False)
    assert response.status_code == 302
    location = response.headers["location"]
    assert "id=42640737" in location
    assert "id=12345" in location


# ---------------------------------------------------------------------------
# GET /work/{work_id} — edition-page resolution + hydration (issue #14)
# ---------------------------------------------------------------------------

WORK_ID = 210573212
EDITION_ID = 204662948
SECOND_EDITION_ID = 204662949
AUTHOR_ID = 4242


def _edition_node(legacy_id: int, fmt: str, ratings: int, language: str = "English") -> dict:
    """A BookInfo-shaped GraphQL node for one edition."""
    return {
        "id": f"kca://book/amzn1.gr.book.v1.{legacy_id}",
        "legacyId": legacy_id,
        "title": "Heaven and Earth",
        "titlePrimary": "Heaven and Earth",
        "webUrl": f"https://www.goodreads.com/book/show/{legacy_id}",
        "imageUrl": "https://images.example/cover.jpg",
        "bookGenres": [],
        "bookSeries": [],
        "details": {
            "asin": "",
            "isbn13": "",
            "format": fmt,
            "numPages": 320,
            "language": {"name": language},
            "publisher": "Test Press",
            "publicationTime": 1600000000000,
        },
        "primaryContributorEdge": {"node": {"legacyId": AUTHOR_ID, "name": "Jonathan Yanez"}},
        "stats": {"averageRating": 4.1, "ratingsCount": ratings, "ratingsSum": ratings * 4},
    }


def _book_response(work_legacy_id: int, inline_editions: list[dict] | None = None) -> dict:
    """A GetBookByLegacyId result: one book node embedding its work."""
    book = _edition_node(EDITION_ID, "Kindle Edition", ratings=500)
    book["work"] = {
        "id": "kca://work/amzn1.gr.work.v1.test",
        "legacyId": work_legacy_id,
        "details": {
            "webUrl": f"https://www.goodreads.com/work/{work_legacy_id}",
            "publicationTime": 1600000000000,
        },
        "bestBook": {
            "legacyId": EDITION_ID,
            "titlePrimary": "Heaven and Earth",
            "primaryContributorEdge": {"role": "PRIMARY", "node": {"legacyId": AUTHOR_ID}},
        },
        "editions": {"edges": [{"node": e} for e in (inline_editions or [])]},
    }
    book["bookGenres"] = [{"genre": {"name": "Fantasy"}}]
    return book


EDITION_PAGE_HTML = (
    "<html><body>"
    f'<a class="bookTitle" href="/book/show/{EDITION_ID}">Heaven and Earth</a>'
    f'<a class="bookTitle" href="/book/show/{SECOND_EDITION_ID}">Heaven and Earth</a>'
    "</body></html>"
)


@pytest_asyncio.fixture
async def work_client():
    """ASGI client with a *real* GoodreadsClient wired in (HTTP mocked via respx)."""
    import app as app_module

    importlib.reload(app_module)
    app_module._pending_complete.clear()
    transport = ASGITransport(app=app_module.app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        yield client, app_module


async def _install_real_client(app_module, rate: float = 1000):
    """Create the client *inside* the active respx router so its transport is mocked."""
    client = GoodreadsClient(rate=rate, batch_size=20)
    app_module.goodreads_client = client
    return client


@pytest.mark.asyncio
async def test_work_returns_work_resource_for_valid_work_id(work_client):
    client, app_module = work_client
    with respx.mock:
        respx.get(f"{XML_BASE}/work/editions/{WORK_ID}").mock(
            return_value=httpx.Response(200, text=EDITION_PAGE_HTML)
        )
        graphql = respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(200, json={"data": {"q0": _book_response(WORK_ID)}})
        )
        gr = await _install_real_client(app_module)

        response = await client.get(f"/work/{WORK_ID}")

        assert response.status_code == 200
        assert graphql.called
        await gr.close()

    payload = response.json()
    assert payload["ForeignId"] == WORK_ID
    assert payload["Title"] == "Heaven and Earth"
    assert payload["Genres"] == ["Fantasy"]
    assert payload["Authors"] == [
        {"ForeignId": AUTHOR_ID, "Name": "Jonathan Yanez", "KCA": ""}
    ]
    assert [b["ForeignId"] for b in payload["Books"]] == [EDITION_ID]
    assert payload["Books"][0]["IsEbook"] is True
    assert payload["Books"][0]["Language"] == "English"


@pytest.mark.asyncio
async def test_work_includes_inline_editions_from_the_work(work_client):
    client, app_module = work_client
    inline = [_edition_node(SECOND_EDITION_ID, "Hardcover", ratings=10)]
    with respx.mock:
        respx.get(f"{XML_BASE}/work/editions/{WORK_ID}").mock(
            return_value=httpx.Response(200, text=EDITION_PAGE_HTML)
        )
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200, json={"data": {"q0": _book_response(WORK_ID, inline)}}
            )
        )
        gr = await _install_real_client(app_module)

        response = await client.get(f"/work/{WORK_ID}")

        assert response.status_code == 200
        await gr.close()

    books = response.json()["Books"]
    assert {b["ForeignId"] for b in books} == {EDITION_ID, SECOND_EDITION_ID}
    assert {b["Format"] for b in books} == {"Kindle Edition", "Hardcover"}


@pytest.mark.asyncio
async def test_work_refuses_to_hydrate_a_different_work(work_client):
    """Work and book ids share one numeric space — a mismatch must 404, not return a book."""
    client, app_module = work_client
    with respx.mock:
        respx.get(f"{XML_BASE}/work/editions/{WORK_ID}").mock(
            return_value=httpx.Response(200, text=EDITION_PAGE_HTML)
        )
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200, json={"data": {"q0": _book_response(999999999)}}
            )
        )
        gr = await _install_real_client(app_module)

        response = await client.get(f"/work/{WORK_ID}")

        assert response.status_code == 404
        assert response.json() == {"detail": "Work not found"}
        await gr.close()


@pytest.mark.asyncio
async def test_work_returns_404_when_editions_page_has_no_book_links(work_client):
    client, app_module = work_client
    with respx.mock:
        respx.get(f"{XML_BASE}/work/editions/{WORK_ID}").mock(
            return_value=httpx.Response(200, text="<html><body>No editions</body></html>")
        )
        graphql = respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(200, json={"data": {}})
        )
        gr = await _install_real_client(app_module)

        response = await client.get(f"/work/{WORK_ID}")

        assert response.status_code == 404
        assert not graphql.called  # no edition id → no needless GraphQL call
        await gr.close()


@pytest.mark.asyncio
async def test_work_returns_404_when_graphql_returns_nothing(work_client):
    client, app_module = work_client
    with respx.mock:
        respx.get(f"{XML_BASE}/work/editions/{WORK_ID}").mock(
            return_value=httpx.Response(200, text=EDITION_PAGE_HTML)
        )
        respx.post(GRAPHQL_URL).mock(return_value=httpx.Response(200, json={"data": {}}))
        gr = await _install_real_client(app_module)

        response = await client.get(f"/work/{WORK_ID}")

        assert response.status_code == 404
        await gr.close()


@pytest.mark.asyncio
async def test_work_returns_404_when_goodreads_has_no_such_work(work_client):
    client, app_module = work_client
    with respx.mock:
        respx.get(f"{XML_BASE}/work/editions/{WORK_ID}").mock(
            return_value=httpx.Response(404, text="Not found")
        )
        gr = await _install_real_client(app_module)

        response = await client.get(f"/work/{WORK_ID}")

        assert response.status_code == 404
        await gr.close()


@pytest.mark.asyncio
@pytest.mark.parametrize("status", [403, 500])
async def test_work_reports_upstream_failures_rather_than_not_found(work_client, status):
    """A blocked or failing Goodreads must not look like a deleted work to Readarr."""
    client, app_module = work_client
    with respx.mock:
        respx.get(f"{XML_BASE}/work/editions/{WORK_ID}").mock(
            return_value=httpx.Response(status, text="boom")
        )
        gr = await _install_real_client(app_module)

        response = await client.get(f"/work/{WORK_ID}")

        assert response.status_code != 404
        assert response.status_code >= 500
        await gr.close()
