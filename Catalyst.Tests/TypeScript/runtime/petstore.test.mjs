import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createClient } from "../build/Snapshots/petstore.js";
import { jsonResponse, withFetch } from "./helpers.mjs";

// The petstore server URL is relative ("/api/v3"), which only works when the path of the base URL is kept.
describe("relative base URL", () => {
    it("prefixes the relative server URL", async () => {
        await withFetch(() => jsonResponse({ id: 7 }), async (calls) => {
            const result = await createClient().get.petById(7);

            assert.equal(calls[0].url, "/api/v3/pet/7");
            assert.deepEqual(result.data, { id: 7 });
        });
    });

    it("prefixes a relative base URL given by the caller and appends the query", async () => {
        await withFetch(() => jsonResponse([]), async (calls) => {
            await createClient({ baseUrl: "/proxy/petstore/" }).get.FindPetsByStatus("sold");

            assert.equal(calls[0].url, "/proxy/petstore/pet/findByStatus?status=sold");
        });
    });
});
