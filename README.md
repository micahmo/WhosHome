# Who's Home

A household presence board. It shows who is home, who is nearby, and who is away, without
showing anyone's actual location.

Household members install exactly one app: [Traccar Client](https://www.traccar.org/client/),
which is published on both stores and reports to this server over the OsmAnd protocol. The
viewer is a web app that installs to the home screen, so there is no second app and nothing
to publish.

## Design

The server computes distance from home when a report arrives and stores that. Only one raw
coordinate pair exists per person, on the person row, overwritten on every report, so the
database is structurally incapable of accumulating a location history. It is kept because
routing needs an origin to compute travel time from. The read API returns distances and states
only, which means no client has a code path capable of rendering a map.

The last known state is always shown, however old it is. Traccar Client stops reporting when a
phone is stationary, does not resume after a reboot, and can be silently disabled by an app
update, so a report older than `StaleAfter` is flagged with `isStale` and its `age` for the UI
to present as history rather than as current fact. Only a person who has never reported at all
reads as `Unknown`.

## Layout

`WhosHome.Server` is an ASP.NET Core app: the OsmAnd receiver, the read API, and session auth.
`WhosHome.Web` is a Vite + Svelte + TypeScript app that compiles to static files. In production
ASP.NET Core serves those files from `wwwroot` on the same origin as the API, so there is no
CORS to configure and no Node at runtime.

## Running locally

Two servers side by side. The .NET one:

```bash
dotnet run --project WhosHome.Server --urls http://localhost:5199
```

And the frontend, which proxies `/api` and `/ingest` across to it:

```bash
npm run dev --prefix WhosHome.Web
```

Open the Vite URL, not the .NET one. The development profile writes to `./.localdb/whoshome.db`
and sets the admin token to `dev-token`.

Add a person, which returns the generated `deviceId` used as the ingest credential:

```bash
curl -X POST http://localhost:5199/api/people -H "Content-Type: application/json" -H "X-WhosHome-Admin-Token: dev-token" -d "{\"name\":\"Micah\"}"
```

Simulate a report:

```bash
curl "http://localhost:5199/ingest?id=<deviceId>&lat=43.0731&lon=-89.4012"
```

Mint a sign-in code for that person and type it into the web app:

```bash
curl -X POST http://localhost:5199/api/people/1/code -H "X-WhosHome-Admin-Token: dev-token"
```

## Configuration

All settings live under the `WhosHome` section and can be overridden with environment
variables using the `WhosHome__` prefix.

| Setting | Default | Notes |
| --- | --- | --- |
| `HomeLatitude` / `HomeLongitude` | 0 | Must be set, or everyone reads as very far from home |
| `HomeRadiusMeters` | 150 | Below ~150 m this flaps, since the client's default distance filter is 75 m |
| `NearbyRadiusMeters` | 3000 | Boundary between nearby and away |
| `StaleAfter` | 45 min | Older than this is flagged stale; the state is still shown |
| `ReportRetention` | 30 d | How long derived reports survive |
| `DatabasePath` | `/data/whoshome.db` | Must be a mounted volume in the container |
| `AdminToken` | unset | Admin endpoints are disabled entirely when unset |

## Client setup link

Traccar Client accepts configuration over a custom URL scheme. Verified against the client
source, not the forum posts, which get the parameter names wrong.

```
org.traccar.client://configure?url=<urlencoded server /ingest URL>&id=<deviceId>&accuracy=medium&distance=75&interval=300&stop_detection=true
```

Any host works except `action`, which is reserved. `org.traccar.client://action/start` and
`org.traccar.client://action/stop` toggle tracking with no confirmation dialog, which gives the
onboarding page a working "resume tracking" button.

The same parameters work in a QR code. If the scanned URI uses http or https, the client takes
the server URL from the link's own origin and path.

## Endpoints

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `GET`/`POST` `/ingest` | device id | OsmAnd protocol receiver |
| `GET` `/api/presence` | session | The board |
| `POST` `/api/session` | sign-in code | Start a session |
| `GET` `/api/session` | session | Who am I |
| `DELETE` `/api/session` | none | Sign out |
| `GET`/`POST` `/api/people` | admin token | Manage household members |
| `POST` `/api/people/{id}/code` | admin token | Mint a 15 minute sign-in code |
| `GET` `/health` | none | Liveness |

Sessions are cookie based, single-use codes, and last a year with sliding expiry. The Data
Protection keys that sign those cookies are written next to the database so a container update
does not sign everyone out.

## Known gaps

Sign-in codes are six digits with no attempt throttling, so they are brute-forceable in
principle. They expire in 15 minutes and are single use, which makes this acceptable for a
household app but not for anything wider. Rate limiting is worth adding before this is exposed
to the open internet.

The schema is created with `EnsureCreated` rather than migrations. That needs to change before
there is a database anyone cares about.
