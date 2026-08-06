# Design notes

Why the parts that are not obvious are built the way they are. Almost every entry here exists
because something behaved unexpectedly first, and the note is the reason not to undo the fix.

## Deciding whether someone is moving

Two separate questions get confused easily, and they need different answers.

**"Have they gone somewhere else?"** resets the how-long-have-you-been-here clock. It is measured
against an anchor: the person row stores the position the clock started from, and the clock resets
when a fix lands further than `MovementThresholdMeters` from it.

Only that. Speed deliberately does not reset the clock, though it used to. A single reading is a poor
reason to rewrite how long someone has been somewhere, and a phone lying still indoors reported 2 m/s
and discarded a clock that had legitimately been running for forty minutes: the card claimed "home for
3 min" for someone who had been home since before dinner. Where you are is durable, what you are doing
this second is not.

Comparing each fix against the previous one instead does not work. While driving, Traccar reports
every few seconds, so consecutive fixes are only 75 to 90 metres apart, well under any threshold that
survives GPS noise. A phone can travel 32 km without a single pair of fixes looking like movement.
Observed directly: a card read "stopped for 22 hours" while its owner was on a motorway, because the
clock had last reset the previous morning. Only an anchor sees a journey made of steps that are each
too small to count.

**"Are they moving right now?"** decides the *On the move* label, and is a question about speed.
Distance alone cannot answer it, because the sampling interval varies by orders of magnitude:

| Situation | Distance between fixes | Interval | Speed |
| --- | --- | --- | --- |
| Driving | ~80 m | ~5 s | ~16 m/s |
| Phone on a table | ~100 m of drift | ~5 min | ~0.3 m/s |

The distances are comparable. The speeds differ by fifty times.

The OsmAnd protocol carries a `speed` field, and `HttpUploader` in the Traccar SDK sends it when the
platform supplies one. Preferred whenever present, because it comes from the GPS rather than from
arithmetic. Measured by comparing stored values against what distance over interval would have given:
iOS supplies it on every fix, Android on roughly a quarter of them, presumably only on true satellite
fixes rather than network-derived ones. So the fallback has to exist, deriving speed from the distance
and the interval since the previous fix, which is why `Person.LastFixUtc` is tracked separately from
`LastSeenUtc`. Raw distance is the last resort, used only for the first fix after a gap where there is
no interval to divide by.

The device's figure is also the safer one during a buffered upload. A phone that has been offline
flushes its backlog in one burst, several positions carrying the same arrival second, and distance over
interval then produces nonsense: one such flush computed to 645 m/s on one row and 92,627 m/s on the
next, from a genuine drive at 28 m/s.

`MovingSpeedMetersPerSecond` sits above a brisk walk rather than just above a stroll, because a phone
at rest has been seen reporting 2 m/s and a lower bar flickers the label on nothing. Walking therefore
does not register as travelling, which suits a board about whether someone is on their way.

## Ignoring positions that cannot be trusted

Every report carries the accuracy the device claims. Anything worse than `MaxAccuracyMeters` is
recorded as contact and nothing else, exactly like a check-in.

This is not theoretical. A phone waking indoors answers with a cell-tower estimate rather than a GPS
fix:

```
03:56:04Z     49 m from home   accuracy     6 m
06:36:01Z    558 m from home   accuracy  2097 m   -> announced "left"
06:36:02Z     49 m from home   accuracy     6 m   -> announced "home"
```

A position accurate to ±2 km cannot tell a 150 metre circle from the next town, and being allowed to
try produced two notifications at half past two in the morning for someone asleep in bed. It had also
recorded 529 metres of movement that never happened, which corrupted the stop clock.

The rule worth remembering: a fix that cannot resolve `HomeRadiusMeters` must not be allowed to decide
anything about `HomeRadiusMeters`.

## Check-ins, and why they are logged loudly

Traccar Client has stop detection on by default, so a stationary phone stops sending positions
entirely. Hours of silence are the *normal* case for someone sitting at home, which makes silence
useless as a signal on its own.

The setup link therefore sets `heartbeat`, and the client checks in on a timer while it is stopped.
A check-in has a timestamp and no coordinates. `LocationFilter` in the SDK passes coordinate-less
positions through deliberately, and `HttpUploader` omits `lat` and `lon` when they are null, so what
arrives is `id` and `timestamp` alone. Staleness is therefore measured from the last contact of any
kind, not from the last position.

These are logged at information level, not debug, and that is not cosmetic. A check-in writes no
report row, and the next position overwrites the contact timestamp that would have shown one landed.
Whether they arrive is the one thing that cannot be reconstructed after the fact. Two separate
overnight investigations were inconclusive purely because the log line was at debug.

Android check-ins have been observed arriving on roughly the configured interval.

### iOS

`IosBackgroundHeartbeat` registers the identifier `org.traccar.client.heartbeat` with
`BGTaskScheduler`. The app's `Info.plist` lists only `com.transistorsoft.fetch` under
`BGTaskSchedulerPermittedIdentifiers`, and iOS rejects any identifier missing from that list. So the
task appears never to run at all.

Even if it were declared, `BGAppRefreshTaskRequest.earliestBeginDate` is a floor rather than a
schedule: iOS decides whether to honour it based on usage and battery, and Low Power Mode disables
background refresh entirely. An interval would be a hope, not a promise.

So an iPhone that settles somewhere goes quiet and stays quiet, and its card eventually reads "Phone
not checking in" while its position is perfectly correct. On iPhones the staleness warning is the
unreliable part, not the location.

That reads like a phone nobody can reach until the app is opened by hand. It is not, for the reason
below.

### Resuming after a stop, which does work on iOS

Coming out of a stop does not use the broken machinery at all. `RegionDetector` registers a
`CLCircularRegion` around wherever the phone stopped and monitors it:

```kotlin
override fun locationManager(manager: CLLocationManager, didExitRegion: CLRegion) {
    scope.launch { signals.emit(Signal.StationaryExit) }
}
allowsBackgroundLocationUpdates = true
```

Region monitoring is a separate iOS subsystem from `BGTaskScheduler`. Monitored regions persist after
the app is terminated, iOS relaunches the app in the background to deliver the event, and none of it
depends on Background App Refresh. `MotionActivityDetector` adds a second signal, watching
`CMMotionActivity` for a still-exit.

The radius is `stationaryRadiusMeters`, which defaults to **100 m** with a `stopTimeoutSeconds` of 60,
and is not exposed as a deep-link parameter. So leaving the house wakes it and pottering about the
garden does not.

The practical consequence is worth being clear about, because it is easy to get backwards: the iOS
heartbeat bug costs the **liveness signal**, not location freshness. Positions resume on their own once
someone actually goes somewhere.

### What we do about it

Positions resuming on their own is not enough, and treating the broken warning as cosmetic was a
mistake worth recording. Silence has to *mean* something. A stationary iPhone that sends nothing looks
exactly like one that is switched off, out of signal, or has had tracking disabled by an app update,
and telling those apart is the entire purpose of the staleness warning. Android has that ability
through heartbeats; leaving iOS without any equivalent gave iPhones a warning that fires when nothing
is wrong and stays silent when something is.

So iPhones get `stop_detection=false` and keep reporting instead of sleeping. `interval` becomes the
check-in cadence and is set from `HeartbeatInterval`, so both platforms check in on the same schedule
by different means. Movement still reports at `distance` regardless, because the client's filters are
an OR rather than an AND.

The cost is real and one-sided: an iPhone runs its location subsystem continuously where an Android
phone sleeps between check-ins. That is the price of being able to trust the warning, and it is worth
paying, because a presence board that cannot distinguish "at home" from "phone is dead" is lying in the
one case you would actually want to know about.

## Routing

Optional, and display only. State and notifications always come from straight-line distance, so a
routing outage changes a number on a card and never moves anyone in or out of the house.

OSRM snaps input coordinates to the nearest road in whichever extract it was built from, and returns
`"code":"Ok"` for points far outside it, describing a journey between two arbitrary snapped positions.
A Los Angeles coordinate against a US Northeast extract comes back as a confident 611 mile route. So
the status code is not the success condition: a route is trusted only when every waypoint snapped
within `OsrmMaxSnapMeters`.

That check earns its keep in ordinary use too. A position 421 metres from the nearest road, in a
field near home, was correctly discarded rather than shown as a plausible-looking route.

Coordinates are formatted to six decimal places on the way out. This is not tidiness: OSRM answers
`{"code":"InvalidQuery"}` for a coordinate carrying a double's full precision.

### Why the nearby radius is not the distance it looks like

State comes from a straight line, but people think in driving distance, so the radius has to absorb
the difference. `NearbyRadiusMeters` was originally set to 3219, being two miles exactly, and the
result was that people read as near home while still nearly three miles of driving away.

Measured rather than guessed, by fanning sample points out in eighteen directions from one household
at a range of straight-line distances and asking OSRM to drive each of them home. Points whose
nearest road lay beyond `OsrmMaxSnapMeters` were discarded, so the sample contains only positions the
app itself would have trusted a route for. From 306 accepted samples, the straight-line distance
equivalent to two driving miles came out as:

| | straight-line miles | metres |
| --- | --- | --- |
| minimum | 1.14 | 1834 |
| median | 1.47 | 2366 |
| maximum | 1.73 | 2779 |

Median detour factor 1.34. Hence the default of 2400.

Two things worth keeping in mind. No circle can be right in every direction: at 2400 m the boundary
falls at about 2.6 driving miles where roads run straight towards the house and about 1.7 where they
do not, and that spread is inherent rather than a defect. And detour factors below 1 turn up at very
short distances, which is a snapping artifact rather than a discovery, since a 250 m snap can make a
routed distance shorter than the unsnapped straight line when the whole trip is only 600 m.

The alternative, should the spread ever matter, is to hold the threshold in driving metres and keep
the calibrated straight-line figure purely as the fallback for when routing does not answer. That
would put the boundary at exactly two driving miles in every direction, and an OSRM outage would
shift it by a few hundred metres rather than by a mile. The reason classification uses the straight
line at all is that a routing outage must never move anyone between states; calibrating the fallback
is what would make a driving-based threshold safe.

After a failure, routing pauses for `OsrmFailureCooldown`. That must comfortably exceed the interval
between reports, or the cooldown expires before the next one arrives and every report pays the
timeout anyway.

## Sessions

`GET /api/admin/session` is a question, not a protected resource, so it always answers 200 with
`{"admin": true|false}`. Refusing with 401 when the answer is simply *no* put a failure in every
ordinary member's network log on every page load. Everything that reads or changes something still
refuses, so 401 keeps a single meaning: an attempt was rejected, never that a question was answered
no.

The board asks on every load even when a member session exists, because being a member and being an
admin are independent. Skipping the check for signed-in members is what once made the admin page
unreachable from the board on the one browser most likely to be both.

Sessions are cookies with sliding expiry. The Data Protection keys that sign them live next to the
database so a container update does not sign the household out.

## Setup links

Minting a link sets two things: a six digit **code** and an unguessable **token**, both valid for
`SignInCodeLifetime`.

Signing in consumes the code and leaves the token alive until it expires, so someone can reopen their
setup page to redo their phone without another link being minted. When the token expires the link
disappears from the admin page on its own, because `/api/people` only returns `setupUrl` while it is
live. Nothing is deleted.

The page behind a live link hands over the sign-in code and the device id, so it is a credential. The
admin page keeps links collapsed until asked for, and a collapsed link is absent from the page rather
than hidden with CSS.

There is no way to revoke a link early. Replacing it mints a new one, which invalidates whatever was
already sent.

Minting a link touches `LoginCode`, `LoginCodeExpiresUtc`, `SetupToken` and `SetupTokenExpiresUtc` and
nothing else. `DeviceId` is assigned once when a person is created and never reassigned, so re-sending
a link cannot break a working phone or orphan its history.

## The device page

`/device` lets a signed-in member re-apply the current recommended settings to their own phone, and
switch tracking back on, without an admin minting anything.

It exists because both of those things otherwise need an admin. The settings worth recommending change
as each platform turns out to behave differently, and without this the only way to update an existing
phone is to mint links and chase everybody. Separately, an app update can switch tracking off silently,
and recovery is the `action/start` deep link, which nobody could reach on their own.

`GET /api/device/config` takes the person from the session claim and accepts no person parameter, so
there is no shape of the request that returns somebody else's device id. Verified: a query string or
path segment naming another person is either ignored or 404s, and an admin session without a member
session gets 401 rather than an arbitrary person's config.

Worth noting for perspective, since this looks like new exposure and is not: `/api/setup/{token}` hands
the same device id, plus a sign-in code, to anyone holding an unguessable URL with no session at all,
and that URL gets pasted into messaging apps. A cookie tied to one person is the safer of the two.

Both pages build the link through `TraccarConfigLink.Configure` rather than each assembling their own.
They must not drift: a phone that took its settings from one page and its updates from the other would
end up in a state neither page believes it is in. Sharing the builder is the whole guard, since there
is no test suite in this repo to catch it if they diverge.

The page never claims an update is needed, because nothing here can read the settings the app currently
holds. It offers to apply them, which is harmless to repeat, and shows when the phone was last heard
from as the only honest feedback available.

## Ordering

People appear in the order they were added. `Person.SortOrder` holds the position, seeded from the row
id, with new people appended past the current maximum. Alphabetical ordering meant someone added
today could land in the middle of the list, which is disorienting exactly when you are looking for the
person you just added.

`PUT /api/people/order` takes the complete order and rejects anything that is not a permutation of the
household. A partial list would leave the people it omits sharing positions with the ones it names,
and the result would depend on the id tiebreak rather than on anyone's intent.

Dragging uses pointer events, not the HTML5 drag API, which never fires on touch. The handle sets
`touch-action: none`, without which the browser claims the gesture as a scroll and no `pointermove`
ever arrives. The insertion point is judged against the *other* rows' midpoints rather than the
dragged row's own: measured against its own, the row lands under the pointer and immediately reads as
belonging one slot further on, which oscillates. Rows vary in height by a factor of three when a
setup link is open, so nothing here can assume a uniform row. Arrow keys on the handle do the same job
without a pointer.

## The client setup link

Traccar Client accepts configuration over a custom URL scheme. The parameter names are `url` and `id`,
not `serverUrl` and `deviceId` as various forum posts claim.

The tail differs by platform, decided from the `User-Agent` of whoever opens the setup page, which is
the phone being configured:

```
# Android and everything else: sleep between check-ins
...&id=<deviceId>&accuracy=medium&distance=75&interval=300&heartbeat=900&stop_detection=true

# iOS: keep reporting, because its check-ins do not work
...&id=<deviceId>&accuracy=medium&distance=75&interval=900&stop_detection=false
```

Sniffing a user agent is normally a poor idea. It is defensible here because the cost of being wrong
is low and self-correcting: the deep link only functions on the device that opens it, and a fresh link
fixes a phone that got the wrong one. iPadOS in desktop mode reports itself as a Mac and would be
misread, which is acceptable for a device unlikely to be anybody's tracker.

Verified against `ConfigurationService.applyUri` and `Preferences` in the current Flutter client, not
the older native Android one, which has neither `heartbeat` nor `stop_detection`. `heartbeat` is in
seconds and defaults to 0, meaning off, so it has to be set explicitly. `stop_detection` defaults to
on.

Settings apply live, so re-sending the link to an already configured phone is enough to change them.

Any host works except `action`, which is reserved. `org.traccar.client://action/start` and
`.../action/stop` toggle tracking with no confirmation dialog. Applying settings does not start
tracking, so both links are needed.

The same parameters work in a QR code. If the scanned URI uses http or https, the client takes the
server URL from the link's own origin and path.

## Logging

One line per entry, through a small `ConsoleFormatter`:

```
[14:22:20 INF] Program: Report from Rosa: 32724 m from home, Away to Away, device clock ...
[14:22:07 INF] Program: Heartbeat from Rosa.
[10:49:11 WRN] Program: Rejected report for unknown device id.
```

These are read through `docker logs`, where the default formatter's two lines and fully qualified
category per entry bury the message. Categories collapse to their last segment, so an entry from
somewhere unexpected stands out instead of blending in. Exceptions keep their own lines, since folding
a stack trace onto one would make it unreadable.

The rejected-report warning deliberately omits the device id. It is the phone's credential, and a log
file is not where it belongs. The cost is that two rejections cannot be attributed to the same device;
a short prefix would fix that if it ever matters.

## Notifications

Web push to the service worker. The VAPID keypair is generated on first run and stored next to the
database. Subscriptions are tied to the public key, so replacing the pair forces everyone to re-enable
notifications.

Announcements fire on state changes only, never on the repeated reports that arrive while someone sits
still:

| Transition | Push |
| --- | --- |
| Away to Nearby | "X is near home" |
| Nearby or Away to Home | "X is home" |
| Home to anything | "X left" |
| Nearby to Away | "X is away" |
| Unknown to anything | silent, first report |

The state is called `Nearby` in code and shown as "Near home" wherever a person reads it. "Nearby"
alone invites the reader to think it means near *them*, wherever they are, when everything here is
measured from home.

## Endpoints

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `GET`/`POST` `/ingest` | device id | OsmAnd protocol receiver |
| `GET` `/api/presence` | member or admin | The board |
| `POST` `/api/session` | sign-in code | Start a session |
| `GET` `/api/session` | member | Who am I |
| `DELETE` `/api/session` | none | Sign out |
| `POST` `/api/admin/session` | admin token | Enter admin mode in this browser |
| `GET` `/api/admin/session` | none | Is this browser an admin; always 200 |
| `DELETE` `/api/admin/session` | none | Leave admin mode |
| `GET`/`POST` `/api/people` | admin | List and add household members |
| `DELETE` `/api/people/{id}` | admin | Remove someone; their reports cascade |
| `PUT` `/api/people/order` | admin | Set the display order; takes every id exactly once |
| `POST` `/api/people/{id}/code` | admin | Mint a sign-in code and setup link |
| `GET` `/api/device/config` | member | The caller's own phone settings; never anyone else's |
| `GET` `/api/setup/{token}` | setup token | What the setup page shows someone |
| `GET` `/api/push/key` | none | Public VAPID key, public by design |
| `POST`/`DELETE` `/api/push/subscribe` | member | Manage this browser's subscription |
| `GET` `/api/notifications` | member | Who I hear about |
| `PUT` `/api/notifications/{id}` | member | Change that |
| `GET` `/health` | none | Liveness |

`/ingest` is the only unauthenticated write path and the only endpoint no browser ever calls, which
makes it easy to miss when auditing from the frontend bundle. It is also the most security-relevant
thing here: anyone holding a device id can post positions as that person.

Unknown device ids get 400 rather than 404 so that a buffering client discards the report instead of
retrying an id that will never be valid.

## Local development gotcha

If `wwwroot` is deleted after a build, `dotnet run` and `dotnet ef` both fail with a
`DirectoryNotFoundException`: the static web assets manifest in `bin` still references it. Rebuild the
frontend, or clean `obj` and `bin`.
