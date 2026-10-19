// Shared helpers for the runtime tests: a fetch stub that records what the generated client sends.

/**
 * Replaces globalThis.fetch. The handler receives (url, init) and returns a Response (or a promise of one).
 * Returns the recorded calls and a restore function.
 */
export function stubFetch(handler) {
    const original = globalThis.fetch;
    const calls = [];

    globalThis.fetch = async (url, init) => {
        calls.push({ url: String(url), init: init ?? {} });
        return handler(url, init ?? {});
    };

    return {
        calls,
        restore() {
            globalThis.fetch = original;
        },
    };
}

export function jsonResponse(body, status = 200) {
    return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

export function textResponse(text, status = 200, contentType = "text/plain") {
    return new Response(text, { status, headers: { "content-type": contentType } });
}

/** A response without a content-type header (a string body would get text/plain). */
export function bareResponse(text, status = 200) {
    return new Response(new TextEncoder().encode(text), { status });
}

/** Runs the body with a stubbed fetch that answers every call with the given response factory. */
export async function withFetch(handler, body) {
    const stub = stubFetch(handler);
    try {
        return await body(stub.calls);
    } finally {
        stub.restore();
    }
}

/** Header names of a recorded call, lower-cased, so tests do not depend on header casing. */
export function headerNames(call) {
    return Object.keys(call.init.headers ?? {}).map((name) => name.toLowerCase());
}
