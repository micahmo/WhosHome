# Who's Home

A presence board for a household. It tells you who is home, who is close by, and who is far away,
without showing anyone's location.

That distinction is the whole point. Everyone in the house can see that you are eight minutes from
home. Nobody can see where you are, because the server never keeps it.

| The board | Onboarding | Admin |
| :---: | :---: | :---: |
| <img src="docs/board.png" width="240" alt="The board: one person home, one driving in with six minutes to go, one away and parked" /> | <img src="docs/setup.png" width="240" alt="The setup page: install the app, point it at the server, start tracking" /> | <img src="docs/admin.png" width="240" alt="The admin page: household members, one with their setup link open" /> |

## What people have to install

One app: [Traccar Client](https://www.traccar.org/client/), free and on both stores. It reports a
position to your server and nothing else.

The board itself is a web page. It installs to the home screen and behaves like an app, so nobody
needs a second install and there is nothing to publish to a store.

## What each card tells you

- **Home**, **Near home**, or **Away**, with how far away they are *by road*, not as the crow flies
- **How long they have been there**, or **On the move** when they are travelling
- **How long ago their phone last checked in**, and its battery
- A bell to choose whether you are notified about that person

Notifications arrive when someone gets close, gets home, or leaves. Everyone picks who they hear
about.

## Running it

The container is published on every push to `main`:

```bash
docker run -d --name whoshome -p 5199:8080 \
  -v /mnt/user/appdata/whoshome:/data \
  -e WhosHome__HomeLatitude=42.5876 \
  -e WhosHome__HomeLongitude=-71.8140 \
  -e WhosHome__AdminToken=something-long-and-random \
  ghcr.io/micahmo/whoshome:latest
```

`unraid/whoshome.xml` is an Unraid template for the same thing.

Two settings are required and have no sensible defaults: **your home coordinates**, since everything
is measured from them, and an **admin token**, which is the credential for adding people. Without
the token the admin side stays switched off. Everything else in [Configuration](#configuration) has
a working default.

Mount `/data` on real storage. It holds the database, the keys that sign sessions, and the
notification keypair. Lose it and you lose history, sign everyone out, and break every existing
notification subscription.

Put it behind a reverse proxy or a tunnel rather than exposing the port directly. Phones need to
reach it from outside the house.

## Adding someone

1. Open `/admin` and enter your admin token.
2. Type their name and press **Add**. A setup link appears.
3. Send them the link.

The link is the entire onboarding flow. It gives them a page with their sign-in code, a button that
installs and configures Traccar Client in one tap, and a button that starts tracking. They never see
a server address or type a device id.

Links are good for 24 hours and then disappear on their own. Send a new one whenever you need to;
the old one stops working.

Treat a live link like a password. It carries the sign-in code and the phone's credential, which is
why the admin page keeps links hidden until you ask for one.

## How it works

A phone reports its position. The server works out the distance from home, stores that number, and
throws the coordinates away. Two coordinate pairs exist per person, both overwritten as they change:
the last fix, so routing has somewhere to measure from, and the spot the "been here this long" clock
is counting from. Nothing accumulates, so there is no location history to leak, and the read API
returns distances and durations only.

Distance from home decides everything: which of the three states someone is in, and which
notifications fire. When routing is available the card shows the driving distance instead, because a
straight line is not how anyone describes how far away they are. A 20 mile straight line is a 25 mile
drive.

Traccar Client stops reporting when a phone sits still, so silence is normal and says nothing about
whether tracking still works. The setup link therefore also asks the phone to check in on a timer.
Those check-ins carry no position. They only say the phone is alive, which is what separates *parked
at home* from *this phone stopped working three hours ago*. A card goes stale only when those stop
too, and says so in words rather than greying out.

For why any of this is built the way it is, see [docs/design-notes.md](docs/design-notes.md). Most
of it exists because something behaved unexpectedly at least once.

## Configuration

Settings live under the `WhosHome` section, or as environment variables with a `WhosHome__` prefix.
Blank counts as unset and falls back to the default, so a container template can leave optional
fields empty.

| Setting | Default | What it does |
| --- | --- | --- |
| `HomeLatitude` / `HomeLongitude` | unset | **Required.** Everything is measured from here |
| `AdminToken` | unset | **Required** to add people. Admin is disabled without it |
| `HomeRadiusMeters` | 150 | Inside this counts as home |
| `NearbyRadiusMeters` | 3219 | Two miles. Inside this counts as near home, and crossing in notifies |
| `OsrmBaseUrl` | unset | An [OSRM](https://project-osrm.org/) server, for driving distance and time |
| `StaleAfter` | 45 min | No contact at all for this long marks a card stale |
| `HeartbeatInterval` | 15 min | How often a stationary phone should check in |
| `MaxAccuracyMeters` | 250 | Positions vaguer than this are ignored except as proof of life |
| `MovingSpeedMetersPerSecond` | 1.5 | Above this reads as on the move |
| `MovementThresholdMeters` | 200 | How far you must go for it to count as a new place |
| `ReportRetention` | 30 d | How long stored distances are kept |
| `DatabasePath` | `/data/whoshome.db` | Must be on the mounted volume |
| `SignInCodeLifetime` | 24 h | How long a setup link and its code last |
| `MemberSessionLifetime` | 365 d | Sliding, so regular use never asks anyone to sign in again |
| `AdminSessionLifetime` | 30 d | Sliding |
| `SignInAttemptsPerMinute` | 10 | Per client, across member and admin sign-in |
| `VapidSubject` | `mailto:admin@localhost` | Contact address notification services see |
| `OsrmMaxSnapMeters` | 250 | How far a position may snap to a road before its route is discarded |
| `OsrmTimeout` | 2 s | Routing runs inline with an incoming report |
| `OsrmFailureCooldown` | 5 min | How long to stop asking after routing fails |

Defaults live in C# only. Putting them in `appsettings.json` as well would quietly override them.

## Sign-in

Nobody has a password.

**Household members** get a six digit code from their setup link, type it once in the installed app,
and stay signed in for a year of regular use.

**Admin** is a mode a browser enters with the admin token, not a person. The machine you administer
from never has to appear on the board.

**Phones** authenticate with a long random device id they never see, handed over by the setup link.

## Developing

Two servers side by side:

```bash
dotnet run --project WhosHome.Server --urls http://localhost:5199
```

```bash
npm run dev --prefix WhosHome.Web
```

Open the Vite URL rather than the .NET one. The development profile writes to `./.localdb/` and sets
the admin token to `dev-token`.

`WhosHome.Server` is ASP.NET Core: the position receiver, the read API, sessions, notifications and
routing. `WhosHome.Web` is Vite, Svelte and TypeScript, compiled to static files that ASP.NET Core
serves from `wwwroot` in production. Same origin, so no CORS, and no Node at runtime.

Simulate a phone without one:

```bash
curl "http://localhost:5199/ingest?id=<deviceId>&lat=42.5876&lon=-71.8140&accuracy=9"
```

[docs/design-notes.md](docs/design-notes.md) covers the API surface, the logging format, and the
reasoning behind the parts that are not obvious.

## Known limitations

**iPhones cost more battery than Android phones.** The check-in mechanism Traccar uses is broken on
iOS, so iPhones are configured to keep reporting rather than sleep between check-ins. Without that,
an iPhone at rest would send nothing at all, and the board could not tell a phone parked at home from
one that is switched off or out of signal. The setup page detects the platform and configures each
phone accordingly, so there is nothing to choose.

**An app update can switch tracking off** without saying so, and nobody finds out until the card goes
stale. Whether tracking survives a phone reboot is upstream's open question rather than ours; if it
does not, the person holding the phone has to start it again.

**Routing only covers the region your OSRM server was built for.** Outside it, cards fall back to
straight-line distance and drop the travel time rather than showing a wrong number.

**Traccar Client reports more often than its interval setting suggests**, an
[upstream issue](https://github.com/traccar/traccar-client/issues/198). It costs battery and nothing
else.
