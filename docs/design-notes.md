# Design notes

Why the parts that are not obvious are built the way they are. Almost every entry here exists
because something behaved unexpectedly first, and the note is the reason not to undo the fix.

## Keep the scenarios executable

Everything below about deciding presence has a matching test in `WhosHome.Tests`, and that is a
standing requirement rather than a one-off. When a new edge case turns up, add it there as well as
here: the note explains why the rule exists, and the test is what stops the next rule from quietly
undoing it.

This matters more than usual because the rules interact. They look like separate patches and are
not: each one covers a different position we can be in relative to the evidence. A fix contradicting
itself, one fix being too little to conclude anything, having no evidence at all, and being handed
evidence we were not using. A change made for one of those routinely lands on another. It has
already happened once, a day apart, and only became obvious because the second case was a phone
belonging to someone who checked.

So the bar for a new rule is that all the old scenarios still pass, and that the new one joins them.
They run in about two seconds and CI runs them before publishing anything.

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

The gap must also beat the fix's own margin of error, not just the threshold. A phone sitting
indoors at work drifted across a 143 m band on fixes accurate to 100 m, and one of them landed far
enough from the anchor to restart the clock. It discarded two hours and eleven minutes of real
dwell and reported its owner as newly arrived where he had been all morning. `MaxAccuracyMeters` is
250, so a 100 m fix is well within what we accept, and rightly — it is fine for deciding someone is
32 km from home. It is not fine for deciding they moved 200 m. Same reasoning as the speed veto
below, applied to distance: a fix that cannot resolve the question has no vote on the answer.

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

The field is also plainly wrong sometimes, in both directions, so distance gets one job after all:
vetoing a speed its own position contradicts. A fix that travelled less than its own stated accuracy
has not established that anything moved, and nothing it claims about speed is worth acting on. Two
readings from the same pair of phones, a day apart:

| Reported speed | Moved | Accuracy | What was actually happening |
| --- | --- | --- | --- |
| 0.01 m/s | 286 m | 32 m | driving away from the house on the first fix after waking |
| 21.89 m/s | 1 m | 3 m | parked at home overnight |

Only the second is caught, and only because the position contradicts it outright. The first has
displacement two hundred times its accuracy, so nothing about it is self-contradictory; that one is
handled by the debounce below instead. Real travel never trips the veto — driving steps are 75 to
90 m against 2 to 10 m of accuracy.

The 21.89 m/s reading is what motivated this. On its own it was harmless, but combined with the rule
that a dwell is only claimed when the last report said stopped, it erased the arrival line from a
stale card: the phone had been home for thirteen hours and its card could no longer say so, because
one garbage number said it was doing 49 mph. The other phone beside it, whose last fix read 0.00 m/s,
displayed correctly. Same situation, different card, decided by a field neither of them can be
trusted to fill in.

`MovingSpeedMetersPerSecond` sits above a brisk walk rather than just above a stroll, because a phone
at rest has been seen reporting 2 m/s and a lower bar flickers the label on nothing. Walking therefore
does not register as travelling, which suits a board about whether someone is on their way.

### Movement expires

"On the move" describes this moment but is read off the latest report, which may be old, so it is only
believed for `MovingClaimLifetime` after that report was received.

Without that, the bias runs precisely the wrong way. A phone stops reporting *because* its owner
stopped, so the last report of a journey is disproportionately likely to be one taken while still
rolling. Two iPhones arrived home at about 7 m/s, went quiet as iOS does when stationary, and sat on
"on the move" for thirty five minutes. The dwell line was suppressed the whole time too, since it is
hidden while someone is moving, so both cards were wrong twice over from one cause.

Android hides this rather than avoiding it: reporting every minute or two, it self-corrects within
about ninety seconds, so the flaw was there from the start and simply never visible.

The lifetime is measured from the report, not from last contact, because a check-in carries no
position and therefore says nothing about whether anyone is still going anywhere. It needs to exceed
the interval at which a moving phone reports, or a genuinely moving person would be cleared between
fixes; iOS wakes roughly every five minutes, hence six.

### A stop has to hold before it is claimed

The duration on a card waits `DwellDebounce` before it appears. Where someone is, and whether they
are moving, are reported immediately; only the how-long-for waits.

Stopping is inferred from a single fix reading slow, and a journey is full of those. Two effects
compound. The anchor resets every time someone travels `MovementThresholdMeters`, so mid-drive the
stop clock has always just restarted, which means any spurious stop looks like one that began
seconds ago and reads entirely plausibly. And a phone that has been asleep does not know it is in a
moving car: the first fix after a long silence comes back with no speed history behind it.

Both at once, observed on a card: a phone waking after fourteen hours reported 0.01 m/s at 32 m
accuracy while its owner was driving away from the house, having moved 286 m since its previous fix.
The card read "Stopped for under a minute" for fifty two seconds, until the next fix said 5.3 m/s.
The other phone in the same car, already awake, reported 6.53 m/s on its equivalent fix and read
correctly throughout, which is what makes this a property of the waking fix rather than of the
device.

Two minutes covers traffic lights and cold fixes without delaying a real arrival noticeably. It also
makes "for under a minute" unreachable, so every duration the board prints is one worth reading.

What this does not fix: a phone that goes quiet, is driven somewhere, and wakes up parked. The
anchor resets on the waking fix, so the clock starts then however long they have really been there.
One phone reported at 12:44, went silent, and reported again at 18:14 from 383 m away; its card
claimed a fresh stop for a car that had been parked five and a half hours. A debounce only delays
that number by two minutes. Knowing when a stop began that we did not witness is a different
problem.

### Running out of evidence is not the same as stopping

A dwell is claimed only when the latest report itself said the device was not moving. Not when
`isMoving` is false, which also happens when a movement claim simply ages out. Those look alike in
code and are different facts: one is a device saying it has stopped, the other is us running out of
evidence.

The difference is visible because a phone with no signal keeps taking fixes and queues them, so the
answer arrives later and can be checked against what the board was saying at the time. One evening
two phones went quiet at 26.9 km doing 19.5 m/s. Their backlog, flushed 44 minutes later, held the
fixes they had taken meanwhile:

```
18:53   26.9 km    last fix that reached the server
18:58   23.5 km
19:03   20.3 km
19:27   20.0 km    only 300 m in 24 minutes, so a genuine stop began around here
19:32   16.7 km
19:37   12.5 km    signal back, whole backlog uploaded at once
```

They were driving for ten minutes after the board began saying "Stopped". Then they did stop, but
20 km from home rather than the 26.9 km still on the card, and the duration was counted from a
timestamp nine minutes before it happened. That timestamp is the stop-clock anchor, which mid-journey
means "the last time they travelled `MovementThresholdMeters`" — a moment of known motion, narrated
as the moment they stopped.

The objection is that iOS goes quiet the instant someone stops, so the last fix of a journey is
disproportionately one taken while still rolling, and suppressing the dwell there loses the number in
the common case rather than the rare one. Two iPhones arriving home at 7.1 m/s and going quiet for
twelve hours would lose "Home for 759 minutes".

The numbers answer it. That dwell was 759 minutes and their last position was 760 minutes old. When
the last report said moving, the anchor was necessarily reset by that same journey, so the duration
can only ever be the length of the silence — which the age line already states, without claiming to
know the reason for it. What is lost is a near-duplicate.

The case where a duration carries real information is the other one, and it is untouched: a phone
reports arrival at 0 m/s, then heartbeats from the same spot for eight hours. "Home for 8 hours"
against a contact age of three minutes are entirely different numbers, and both are earned.

One consequence worth knowing: a stale card keeps its "Arrived at" only if the last report said
stopped. Where the last report said moving, there is no arrival to date, and the card falls back to
position and age alone.

### Except when the device says it stopped

There is one piece of evidence that does settle it, and it arrives on its own. The client checks in
once its own stop detection has fired, so a check-in landing well after the last position is the
device stating it has parked since. That clears the movement claim and restores the dwell, dated
from the anchor, which is the arrival.

"Well after" is load bearing, and getting it wrong shipped a bug. A waking iPhone sends a heartbeat
and a position in the same breath, and the heartbeat can land second:

```
[11:26:01] Report from Alaina: 134 m from home, Home to Home
[11:26:01] Heartbeat from Alaina.
```

That fix read 9.26 m/s. Reading the trailing heartbeat as a stop took "On the move" off a card that
had just earned it. So the gap has to exceed `MovingClaimLifetime`, which states the rule properly:
a check-in speaks for the time after we stopped believing a fix, never against one we still believe.

Worth recording how that got through. The same pairing appears all over these logs — 12:08:25,
13:05:38, 14:33:14, 18:38:42 — and each was noted at the time as evidence that the iOS heartbeat
only fires when the app wakes for something else. The observation was right there and was never
connected to the rule being built on top of it. The test that catches it now asserts the assumption
rather than the symptom, which is the kind the first pass of the suite was missing.

Without it the rule above is badly wrong for the commonest day on Android. Stop detection means the
final fix of a journey is usually taken while still rolling — a real one read 5.59 m/s pulling into
a car park — and no further position arrives until the owner leaves again. So an entire day at work
would carry no duration at all.

That case also exposes a flaw in the reasoning above. "The duration is a near-duplicate of the age"
holds on iPhones, where a silent phone's age and dwell advance together. On Android they diverge
completely: heartbeats keep the age at fifteen minutes all day while the position stays pinned at
the morning arrival, so the dwell is the only thing that would say how long someone has been
somewhere. The 759-versus-760-minute comparison that justified the rule came from iPhone data, and
does not generalise.

`LastCheckInUtc` is deliberately not `LastSeenUtc`. A fix rejected for poor accuracy is recorded as
contact too, and moves `LastSeenUtc`, while proving nothing about whether anyone stopped: a phone can
answer with a cell-tower estimate at any point in a journey, and one did so mid-drive. Reusing the
existing field would let a bad reading manufacture precisely the stop this section exists to prevent.

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

The limit is 1000 m, set generously on purpose. Its real job is catching the out-of-extract
fabrications above, and those miss by a country: the Los Angeles fix snapped 3.4 million metres. A
legitimately off-road position misses by hundreds. An earlier 250 m was tight enough to discard a
field 421 m from the nearest road, but it also blanked ordinary parked cars: a phone stopped 68 m
from an unnamed service road produced fixes whose GPS scatter crossed 250 m, so the drive-home line
flickered off and on as individual fixes were accepted or rejected. For a number that decides
nothing, a coordinate up to a kilometre from a road still gives a usable estimate, and the
fabrications the check exists to reject are three orders of magnitude further out.

Coordinates are formatted to six decimal places on the way out. This is not tidiness: OSRM answers
`{"code":"InvalidQuery"}` for a coordinate carrying a double's full precision.

### Why the nearby radius is not the distance it looks like

State comes from a straight line, but people think in driving distance, so the radius has to absorb
the difference. `NearbyRadiusMeters` was originally set to 3219, being two miles exactly, and the
result was that people read as near home while still nearly three miles of driving away.

Measured rather than guessed, by fanning sample points out in eighteen directions from one household
at a range of straight-line distances and asking OSRM to drive each of them home. Points whose
nearest road lay beyond `OsrmMaxSnapMeters`, then 250 m, were discarded, so the sample contains only
positions the app itself would have trusted a route for. From 306 accepted samples, the straight-line
distance equivalent to two driving miles came out as:

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

### Carrying the estimate through a fix that could not be routed

The estimate is display only and comes and goes with whether a fix could be routed, so one
snap-rejected fix should not blank it while nothing about where the person is has changed. When the
newest fix has no route but an earlier one taken at the same stop does, the card shows that earlier
estimate, marked with a leading `~` so a held number is never passed off as a fresh one.

The bound is the stop, not a distance. Distance from home is a radius, and two places the same
distance out drive home differently, so "they have not left this spot" is the only thing that keeps a
carried estimate true. `StationarySinceUtc` already resets the instant someone relocates, so a report
taken at or after it was taken here; the moment they drive off, none qualifies and either a fresh
on-road fix routes or the estimate honestly disappears. A second bound of `StaleAfter` stops an
ancient estimate outliving the card's own claim to be current, and the whole thing is skipped at
home, where routing never runs and a just-cleared away route would otherwise print `~3 min away` on
someone sitting in the house.

The reading behind it: the same phone parked 68 m from an unnamed service road, its fixes scattering
past the snap limit, so the "36 min away" line flickered off and on. Raising the snap limit makes that
rare; this makes the residual invisible.

Not built, and worth reaching for only if it ever bites: taking an estimate from the minute or two
before the stop as well, to cover the moment the final rolling fix of a journey snap-rejects and the
phone then goes silent, which can leave a just-parked iPhone with no estimate until it moves again. It
is the same shape as the isolated-speed-spike watch-item and deferred for the same reason: reaching
back before the anchor is the only version that can attach an estimate across a place it cannot prove,
so it waits for a real reading rather than a supposed one.

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

### A late notification should not claim to be fresh

A phone holds a background push until it wakes, so a notification routinely surfaces on unlock or when
the app is opened, sometimes hours after the event. "Just left home" arriving at dinner about a
departure at breakfast is wrong, and the server cannot fix it because it sent the push the instant the
report landed; the lateness is entirely the OS holding it.

Two different kinds of late, which want opposite handling. A push delivered promptly that then sits in
the tray until you glance at it is already right: Android labels it with how long ago it was posted, so
"Just left home" next to "3h ago" reads as a departure three hours back. The one that misleads is a
push *delivered* late: posted now, about an event hours old, which Android labels "now". So the wording
is dated only in that second case, and the signal that tells the two apart is the gap between the event
and the moment the notification is shown.

The service worker is the only thing that runs at that moment, so it decides. The payload carries the
event's own time, the reporting device's clock, which also dates a buffered upload to when it happened
rather than to when it was flushed. If that time is more than a couple of minutes behind when the
worker shows the notification, it swaps the friendly body for a dated one: "Just left home" becomes
"Left home at 8:14 AM". Fresh ones are untouched. The two-minute slack absorbs the clock difference
between the phone that reported and the phone that is looking. Deliberately not the notification's own
`timestamp`, which stays the post time: overriding it to the event time would relabel a promptly
delivered, late-read notification as hours old and break the case Android already gets right.

### The switch and the subscription can disagree

A subscription has two halves that live in different places. The browser holds one, the
`Subscriptions` table holds the other, and delivery needs both. Nothing in the UI reads the server
half: the switch reflects `pushManager.getSubscription()`, and the per-person bells are preference
rows that exist whether or not anything can be delivered. So a server that has forgotten a device
looks exactly like a working one, from every screen.

That is not hypothetical. A row went missing between 12:19 and 20:02 one day, and the board went on
showing notifications switched on and every bell lit while nothing could arrive. It cost four
announcements before anyone noticed, and only because the arrivals home were expected and did not
come.

Two changes, because a state that cannot be observed has to be made impossible instead:

- The board re-registers its subscription on every load. `POST /api/push/subscribe` is an upsert
  keyed on the endpoint, so it is cheap, and it repairs both a missing row and an endpoint the
  browser has rotated.
- Turning notifications off unsubscribes the browser first and tells the server second. A failure
  between the two then leaves the server holding a dead endpoint, which the existing 410 handler
  clears on the next send. The other order leaves a live browser subscription the server has
  forgotten, which is the unobservable state again.

Both halves are logged now. Every position report was logged and subscriptions were not, which is
why the original disappearance is undatable: there is no reverse proxy in front of this and
cloudflared does not log paths, so the application log is the only record there was ever going to
be.

### Telling a dead subscription from a living one

Reinstalling the app orphans a subscription. A reinstalled PWA is a fresh service worker with a new
endpoint, and the old row lives on because nothing reports its death: the browser does not reliably
unsubscribe on uninstall, and the push service keeps returning success for the dead endpoint rather
than the 410 the cleanup waits for. Seen directly: after one reinstall a person held two Firefox
subscriptions, the new one delivering and the old one silently taking a duplicate of every push into
a void, and a push to it still came back 201.

The 410 handler cannot catch this, because 410 never comes. The obvious alternatives do not work
either. Deduplicating at registration is impossible, because the reinstalled app has no memory of the
endpoint it replaced, so the server cannot know which existing row the new one supersedes. And a
staleness rule keyed on the board re-registering itself would delete exactly the subscriptions worth
keeping: the whole point of the app is that someone can enable notifications and then never open it
again, so "has not loaded the board lately" is not evidence of a dead subscription, it is the normal
state of a satisfied user.

The one thing that tracks the subscription rather than the person is the service worker, which runs
on push receipt whether or not the app is open. So it acknowledges: on every push it shows, it posts
the endpoint back to `/api/push/ack`, which is anonymous and keyed on the endpoint because the worker
may run with no session to present. Two timestamps record the two sides. `LastPushUtc` moves when the
push service accepts a message for a subscription; `LastAckUtc` moves when its worker acknowledges
one. A living install keeps them level. A dead one lets `LastPushUtc` pull ahead, and the gap only
grows while we are actually pushing, so a household that simply had a quiet fortnight never accrues
one.

The prune drops a subscription once that gap passes `SubscriptionDeadAfter`, checked whenever an
announcement is about to send, since that is the moment a dead one would take another push into the
void. The threshold is deliberately generous, thirty days to start. Its whole job is out-of-reach
orphans, whose gap grows without bound, so any sane value catches them; what the value has to clear
is the other direction, the longest a genuinely live worker legitimately goes unacknowledged across
sleep, dead zones and iOS throttling. Set it too tight and a real phone that was simply off for a
while gets deleted. Even that is not fatal, because the board re-registers on the next load, but it is
still worth avoiding, so the number starts high and can come down once real gaps are measured. The
right threshold is a question about device behaviour rather than a thing to guess precisely, and an
orphan lingering a few extra weeks costs only a duplicate push per event in the meantime.

The check is the gap between two recorded times, never the wall clock, which is what keeps a quiet
household safe: a subscription nobody has had reason to push to has no gap to accrue, however old its
last acknowledgement. A live worker that has been asleep or offline closes the gap the moment it comes
back and answers the queued push, so only a subscription that stays silent through an entire window of
announcements it was actually sent is taken as gone.

## Pushing the board instead of waiting for it

The board polled every 30 seconds, so an arrival could sit on a phone for half a minute before it
appeared. The symptom that makes it obvious: the notification about someone getting home arrives
before the card saying so, from the same event, because push is immediate and the board was not.

Server-sent events rather than WebSockets. The traffic is one-way, `EventSource` reconnects by
itself, and it is ordinary HTTP so the existing cookie carries the auth with no token plumbing. A
return channel would have been machinery with nothing to carry.

Three things about the design are deliberate:

**The signal carries no payload.** `PresenceBroadcaster` wakes listeners and nothing more, and each
listener then reads current state for itself. A connection busy writing therefore cannot fall behind
and start serving a queue of increasingly old snapshots. The channel is bounded at one and drops
writes, so a phone flushing forty buffered reports wakes each connection once, and the single read
that follows already reflects all forty.

**A scope per snapshot, not per connection.** These connections live for hours. `PresenceService`
and its `DbContext` are scoped, so holding one for the connection would pin a pooled database
connection all that time for a query that runs every few minutes.

**The poll stays.** It is the fallback when the stream cannot be established, and it is also what
keeps the ages on the cards moving: those are computed server-side, so without a periodic refetch a
quiet board would freeze at "2 minutes ago" indefinitely.

The stream serializes with its own options, which must include the string enum converter. Without it
states arrive as integers on one route and as names on the other, and the board renders differently
depending on which delivered the data. Measured end to end on a real page: a report reached the DOM
236 ms after it was posted, against up to 30 seconds before. A `: ping` comment goes down every
20 seconds so an idle proxy cannot quietly close a connection overnight.

## Telling the app it is out of date

A page left open keeps running the bundle it loaded with, and this one is built to be left open for
days: the screen is held awake so the board can be watched. So a deploy needs to announce itself.

The usual way is the service worker — listen for `updatefound`, prompt, `skipWaiting`, reload. It
does not work here, and the reason is worth writing down because the code looks correct. A browser
only installs a new worker when `sw.js` itself changes, byte for byte. Ours lives in `public/`,
which Vite copies verbatim, and it names no bundles because it caches at runtime rather than from a
precache manifest. So it is identical between deploys and `updatefound` never fires. Tooling like
`vite-plugin-pwa` avoids this by generating a hashed manifest inside the worker; we have no such
manifest to generate.

What does change on every build is the hashed script name Vite writes into `index.html`. The check
is therefore to fetch `index.html` and compare its module script against the one this page is
running, read from its own script tag. No server support and no build step.

It is also correctly scoped by accident. A change that compiles to identical output leaves the hash
alone, so a comment-only edit raises no prompt — verified by trying it. Nothing to update, nothing
said.

A timer is a poor way to learn this, though, and the event stream gives a much better one. A deploy
takes the container down, which drops every open stream, and the browser reconnects a few seconds
later against the new build. So a stream reopening is the earliest evidence available that the
server changed, and the check runs there. Measured end to end: the prompt appeared 1.5 seconds after
the server came back, against a five minute worst case on the timer alone. The first open is
skipped, being nothing more than the page loading.

Checks also run on returning to the app and on a timer, both gated on `document.visibilityState`, so
a backgrounded phone does not poll. Those remain the fallback for when the stream cannot connect. That gate is worth remembering when testing: an automation browser
reports the page as hidden and the check silently does nothing, which looks exactly like a bug.

## Endpoints

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `GET`/`POST` `/ingest` | device id | OsmAnd protocol receiver |
| `GET` `/api/presence` | member or admin | The board |
| `GET` `/api/presence/stream` | member or admin | The same board, pushed as it changes |
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
