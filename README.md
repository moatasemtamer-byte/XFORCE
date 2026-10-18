# XforceMsw

Ladder diagram → MSW conversion platform. Marketing site, licensing, conversion job
pipeline and an admin view, built to sit in front of an external MSW conversion engine.

- **Stack** — Next.js 16 (App Router, React 19, TypeScript), Tailwind CSS 4, SQLite via `better-sqlite3`
- **Runtime** — Node.js, single process, no external services required
- **Engine** — your tool, called over one documented HTTP endpoint

---

## Quick start

```bash
npm install
cp .env.example .env.local        # then set SESSION_SECRET
npm run dev                      # http://localhost:3000
```

Generate a session secret:

```bash
node -e "console.log(require('crypto').randomBytes(48).toString('base64url'))"
```

The SQLite database and uploaded files are created on first use under `./data`.
Schema is applied idempotently from `src/lib/schema.sql` on every cold start.

### Bootstrapping your first admin

Register through the UI — anyone listed in `ADMIN_EMAILS` gets the admin role
automatically. To mint a session directly (local development only):

```bash
node scripts/dev-session.mjs you@example.com "YourPassword123" "Your Name" admin
```

The script prints a `xmsw_session` cookie value for curl testing and refuses to run
when `NODE_ENV=production`.

---

## Connecting the conversion engine

The site ships with a **stub adapter**. With no engine configured, uploads are accepted
and recorded as jobs, but the conversion step fails with `engine_not_connected`. That
is the expected state until your engine is wired in.

Set two variables and conversions start working — no code change, no redeploy of logic:

```bash
MSW_ENGINE_URL=https://your-engine.example.com
MSW_ENGINE_TOKEN=optional-bearer-token
MSW_ENGINE_TIMEOUT_MS=120000
```

### The contract

**Request** — `POST {MSW_ENGINE_URL}/convert`, `multipart/form-data`, optional
`Authorization: Bearer {MSW_ENGINE_TOKEN}`:

| Field        | Type   | Notes                                              |
| ------------ | ------ | -------------------------------------------------- |
| `file`       | file   | The uploaded source drawing                        |
| `jobId`      | string | Platform job id, use it in your logs              |
| `filename`   | string | Sanitised source filename                         |
| `options`    | JSON   | `targetFormat`, `includeNetlist`, `includeBom`, `includeRungsReport`, `sheetName`, `notes` |
| `licenseKey` | string | Issued key, empty when none was selected           |

**Response** — `200 application/json`:

```json
{
  "artifacts": [
    {
      "name": "panel.msw",
      "mimeType": "application/octet-stream",
      "kind": "primary",
      "contentBase64": "TVSW…"
    }
  ],
  "warnings": ["Rung 2 has no terminal comment"],
  "summary": { "rungs": 4, "contacts": 3 },
  "engineVersion": "2.3.1"
}
```

- Each artifact needs `name` plus **either** `contentBase64` **or** `url`. `url` is fetched
  by the platform; the remote must be reachable from the app.
- `kind` is one of `primary`, `report`, `netlist`, `bom`, `preview`. The first artifact
  defaults to `primary`.
- Returning a **non-JSON** body is also supported: the whole response is stored as one
  `primary` artifact.
- `engineVersion` is recorded on the job for traceability.
- Any non-2xx status, network failure, timeout or unparseable response becomes a readable
  error on the job — never a silent success.

**Swapping engines** means changing `MSW_ENGINE_URL`. Multiple artifacts in one response
let the engine return the MSW bundle plus a netlist, BOM and rung report in a single call.

### Bundled engine: Triconex XML to TIA Portal

`engine/` contains a self-contained .NET 7 solution that implements the contract above. It
reads a Triconex XML project export and emits a Siemens TIA Portal import package: SCL
sources per block, SimaticML UDTs, tag tables, a TIA Openness `import.ps1`, and a
`conversion_report.txt`. It never generates `.apXX` files and never guesses silently — anything
it cannot map faithfully is recorded as an approximation or a manual action in the report.

**HTTP server** (what the platform talks to):

```bash
dotnet build engine/TriconexToTia.sln            # or: dotnet run --project engine/src/TriconexToTia.Server
dotnet engine/src/TriconexToTia.Server/bin/Debug/net7.0/TriconexToTia.Server.dll
# listens on http://localhost:5080, or $PORT / $ASPNETCORE_URLS
```

`GET /health` returns `{ ready, engineVersion, converter }`. `POST /convert` accepts the
multipart contract above and returns the TIA package as the `primary` artifact plus the
conversion report as a `report` artifact.

**Command line** (batch or manual runs):

```bash
triconex-to-tia --input project.xml --output ./output [--tia-version 20] [--plc-cpu ORDER]
                [--mappings custom.json] [--fbd-output scl|fbd|auto]
                [--fbd-dialect v16-update3|v16-ga] [--validate-only]
```

`--fbd-output` selects the FBD body format: `scl` (default, IEC 61131-3 structured text),
`fbd` (TIA SimaticML `FlgNet`) or `auto` (FBD where a verified box exists, SCL otherwise).
`--fbd-dialect` chooses the box-template spelling emitted in FBD mode: `v16-update3`
(default, the genuine TIA Portal V16 Update 3 exports) or `v16-ga` (the unpatched V16.0
GA build, which spells `Not` as a negated single-input `A` gate and drops `AutomaticTyped`
from `Move`). Both FBD options are command-line only; the HTTP API always emits SCL.

**Limits / auth** — env vars: `XFORCEMSW_ENGINE_TOKEN` (or `MSW_ENGINE_TOKEN`) requires
`Authorization: Bearer …`; `PORT` selects the listen port; `MSW_MAX_UPLOAD_BYTES` caps the
multipart body (default 512 MB).

---

## HTTP API

| Method | Path                          | Auth        | Purpose                                  |
| ------ | ----------------------------- | ----------- | ---------------------------------------- |
| `POST` | `/api/jobs`                   | session     | Upload a source, run a conversion        |
| `GET`  | `/api/jobs/{id}/output/{n}`   | session     | Download the *n*-th output of a job      |
| `GET`  | `/api/licenses/validate?key=` | public      | Check a key without claiming a seat      |
| `POST` | `/api/licenses/validate`      | public      | Check a key and claim a device seat      |

`POST /api/jobs` accepts `file` plus any of the option fields above and returns
`{ id, status, error }`. Uploads are limited to 15 extensions and to your plan's maximum
file size; monthly quota is enforced server side.

License validation responses look like:

```json
{ "valid": true, "plan": "professional", "activations": 2, "activationLimit": 5,
  "expiresAt": null, "seatStatus": "already_held" }
```

Rejections return a machine-readable `reason`: `unknown_key`, `expired`, `suspended`,
`revoked`, `activation_limit`, `rate_limited`.

### Shared seats

A license key is a pool of seats, not a single activation. Seats are taken
automatically, first come first served — nobody is assigned in advance:

- A signed-in account claims a seat for its own email on its first conversion with
  the key, and keeps it on every later conversion.
- `POST /api/licenses/validate` claims a seat for the posted `device` label and is
  idempotent, so a client can poll it without consuming extra seats. Unlabelled
  clients share the single `unnamed-device` seat.
- Anyone holding the key may claim a seat until the limit is reached, so a key can
  be shared across accounts. Once it is full, further claims are refused.
- A seat is a row in `license_seats`, so the dashboard can always show who is using
  a license. Admins and the license owner can release a seat, which frees it for
  the next account to claim, or change the seat count and expiry at any time.
- `activations` on the license row is a denormalised copy of the seat count, kept
  in step by claims and releases.

---

## Plans and entitlements

| Plan           | Conversions/mo | Max file | Seats | Keys | Retention | API |
| -------------- | -------------- | -------- | ----- | ---- | --------- | --- |
| Free           | 5              | 5 MB     | 1     | 0    | 7 days    | no  |
| Starter        | 150            | 25 MB    | 1     | 1    | 30 days   | no  |
| Professional   | 1,000          | 100 MB   | 5     | 5    | 180 days  | yes |
| Enterprise     | Unlimited      | 512 MB   | 25    | 50   | 730 days  | yes |

A user's effective plan is the highest of their account plan and any active license.
Jobs older than the retention window are deleted — rows and files together — on the next
upload, so no cron job is needed.

---

## Project layout

```
src/
  app/
    page.tsx                 landing
    features|how-it-works|portfolio|pricing|contact
    login|register
    dashboard/
      page.tsx               overview
      convert/               upload UI
      jobs/  jobs/[id]/      job history and detail
      licenses/              issue, edit seats/expiry, release seats, suspend, revoke
      billing/               plan comparison and change requests
      settings/              profile, password, sessions
      admin/                 users, issue licenses to users, seat rosters, messages, audit log
    api/
      jobs/route.ts          POST — run a conversion
      jobs/[id]/output/[n]/  GET  — download an artifact
      licenses/validate/     GET / POST — license checks
  lib/
    env.ts                   lazy, validated configuration
    db.ts                    SQLite singleton
    schema.sql               idempotent schema
    auth.ts                  sessions, cookies, roles
    password.ts              scrypt hashing, constant-time verify
    licensing.ts             plans, key generation, validation
    jobs.ts                  job lifecycle, storage, quota
    converter/               adapter interface + http and stub adapters
    actions/                 server actions
  components/                UI
scripts/dev-session.mjs      local-only session helper
engine/                      C# Triconex XML -> TIA Portal conversion engine
  src/TriconexToTia.Core/      domain model, options, results
  src/TriconexToTia.Parser/    tolerant Triconex XML reader
  src/TriconexToTia.Mapper/    type/library/address tables (mappings.json)
  src/TriconexToTia.Generators/ SCL, UDT, tag, import script, report writers
  src/TriconexToTia.Engine/    pipeline entry point
  src/TriconexToTia.Cli/       triconex-to-tia command line
  src/TriconexToTia.Server/    /health and /convert HTTP endpoints
```

---

## Security notes

- Passwords are hashed with `scrypt` (N=16384, r=8, p=1) and a per-user salt; verification
  is constant-time.
- Session cookies are `httpOnly`, `sameSite=lax`, `secure` in production. Only a SHA-256
  digest of the session token is stored, so a database copy does not yield usable sessions.
- License keys are generated from a 31-character unambiguous alphabet and checked for
  collisions before insert.
- Job downloads resolve inside the job's own output folder; anything else is refused.
- Rate limits: registration 5/15 min per IP, sign-in 10/10 min, conversions 60/hour per
  account, license endpoints 120/min and 30/min.
- Uploaded filenames are sanitised and extension-checked before anything touches disk.

---

## Deploying to Railway

1. **Persistent storage is required.** The app uses SQLite, so mount a volume and point
   the database and storage at it:

   ```bash
   railway volume add --mount-path /data
   railway variables set DATABASE_PATH=/data/xforcemsw.db STORAGE_DIR=/data/storage
   ```

2. **Set the remaining variables:**

   ```bash
   railway variables set SESSION_SECRET="$(node -e "console.log(require('crypto').randomBytes(48).toString('base64url'))")"
   railway variables set APP_URL=https://<your-domain>
   railway variables set ADMIN_EMAILS=you@example.com
   railway variables set MSW_ENGINE_URL=https://your-engine.example.com
   railway variables set MSW_ENGINE_TOKEN=...
   ```

3. `better-sqlite3` is a native module. Its install script is already allow-listed in
   `package.json` (`allowScripts`), so `npm install` compiles or fetches the prebuild on
   the platform without extra flags.

4. Deploy:

   ```bash
   railway init
   railway up
   railway domain
   ```

Without a volume, redeploys reset the database and any job history. Nothing else in the
app requires platform plugins.

---

## Commands

```bash
npm run dev     # development server
npm run build   # production build
npm start       # serve the production build
npm run lint    # eslint
```

The conversion engine under `engine/` builds and tests with the .NET 7 SDK:

```bash
dotnet build engine/TriconexToTia.sln
dotnet test  engine/TriconexToTia.sln
dotnet run --project engine/src/TriconexToTia.Server   # http://localhost:5080
dotnet run --project engine/src/TriconexToTia.Cli -- --input project.xml --output ./out
```

---

## Still to do

- Payment provider for self-serve upgrades (plan changes are currently recorded as requests
  and applied by hand).
- A worker queue if conversions ever need to outlive a single request.
- Per-user storage quotas beyond the per-file plan limit.
- OpenAPI document generated from the route handlers.
