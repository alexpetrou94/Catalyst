import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createClient } from "../build/Snapshots/edge-cases.js";
import { createClient as createMinimalClient } from "../build/Snapshots/minimal.js";
import { bareResponse, headerNames, jsonResponse, textResponse, withFetch } from "./helpers.mjs";

const ok = () => jsonResponse({ id: "1" });

describe("base URL", () => {
    it("keeps the path of the server URL, with variables replaced by their defaults", async () => {
        await withFetch(ok, async (calls) => {
            await createClient().get.ping();
            assert.equal(calls[0].url, "https://api.example.com/api/v1/ping");
        });
    });

    it("keeps the path of a caller supplied base URL and ignores trailing slashes", async () => {
        await withFetch(ok, async (calls) => {
            await createClient({ baseUrl: "https://other.test/root//" }).get.ping();
            assert.equal(calls[0].url, "https://other.test/root/ping");
        });
    });

    it("requires a base URL when the spec has no server", () => {
        assert.throws(() => createMinimalClient(), /baseUrl is required/);
        assert.doesNotThrow(() => createMinimalClient({ baseUrl: "http://localhost" }));
    });
});

describe("path parameters", () => {
    it("substitutes and encodes a path-level parameter", async () => {
        await withFetch(ok, async (calls) => {
            await createClient().get.itemsId("a b/c", "2024");
            assert.equal(new URL(calls[0].url).pathname, "/api/v1/items/a%20b%2Fc");
        });
    });

    it("substitutes and encodes a parameter the spec never declared", async () => {
        await withFetch(ok, async (calls) => {
            await createClient().get.ordersOrderIdLinesLineNo(7, "x/y z");
            assert.equal(calls[0].url, "https://api.example.com/api/v1/orders/7/lines/x%2Fy%20z");
        });
    });
});

describe("query parameters", () => {
    it("uses the wire names of hyphenated parameters and drops undefined values", async () => {
        await withFetch(ok, async (calls) => {
            await createClient().get.itemsId("1", "v2", 25);
            assert.equal(new URL(calls[0].url).search, "?page-size=25&api-version=v2");
        });
    });

    it("sends a parameter named like the inner request function or a path parameter under its wire name", async () => {
        await withFetch(ok, async (calls) => {
            await createClient().get.itemsId("p", "v", undefined, "r", "q");
            const url = new URL(calls[0].url);
            assert.equal(url.pathname, "/api/v1/items/p");
            assert.equal(url.searchParams.get("request"), "r");
            assert.equal(url.searchParams.get("id"), "q");
            assert.equal(url.searchParams.get("api-version"), "v");
        });
    });

    it("repeats array parameters", async () => {
        await withFetch(ok, async (calls) => {
            await createClient().get.search("plain", ["a", "b"]);
            assert.equal(new URL(calls[0].url).search, "?tags=a&tags=b&kind=plain");
        });
    });

    it("keeps parameters named body, signal and headers apart from the real ones", async () => {
        await withFetch(ok, async (calls) => {
            const controller = new AbortController();
            await createClient().get.search("plain", undefined, "B", "S", "H", { "x-extra": "1" }, controller.signal);

            const url = new URL(calls[0].url);
            assert.equal(url.searchParams.get("body"), "B");
            assert.equal(url.searchParams.get("signal"), "S");
            assert.equal(url.searchParams.get("headers"), "H");
            assert.equal(calls[0].init.headers["x-extra"], "1");
            assert.equal(calls[0].init.signal, controller.signal);
            assert.equal(calls[0].init.body, undefined);
        });
    });

    it("appends to the URL with ? and sends booleans as text", async () => {
        await withFetch(ok, async (calls) => {
            await createClient().post.items({ "x-trace": "t" }, true);
            assert.equal(calls[0].url, "https://api.example.com/api/v1/items?notify=true");
        });
    });
});

describe("request body", () => {
    it("sends objects as JSON with a content-type", async () => {
        await withFetch(ok, async (calls) => {
            await createClient().post.items({ "x-trace": "t" });
            const call = calls[0];
            assert.equal(call.init.method, "post");
            assert.equal(call.init.body, JSON.stringify({ "x-trace": "t" }));
            assert.equal(call.init.headers["Content-Type"], "application/json");
        });
    });

    it("respects a content-type the caller set in lower case", async () => {
        await withFetch(ok, async (calls) => {
            await createClient().post.items({ "x-trace": "t" }, undefined, { "content-type": "application/vnd.api+json" });
            const call = calls[0];
            assert.deepEqual(headerNames(call).filter((name) => name === "content-type"), ["content-type"]);
            assert.equal(call.init.headers["content-type"], "application/vnd.api+json");
        });
    });

    it("merges client level and call level headers", async () => {
        await withFetch(ok, async (calls) => {
            await createClient({ headers: { authorization: "Bearer t", "x-a": "client" } }).get.ping({ "x-a": "call" });
            assert.equal(calls[0].init.headers.authorization, "Bearer t");
            assert.equal(calls[0].init.headers["x-a"], "call");
        });
    });
});

describe("responses", () => {
    it("parses a JSON response", async () => {
        await withFetch(() => jsonResponse({ id: "9" }), async () => {
            const result = await createClient().get.defaultOnly();
            assert.deepEqual(result, { data: { id: "9" }, error: null });
        });
    });

    it("returns the text of a non-JSON response", async () => {
        await withFetch(() => textResponse("pong"), async () => {
            const result = await createClient().get.ping();
            assert.deepEqual(result, { data: "pong", error: null });
        });
    });

    it("parses a body without a content-type when it is JSON and keeps it as text when it is not", async () => {
        await withFetch(() => bareResponse('{"id":"9"}'), async () => {
            assert.deepEqual((await createClient().get.defaultOnly()).data, { id: "9" });
        });
        await withFetch(() => bareResponse("pong"), async () => {
            assert.equal((await createClient().get.ping()).data, "pong");
        });
    });

    it("returns undefined data for 204", async () => {
        await withFetch(() => new Response(null, { status: 204 }), async () => {
            const result = await createClient().put.itemsId("1", { id: "x" });
            assert.deepEqual(result, { data: undefined, error: null });
        });
    });

    it("turns a JSON problem response into a ProblemDetail", async () => {
        await withFetch(() => jsonResponse({ title: "Nope", detail: "Not here", code: 7 }, 404), async () => {
            const result = await createClient().get.ping();
            assert.equal(result.data, null);
            assert.equal(result.error.status, 404);
            assert.equal(result.error.title, "Nope");
            assert.equal(result.error.detail, "Not here");
            assert.deepEqual(result.error.extensions, { code: 7 });
        });
    });

    it("turns a plain text error into a ProblemDetail", async () => {
        await withFetch(() => textResponse("boom", 500), async () => {
            const result = await createClient().get.ping();
            assert.equal(result.data, null);
            assert.equal(result.error.status, 500);
            assert.equal(result.error.detail, "boom");
        });
    });
});

describe("timeouts", () => {
    // A fetch that never answers on its own and rejects when its signal aborts.
    const hang = (url, init) => new Promise((resolve, reject) => {
        init.signal?.addEventListener("abort", () => reject(init.signal.reason));
    });

    it("aborts a hanging request after timeoutMs", async () => {
        await withFetch(hang, async () => {
            await assert.rejects(createClient({ timeoutMs: 20 }).get.ping(), (error) => error.name === "TimeoutError");
        });
    });

    it("still honours the caller's own signal", async () => {
        await withFetch(hang, async () => {
            const controller = new AbortController();
            const pending = createClient({ timeoutMs: 10_000 }).get.ping(undefined, controller.signal);
            controller.abort(new Error("caller gave up"));
            await assert.rejects(pending, /caller gave up/);
        });
    });
});

describe("multipart bodies", () => {
    const pending = () => new Response(null, { status: 204 });

    it("builds FormData with files, repeated array entries and JSON objects, and leaves the content-type to fetch", async () => {
        await withFetch(pending, async (calls) => {
            const file = new Blob(["abc"], { type: "text/plain" });
            await createClient().post.createUpload({ file, tags: ["a", "b"], meta: { id: "1" }, note: undefined });

            const form = calls[0].init.body;
            assert.ok(form instanceof FormData);
            assert.equal(await form.get("file").text(), "abc");
            assert.deepEqual(form.getAll("tags"), ["a", "b"]);
            assert.equal(form.get("meta"), '{"id":"1"}');
            assert.equal(form.has("note"), false);
            assert.deepEqual(headerNames(calls[0]).filter((name) => name === "content-type"), []);
        });
    });

    it("passes FormData supplied by the caller through untouched", async () => {
        await withFetch(pending, async (calls) => {
            const form = new FormData();
            form.append("file", new Blob(["x"]));
            await createClient().post.createUpload(form);

            assert.equal(calls[0].init.body, form);
        });
    });
});

describe("other body encodings", () => {
    const pending = () => new Response(null, { status: 204 });

    it("sends a urlencoded body with its content-type", async () => {
        await withFetch(pending, async (calls) => {
            await createClient().post.createLogin({ username: "a b", scopes: ["x", "y"], remember: true });

            assert.equal(String(calls[0].init.body), "username=a+b&scopes=x&scopes=y&remember=true");
            assert.equal(calls[0].init.headers["Content-Type"], "application/x-www-form-urlencoded");
        });
    });

    it("sends text as is with a text/plain content-type unless the caller chose one", async () => {
        await withFetch(pending, async (calls) => {
            await createClient().put.notes("hello");
            await createClient().put.notes("hello", { "content-type": "text/markdown" });

            assert.equal(calls[0].init.body, "hello");
            assert.equal(calls[0].init.headers["Content-Type"], "text/plain");
            assert.deepEqual(Object.keys(calls[1].init.headers), ["content-type"]);
        });
    });
});

describe("binary responses", () => {
    it("returns a Blob with the bytes of the response", async () => {
        const bytes = new Uint8Array([137, 80, 78, 71]);
        await withFetch(() => new Response(bytes, { headers: { "content-type": "image/png" } }), async () => {
            const result = await createClient().get.avatar("me");

            assert.ok(result.data instanceof Blob);
            assert.deepEqual(new Uint8Array(await result.data.arrayBuffer()), bytes);
        });
    });
});
