import { test, afterEach } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { handle, type Env } from "../src/index.ts";

const example = readFileSync(new URL("./request.example.json", import.meta.url), "utf8");
const realFetch = globalThis.fetch;
afterEach(() => { globalThis.fetch = realFetch; });

function env(spent: string[] = []): Env {
  const limiter = (name: string) => ({ limit: async () => { spent.push(name); return { success: true }; } });
  return { GEMINI_API_KEY: "test-key", GEMINI_MODEL: "gemini-3.5-flash-lite", DEVICE_LIMITER: limiter("device"), GLOBAL_LIMITER: limiter("global") } as unknown as Env;
}

function post(body: string, headers: Record<string, string> = {}): Request {
  return new Request("https://proxy.test/v1/assist", {
    method: "POST", body, headers: { "content-type": "application/json", "x-device-id": "device-0000000001", ...headers },
  });
}

const answer = (status: number, text = "{\"rationale\":\"ok\"}") =>
  new Response(JSON.stringify({ candidates: [{ content: { parts: [{ text }] } }] }), { status });

test("bad bodies are refused before any rate-limit budget is spent", async () => {
  const spent: string[] = [];
  assert.equal((await handle(post("{\"v\":2}"), env(spent))).status, 400);
  assert.equal((await handle(post("x".repeat(20_000)), env(spent))).status, 413);
  assert.equal((await handle(post(example, { "x-device-id": "short" }), env(spent))).status, 400);
  assert.deepEqual(spent, []);
});

test("a good request returns the plan text", async () => {
  const sent: any[] = [];
  globalThis.fetch = (async (_url: string, init: RequestInit) => { sent.push(JSON.parse(String(init.body))); return answer(200); }) as typeof fetch;
  const spent: string[] = [];
  const res = await handle(post(example), env(spent));
  assert.equal(res.status, 200);
  assert.deepEqual(await res.json(), { plan: "{\"rationale\":\"ok\"}" });
  assert.deepEqual(spent, ["device", "global"]);
  assert.ok(sent[0].generationConfig.responseJsonSchema);
});

test("a 400 retries once without the schema; other upstream errors are not leaked", async () => {
  const sent: any[] = [];
  globalThis.fetch = (async (_url: string, init: RequestInit) => {
    sent.push(JSON.parse(String(init.body)));
    return sent.length === 1 ? new Response("{}", { status: 400 }) : answer(200);
  }) as typeof fetch;
  assert.equal((await handle(post(example), env())).status, 200);
  assert.equal(sent.length, 2);
  assert.equal(sent[1].generationConfig.responseJsonSchema, undefined);

  globalThis.fetch = (async () => new Response("secret upstream detail", { status: 503 })) as typeof fetch;
  const res = await handle(post(example), env());
  assert.equal(res.status, 502);
  assert.deepEqual(await res.json(), { error: "upstream_error" });
});

test("a slow upstream gives 504 within the budget", async () => {
  globalThis.fetch = ((_url: string, init: RequestInit) => new Promise((_, reject) =>
    init.signal!.addEventListener("abort", () => reject(init.signal!.reason)))) as typeof fetch;
  const started = Date.now();
  const res = await handle(post(example), env(), 200);
  assert.equal(res.status, 504);
  assert.ok(Date.now() - started < 1500);
});
