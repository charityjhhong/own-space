// Own Space design-assistant proxy (Cloudflare Worker).
// POST /v1/assist with the app's fixed request shape → { "plan": "<model JSON text>" }.
// The Gemini key lives only in the GEMINI_API_KEY Worker secret. Request bodies are never logged.

import { MAX_BODY_BYTES, answerText, geminiBody, isShapeError, parseRequest } from "./shape.ts";

export interface Env {
  GEMINI_API_KEY: string;
  GEMINI_MODEL: string;
  DEVICE_LIMITER: RateLimit;
  GLOBAL_LIMITER: RateLimit;
}

/** Total time for Gemini, including the one retry. The app gives up at 8 s and uses its offline preset. */
export const UPSTREAM_BUDGET_MS = 6000;
const DEVICE_ID = /^[A-Za-z0-9-]{16,64}$/;

function reply(status: number, body: Record<string, unknown>): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json", "cache-control": "no-store" },
  });
}

function callGemini(env: Env, body: Record<string, unknown>, timeoutMs: number): Promise<Response> {
  const url = `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(env.GEMINI_MODEL)}:generateContent`;
  return fetch(url, {
    method: "POST",
    headers: { "content-type": "application/json", "x-goog-api-key": env.GEMINI_API_KEY },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(timeoutMs),
  });
}

/**
 * The whole request path. Cheap checks first (path, size, shape, device id), so a bad request
 * never spends rate-limit budget; then the limiters; then Gemini within one time budget.
 */
export async function handle(request: Request, env: Env, budgetMs = UPSTREAM_BUDGET_MS): Promise<Response> {
  const url = new URL(request.url);
  if (url.pathname !== "/v1/assist") return reply(404, { error: "not_found" });
  if (request.method !== "POST") return reply(405, { error: "method_not_allowed" });
  if (!env.GEMINI_API_KEY) return reply(503, { error: "not_configured" });

  const declared = Number(request.headers.get("content-length") ?? "0");
  if (declared > MAX_BODY_BYTES) return reply(413, { error: "too_large" });
  const bytes = new Uint8Array(await request.arrayBuffer());
  if (bytes.byteLength > MAX_BODY_BYTES) return reply(413, { error: "too_large" });
  let req;
  try {
    req = parseRequest(JSON.parse(new TextDecoder().decode(bytes)));
  } catch (e) {
    return reply(400, { error: "bad_request", detail: isShapeError(e) ? e.message : "not JSON" });
  }
  const deviceId = request.headers.get("x-device-id") ?? "";
  if (!DEVICE_ID.test(deviceId)) return reply(400, { error: "bad_request", detail: "x-device-id header missing or malformed" });

  if (!(await env.DEVICE_LIMITER.limit({ key: deviceId })).success ||
      !(await env.GLOBAL_LIMITER.limit({ key: "all" })).success) {
    return reply(429, { error: "rate_limited" });
  }

  const deadline = Date.now() + budgetMs;
  try {
    let upstream = await callGemini(env, geminiBody(req, true), budgetMs);
    if (upstream.status === 400) {
      // The schema or thinking field was rejected: one retry without them, in the same budget.
      const left = deadline - Date.now();
      if (left < 250) return reply(504, { error: "timeout" });
      upstream = await callGemini(env, geminiBody(req, false), left);
    }
    if (upstream.status === 429) return reply(429, { error: "upstream_busy" });
    if (!upstream.ok) {
      console.log(`gemini status ${upstream.status}`); // logged here only, never returned
      return reply(502, { error: "upstream_error" });
    }
    const plan = answerText(await upstream.json());
    return plan === null ? reply(502, { error: "no_answer" }) : reply(200, { plan });
  } catch (e) {
    const timedOut = e instanceof DOMException && (e.name === "TimeoutError" || e.name === "AbortError");
    return reply(timedOut ? 504 : 502, { error: timedOut ? "timeout" : "upstream_error" });
  }
}

export default {
  fetch: (request: Request, env: Env) => handle(request, env),
} satisfies ExportedHandler<Env>;
