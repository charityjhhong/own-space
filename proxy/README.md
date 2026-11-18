# Design-assistant proxy

A small Cloudflare Worker between the app and the Gemini API. The app only knows this Worker's
URL. **The Gemini API key lives only in a Worker secret.** It is never in this repo, the APK, a
config asset or a chat message.

What it does:

- Accepts only `POST /v1/assist` with the app's fixed request shape (`src/shape.ts`; example in
  `test/request.example.json`). Every field is checked and the request is rebuilt from known fields.
  Anything else gets `400` with the bad field's name.
- Limits each device to 6 requests a minute (`x-device-id`, the app's random install id) and all
  devices together to 60 a minute (`429`). Bad requests are refused before they spend any of it.
- Spends at most 6 s on Gemini, including one retry, so it answers before the app's 8 s fallback.
  Upstream errors are logged as a status code only and never passed to the client.
- Calls `generateContent` with a fixed system prompt and JSON output limited to the catalog,
  finish and mood ids the app sent. It returns `{ "plan": "<JSON text>" }`, which the app parses
  strictly and validates with its own placement and fit rules before applying anything.
- If Gemini doesn't answer in time it returns `504`; the app then uses its offline preset and
  tells the user.

**What the limits are not:** Cloudflare's rate-limit binding counts per location and is eventually
consistent, and `x-device-id` is chosen by the client, so a determined caller can rotate it. The
limits stop loops and casual abuse. **The real spend cap is the Google AI Studio project's limit**
(on the free tier there is no bill at all).

**Privacy:** on the free tier Google may have people review inputs. We send only room geometry
(floor and zone outlines, door/window and furniture boxes), catalog/finish/mood ids, and the
user's intent text (at most 200 characters). No names, images, audio or account data.

## Set up (one time, about 10 minutes) — the human

You need a Google account and a free Cloudflare account. Run the commands in this `proxy/` folder.

1. **Create the API key.** Open [Google AI Studio](https://aistudio.google.com), choose
   **Get API key → Create API key**, and copy it. Don't paste it anywhere else. In AI Studio,
   set a **spend limit** on the project.
2. **Install and log in:**
   ```bash
   npm install
   npx wrangler login
   ```
3. **Store the key as a secret** (it asks for the value; paste the key there):
   ```bash
   npx wrangler secret put GEMINI_API_KEY
   ```
4. **Deploy:**
   ```bash
   npx wrangler deploy
   ```
   Wrangler prints the URL, e.g. `https://ownspace-assistant.<your-subdomain>.workers.dev`.
   Post that URL (not the key) on Agent sync #1 so `xr` can put it in the app's config asset.

To check it works, send the example request:

```bash
curl -s -X POST "$URL/v1/assist" -H "content-type: application/json" \
  -H "x-device-id: test-device-0000001" --data @test/request.example.json
```

You should get `{"plan":"{...}"}` within a few seconds.

## Change the model

`GEMINI_MODEL` in `wrangler.jsonc` (default `gemini-3.5-flash-lite`, a stable fast model in
AI Studio's model list. Change it and run `npx wrangler deploy` again.

## For agents

- `npm run check` (TypeScript) and `npm test` (Node's built-in test runner on the `.ts` files) need
  no account. Tests need Node 22.18+ or 23.6+, where TypeScript type stripping is on by default
  (on Node 22.6–22.17, run `node --experimental-strip-types --test test/`).
- `npx wrangler deploy --dry-run --outdir /tmp/out` builds without deploying.
- **Checked in Google's docs:** `POST .../v1beta/models/{model}:generateContent` with
  the `x-goog-api-key` header; `generationConfig.responseMimeType: "application/json"`.
  `responseJsonSchema` is listed but marked internal, and `thinkingConfig.thinkingLevel` is not
  confirmed for this model. So if Google answers `400`, the Worker retries once without the schema
  and thinking fields (same 6 s budget), and the app's parser enforces the shape either way.
- Never log request bodies or the key. The Worker logs only upstream status codes.
