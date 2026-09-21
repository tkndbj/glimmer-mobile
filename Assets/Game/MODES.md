# Modes — what a mode is, and what a mode costs

Companion to `CLAUDE.md`, which holds the cross-cutting invariants (content and ids, the save and the
merge, the store, what a stranger can see, the UI kit, tasks, the season, the streak, the update wall and
reminders). This file holds everything about *a way of playing*: the entry tests, grading, fail states, the
modes that were withdrawn, the hidden ones, and Thornwatch, the live mode.

**The same rules apply here as in `CLAUDE.md`.** Each invariant is a rule and, where it is not obvious, the
failure that bought it. **Numbers are permanent** — code comments cite them (`invariant 37bl`,
`invariant 20j`) — so an entry may be compressed but **never renumbered**. Spent mode, chapter and level
ids are in `CLAUDE.md`'s table. Drawing detail belongs in `Assets/Game/CRAFT.md`, not here.

## What a mode is, and what a mode costs

20. **A mode is code, and a chapter names one.** Content can never add a way of playing, but a chapter says
   which mode it belongs to, so a drop ships a whole second game with no app update.
20a. **A second mode's level is an ordinary level, and that is why it cost nothing.** A whole second game
   added **no save schema version, no reward retune, no `firestore.rules` change and no server work**; the
   two things that differ are *order* and *unlocking*, per-mode in `CatalogIndex` alone. **The bargain
   collects in both directions** — six modes have been deleted for the same price.
20b. **A mode may be a whole screen.** What it may share is the *world*; what it must share is everything
   about being a **run**, reached through the same Domain classes rather than copied.
20c. **A level carries a board or one of the mode blocks, never neither.**
20d. **A level authors no numbers at all.** A typed par is the failure with no symptom.
20e. **Where a mode's material never decays, an *ordered* queue is what makes it a puzzle.**
20f. **A mode is hard when its constraints cannot all have their way at once, and that is one number over
   the whole board, never a bar on each element.**
20g. **A mode may bring a rule no board can demonstrate, and the fix is to make the board demonstrate it,
   not to explain it better.**
20h. **A chapter's mode is derived from its levels, never typed — and the build gate proves it.**
20i. **A mechanic that moves the *floor* moves par, the ladder's yardstick and the top of the star ladder
   with it.** The difficulty reading has to follow too, which is easiest to miss.
20j. **Three tests any new mode has to pass before a level is authored.** The answer has to be visible on
   the board **now** (adding readouts is the wrong fix); and a finite board with no refill must not be able
   to freeze — ask whether every legal input strictly moves something that only goes one way, which gives
   *cannot stall*, *always ends* and *the search terminates* at once. The monotone quantity is often the
   thing being *added*.
20k. **A mode may be built to be *easy*, and then two rules invert**: `ways` flips and `greedy` flips and
   becomes the bar. Nothing about money or grading flips.
20l. **"Brain-dead" is a property of what the player has to *work out*, not of how hard the board is** — a
   mode tuned three times toward chill still read as hard, because the match was invisible until you made it.
20m. **A mechanic the author places is one the player finds; a payoff has to be one they made.**
21. **A chapter is opened by stars, and only its first level asks.** Authored **per level** rather than as
   a total, because a chapter is not a fixed size. `NextToPlay` must return the **furthest unlocked** level;
   the victory panel's Next asks `IsUnlocked`; a chapter opening is a **transition**, measured either side
   of the record fold; and every screen prints the **count**.
22. **A puzzle is graded on the puzzle, so there is no clock anywhere in this game** — a countdown prices
   deliberation, which is what every mechanic here exists to force. **Three lines, even thirds of one
   slack**: three stars at **1.20**, two at **1.40**, the run ends at **1.60** — and **they must move
   together**, or a band becomes unscorable with every number plausible.
22a. **A mode with no turns is graded on the count it does keep, never on how fast it was.**
22b. **A mode's fail state is a budget in the unit it is graded in.** Undoing frees the *ground*, not the
   resource; resource spent is the grade, not units occupied.
23. **A lost run may be bought back, and the offer comes before the accounting rather than on top of it.**
   **It cannot inflate a reward**: stars are held against par, so a run at its fail state scores **one star
   at most**. A continue that does not continue is a charge, so the shortfall is cleared first and a mode
   that cannot be rescued answers `NoContinue`.
23a. **A lost run has two prices and they buy different things** — the *run* (one star at most) and a
   *heart* (the board is rebuilt and graded like any other), on different panels in a fixed order.
23b. **A mode whose fail state is not the counter it is graded on has to buy that promise back**, because
   stars derive credits and credits reach a public board.
23c. **A repeatable price has to end the ladder by arithmetic, or the fail state stops binding.** It
   **doubles**, as one recurrence with two parameters, never two escalation rules. A factor below a hundred
   is refused and clamped to flat; the ladder tops out at the gem ceiling, and both gates **print the ladder
   and say where it stops**. Every branch that cannot climb further answers at once, or a lost run never
   gets a defeat panel.
24. **A run is free when it teaches nothing new, and the rule lives in one predicate** — the opening (the
   first few levels of the first chapter of each mode) and the replay (a level already **cleared**, not
   attempted, for ever).
24a. **A run is charged when it ends and gated when it begins, so every door has to ask.** A run may only
   begin if the player could pay for it if it went wrong — an invariant that lived inside one screen.
24b. **A refusal over a live run brings the shelf to the player; only a refusal with nothing behind it may
   navigate.** The free way, the paid way with the shelf stacked, a countdown, and KEEP PLAYING under both.
25. **A variable reward the client shows must be one the server can recompute** — eight equal slices, each
   a multiplier on that placement's own amount, so it costs no schema version, no merge rule and no claim
   work. The slice is a pure function of (account, day, spin index).
26. **A mode that cannot be lost is a prototype.** No fixed future → no search → no par → no star line →
   no budget → no fail state.
26a. **A documented chain that does not exist and cannot** — a wave counter, a rising pitch and a chain
   multiplier were dead code against a rule that rejects them (5d, where nobody thought to count).
26b. **Two fail states, and only one may be sold a continue**, read in one predicate.
26c. **A procession must carry all three channels** — supplying only what the board is missing is wrong by
   one step and can produce a board that can be neither won nor lost.
26d. **Par may be resolved lazily**, and the memo is safe to race on because the function is a pure search
   over a frozen board. **Lazy is not free**: the validator warns above 40,000 positions, refuses above
   120,000.
26e. **A well's room to err is a count of drops, never a multiple of par.**
26f. **A payoff handed out for free cannot be a payoff.** "It made the game much easier" and "the animation
   is too weak" are one fault.
26g. **A mechanic that delivers light competes with the lens, so the next one had to change *which colour
   is travelling*.** A mechanic with no event of its own passes every numeric reading, because a decoration
   passes all of them.
26h. **A mechanic can have an event of its own and still be decoration; what separates them is whether the
   player *decides* anything about it.** **Ask: what does the player decide, and can they be wrong?**

## Modes that were built and withdrawn

Sixteen modes were built here and one ships. Each id below is kept because comments cite it; the mode
narratives are gone. **Withdrawn:** 28 Groovekeeper (every reading good, the board played as arithmetic),
29 five modes commissioned at once and judged by playing them, 30 Deep Orbit and Moonwake (*not enough on
the screen*), 31 Nova Raid and Toppleglen (every gate green, not the *fresh* thing the slot was for), 32 The
Iron Quarry (withdrawn without being played), 33 Hollowmarch, 34 Emberforge, 35 Kindlewake (the commission
named a *verb* and what was delivered invented a different one). **Removing four of five cost the save file
exactly what adding them did: nothing** (20a).

28f. **The proof that a board is lost never ends a run, and only decides whether it would be honest to sell
   one.**
29a. **Modes share a level *shape*, not a rule** — the grid, search, budget, verdict, run, validator, view
   and screen are each written **once**.
29b. **Breadth-first searchable because monotone, and that is the entry test.** The first layer holding a
   finished board is par.
29c. **A companion is on every board, their part is fixed by the level, and that is what keeps par honest**
   — an ability varying with the *worn* companion would vary par and both star lines per player.
29d. **A prototype is refused if a greedy player can finish it.** `careless` is a warning; `ways` is pinned
   for the direction nothing else sees — a rule change that makes a board *easier*.
29e. **A mirror can disagree silently, and a fixture that needs the Editor compares nothing.** Every
   `*VectorTests` reads JSON through `JsonUtility`, so the offline runner skips the whole fixture — the one
   gate nobody runs on the way past.
29f. **A predicate about the *ground* must not answer a question about the *state* standing on it.**
30d. **A level may speak, and every line is a loc key authored in content** — the one place a key is
   written down rather than derived.
30e. **A backdrop belongs to a mode's *world* as well as to a level's place.** The map is not part of it.
30g. **A `MonoBehaviour` that hides itself must not disable the object it needs to be alive on** — Unity
   silently refuses to start a coroutine on a disabled behaviour. Alpha for the look, `blocksRaycasts` for
   the input, and the object stays awake.
30h. **A modal sets `Time.timeScale` to nought, so a screen coroutine waiting in *scaled* seconds never
   finishes while a lesson is up.** Use real seconds.
30i. **A recorded turn says where a piece was *immediately before* an event**, and the cells between two
   events carry no beat at all. Nothing else here can see that fault, because every gate reads the model.
32b. **A goal has to be the most legible thing on the board, and no numeric gate can tell you it is not.**
   **Approximating a goal out of scenery is how a goal comes to look like dressing.** (The standing blind
   spot: no gate here opens a PNG.)
32c. **What a preview may show is geometry, and never outcome.**
32d. **Every board is dealt by seed into a designed template and kept for what it asked**, and the seeds
   are recorded, so a board can be re-derived rather than only re-typed.
33a. **The allowance is drawn on the board**, and the number in the corner is the same number.
33b. **A goal that can leave the board makes a board that can be neither won nor lost.** A goal that *jams*
   is what lets `Stranded` honestly answer false.
33c. **Ask which quantity is monotone** — it is not always the thing on the board.
33d. **The chain clears the same threshold again on every wave**, so the mode's best move and its reward
   are the same move.
33e. **A special the player makes must differ in *kind* from an ordinary move, not in degree.**
33f. **A mechanic's reading is taken over the shortest answers, never over the opening move.**
33g. **Where a fact can be derived from a shape it can never come apart from it** — a road drawn as a
   winding rail has its order read off the drawing, and a fork is refused **by cell**.
33h. **A repeated tile is drawn full-bleed** — inset and rounded, forty road tiles read as separate sockets.
33i. **Three shipped rungs is a test, not a chapter.**
34a. **A refill is the one thing a searchable mode may never have.**
34b. **The chain runs through the player's own work and stops the instant it reaches material that is not.**
34c. **A finite board has two fail states and only one is the meter.** `life` plays the most extravagant
   possible player; `careless` asks whether thoughtlessness *wins*.
34d. **Before pinning a difficulty reading, ask whether two moves can reach one state.** `ways`
   double-counted at every depth on every board, and par never moved, which is why nothing else saw it.
34e. **A goal has to win a legibility fight against four jewels, and only a render can say whether it does.**
34f. **A board's pieces differ in silhouette as well as in hue.**
35b. **A pruning rule that is sound for the search can still delete the mode's fail state.** A no-op rule
   must refuse only what genuinely changes nothing, never what merely fails to help.
35c. **`life` is the longest play, not the greedy one** — and a mode where nothing is consumed needs no
   `life`. **A check that could only ever answer yes is not a check.**

## Hidden modes *(code and chapters still stand; `"disabled": true`)*

The glade (`c01`–`c04`), Lightfall (`f01`–`f03`) and Prismvale (`p01`) are hidden, not deleted.

36. **Prismvale** — drag a gem onto its neighbour and the two swap; a lantern feeds gems of its own colour,
   the colour runs on through matching neighbours, and a critter against the vein wakes.
36a. **Nothing is ever removed, and that decides everything else** — light is read off the arrangement, so
   a vein is **breakable** and the board's key is its **cells alone**.
36b. **It does not pass 20j's second test, and the honest statement is narrower**: the goal count is
   monotone, the arrangements are finite, and a legal move always exists.
36c. **The fail state is the meter and nothing else**, so `Stranded` is about the layout, not the run.
36d. **A critter wants light and not a colour** — the lantern already decides the colour.
36e. **The reading that judges a board is `used`; the one that catches the silent fault is `dealt`.**
36f. **`careless` says nothing while par does not exceed the goal count.**
36g. **A lantern that reads as a gem is the one confusion this board cannot afford** — a gem moves and a
   lantern never does. Caught by the render alone.
36h. **A permanent highlight rather than an effect, and its arrival is a *run***, named in flood order.
36i. **Two levels is a test, not a chapter.**

**The glade's vocabulary.** The wheel is paint, not light — the middle channel is drawn yellow, while
`Energy` still mixes by `|` over three bits. One verb (turn a conduit, light a critter) with modifiers and
no second solver: `~` brittle stone, `!` rooted (authored at `/0`, 5c), `&A` taproot, `=NS+EW` crossing,
`%NS+EW` briar. A **pocket** is a heart and a critter behind a ford standing on a *cycle* of the network.

## Thornwatch — the live mode

Raiders come down a hill at the ward line; match gems and the colour you matched fuels the **ward** of that
colour, which looses bolts until its fuel runs out; the run ends when the last ward falls. Entries below
marked *(art)* are one-line pointers — the working detail is in `CRAFT.md`.

37. **Two proven loops bolted together with one thing changed: nothing on the jewel board is a goal**, so a
   match is only ever worth the *colour* it was.
37a. **A mode may run on a clock, and what that costs is exactly one thing: par stops being a proof.** Par
   is arithmetic (hill health over the most one match could deliver) and the opening answers **null**. It
   is a calibrated estimate, not a floor.
37b. **The fail state is the ward line, so there is no move allowance at all** — a budget would be a second
   fail state counting down to an ending that never happens. `budgetFactor` is -1 on every siege. A fallen
   line reads as `Stuck`, so no continue is offered there.
37c. **Fuel used to fade on a clock and does not** — a meter draining while nothing happens reads as the
   game taking something away. Removing it fell out in three places at once, none where the rule was.
37d. **A level authors what is standing there and what is coming, and no numbers at all** — everything else
   is one constants file, so moving one constant moves every star line in the mode at once.
37e. **A refilling field is dealt deterministically, and is dealt again rather than allowed to lock**,
   because this clock does not stop.
37f. **A raider says its colour three times and none is enough alone.** **Before drawing a rule in colour,
   count how many ways it is said.**
37g. **Three bands is a board no gate can look at, and a render moved two of them** — the readout every
   decision rests on was behind the field's plate.
37h. **Only a ward coming down flashes the screen** — a flash on every blow is five a second.
37i. **What a played run still has to answer**: does fuelling a colour read as the verb; is par a line a
   good run can get under; does a cog read as an upgrade; do the bosses read as different fights?
37j. **A mode with no search needs somebody to play it, so one is written down.** The hold simulation
   asserts the hill is cleared with the line standing **and visibly damaged** — a line nothing reaches is a
   fail state that rejects nothing. **Every rule change goes back through it.**
37k. **A wave comes on a clock *or* the moment the hill is empty, and it took both.**
37l. **`Image.color` is a multiply, so a tint can only ever darken** — tinted wards carry a baked hue
   rotation. A tint says which of several things this is; it does not make something look lit.
37m. **"Has fuel" reads as brighter, never "no fuel" as dimmer.** A light goes *up*.
37n. **A toast grows to fit what it says, and renders its markup.** A Unity `Text` that overflows is not
   clipped and nothing says so; the label turns rich text off, correctly, since a name is a string another
   player wrote. Both faults were in shared code.
37o. **A siege says the word on the board, because its board does not stop**, and the clock runs under the
   opening countdown so the count tells the truth.
37p. *(art)* **A hue rotation goes *most* of the way, never all of it**, and the blend is circular.
37q. **Two sounds a frame apart are a flam, not emphasis.**
37r. **A mode's defeat is its own piece of news** — its own ordinal, because analytics cannot tell two
   endings apart afterwards. **And the panel says it, not the board.**
37s. **A move's *effect* may not land before its animation does.** Turn-based modes are immune by
   construction; a clock is not.
37t. **The last wave is a boss, and what a level authors about it is one token** — which wave it is in is a
   rule, so it can be neither typed into the middle nor left off the end.
37u. **A boss is drawn big, and three of the four things that make it read are *placements* no gate can
   look at**: several cells tall, standing still, health across the top, and a spell that is a different
   *kind* of object rather than a bigger bolt.
37v. **The header counts waves, not wards.** **What a mode owes the corner is the thing its board cannot
   say.**
37w. **A ward can be upgraded, and the mechanic is one question about the *line* rather than the hill** —
   five tiers, each +10% damage and −10% fuel, multiplying. A cog taken by a fallen or maxed ward is spent
   for nothing and the view draws that by having it go nowhere. The first neighbour in cell order wins a
   contested cog and is *stated*, because a `HashSet` walk is not promised to enumerate the same way twice.
   **Ten per cent is only exact because every damage number was multiplied by ten.**
37x. **A second boss is one token, four constants and three art reels**, and the ordinal is **appended**.
37y. **A readout belongs where the thing it is about is** — and what stopped it moving was the layer, not
   the number.
37z. **A boss is a way of fighting, and telling two apart by a hue is not telling them apart at all.** What
   separates them is what each one *takes*: fire, health, the whole line at once, or the rank they earned.
37aa. **`PerfectMatch` is an identity nobody had written down** — it read `gems x damage x 2`, which holds
   only while a gem buys one bolt. Left alone it would have doubled every par in the chapter, green.
37ab. **A rung is fought over a ground of its own** — arithmetic on the level's place in its chapter.
37ac. **"Boring bosses" was a complaint about the drawing, and the answer was to *spend* the window rather
   than shorten it.** The cadence is a rule, so not one number moved.
37ad. **A boss that cannot bring a ward down is not a wave of its own; it rides the last one.**
37ae. **Every turret a player buys throws an effect of its own**, and **two turrets sharing an ability are
   two readings of it, never one shape in two tints**.
37af. *(art)* **Baked VFX are authored to be seen through bloom.** **Before tuning a bought effect, check
   what the artist expected to be applied to it.**
37ag. *(art)* **A projectile can be right while the flash and impact it names are wrong.** `BoltScale` is
   the one place a projectile may differ in size.
37ah. **Which turret throws the elemental set is a decision, and the day it moved it stopped being
   derivable** — a field names it, never spelled `IsStarter` (16j's trap).
37ai. **A turret's effect and the name over it may be swapped; its id may not.** If a name keeps its rung,
   the price and ability must move too — a balance change, not a re-skin.
37aj. *(art)* **When a fix is a recipe, grep for every caller that builds one.** The anchor was then wrong
   again in the one way the eye could not see: right shape, wrong sign.
37ak. **The gem is what a colour *is*, and everything else agrees with it** — agreement was bought by
   construction and stopped being true the day the gems started being cut.
37al. **A shelf belongs to the display and its cells to the safe area**; reading those as one thing put a
   band of nothing at the foot of every iPhone.
37am. **A request that names a removal and a replacement in one breath needs one question: which of them
   does the replacement attach to?**
37an. **`ProtoView` sizes its plate to fill its host *exactly*, so a host inset is a hard edge, not a
   margin, and nothing says so** — two thirds of every caption was behind an opaque panel since launch.
37ao. **A field that has gone quiet points at a match**, three times a level and never while anybody plays.
37ap. **"Super fast paced" is two dials, and only one may be turned on its own** — the cadence is the
   line's *output*, so the bolt got twice as heavy in the same breath and par came back bit-identical.
37aq. **The mode's one instrument was a coin toss, and it had been green by luck** — two player rhythms
   twenty milliseconds apart play out completely differently, so the sweep runs nine.
37ar. **What a one-pack cast costs is a thing to say out loud rather than discover later.** *(The survey
   behind this was wrong about everything except the insects — 37db.)*
37as. *(art)* **An offset is a fact about what the body is** — a side-view shadow ratio is wrong for a
   top-down body.
37at. *(Withdrawn; see 37db.)* **A sprite can be baked instead of bought, which turns the camera angle from
   a purchase into a decision.** The technique is sound and the premise was not.
37au. *(art)* **Envelope, never stretch** — so the art overhangs and needs a clip of its own.
37av. **The ground is a plain grid of small tiles**, after five rounds of answering a complaint by being
   *more designed*. The instruction was four words: *do not try to design anything*.
37aw. *(Superseded by 37av.)* **Where two normalisations decide whether one thing reads against another,
   the invariant is the gap, and a written-down absolute is a gap nobody is checking.**
37ax. **The shelf is ordered by how much of the hill an ability reaches, and it shipped the other way up.**
   Multi-target abilities are dearest, **and the two halves of the shelf must agree**.
37ay. **An effect belongs to a turret and an ability to a rung, and after three rounds of tuning those
   stopped lining up — which is fine, and had to be said out loud.** Moving an effect costs no re-bake.
37az. *(art)* **"Two of a thing" is only two if the gap beats the size of the thing, measured against what
   is *drawn*** — and a second barrel is drawing only, keyed on the rung.
37ba. **An exception to the colour rule is affordable when the rule is said somewhere else.**
37bb. **A turret carries a bolt weight and a toughness, and the two are not symmetrical: one may only ever
   go up and the other is the trade.** Par is health over `PerfectMatch` against the **baseline** bolt, so a
   turret hitting under it would push three stars out of reach of whoever bought it.
37bc. **A shelf is read in three bands, and a band is a label on an order that already exists** — it keys
   on the shelf rung, nothing may key on it, and the grid is laid out by a **cursor**, not `i / Columns`.
37bd. **A boss may not be a raider drawn three times the size, and a chapter may not be half boss rungs** —
   two a chapter, on rungs five and ten, with the verbs dealt one each across twenty rungs.
37be. **"Hard with the default turret, doable with a little better" is a measurement**, and making it one
   meant playing a *chosen* line for the first time.
37bg. **A star count is the rarest thing in this save: a stored number that may be stored**, because an
   upgrade cannot be undone, so the join is a per-key `max`. **Before storing any count, the only question
   is whether it can fall.** Keyed on the **holding**, not the turret.
37bh. **An upgrade is three screens, and the middle one exists because a decision needs both numbers.** The
   payoff is the numbers moving, so the numbers are the ceremony.
37bi. **An accessibility rule is about what is on the board at once, so what invalidates it is a change to
   *who draws it*.**
37bj. **A count-in is part of the run, so it is paced by the run.** `RunHold` stops the board's clock and
   cannot stop a **wall clock**.
37bk. **A lesson goes when the board demonstrates the rule the first time it is met.** The `Mechanic` member
   is kept, because a lesson id travels in the save.
37bl. **A turret only ever fires at its own colour, and every other rule here is a consequence.** Reported
   as *I barely look up*: four wards firing at once made "take the biggest match" correctly optimal, and the
   clock punished deliberation.
37bm. **Two correct branches whose union leaves a state with no answer** — a panel offered one key and chose
   what it said, and every turret starts at one star, so a twenty-turret shelf could not be reached at all.
37bn. **A latch meaning "the board is resolving" must not take the player's hands off everything else on
   the screen.** `Busy` is a fact about the *field*.
37bo. **A siege's model resolves in an instant while its drawing runs on**, so "the run is over" and "the
   screen has finished saying what happened" are two moments here and nowhere else. Judging returns while
   `Busy`, and both endings are held.
37bp. **A run says which level it is, and it is the map's number and not a new one** — display only, so an
   unknown level draws nothing. Written once on `RunScreen`, because there are two headers.
37bq. **A boss is answered by the whole line, and its colour decides the *double* rather than the
   permission** — a boss is what a ward shoots when it has nothing of its own left to shoot, last. **And a
   part-weight bolt has to cost a part of the fuel**: a half-weight shot at full price is the player's fuel
   converted at half rate on their behalf, and it cost three runs in ninety on a change meant to help.
37br. **Two bosses a chapter against a fixed set of verbs is a chapter budget** — the mode supports
   **verbs ÷ 2** chapters and then needs code (`NoBossVerbIsSentByAnyTwoChapters`).
37bs. **A boss that *adds* to the hill has to be capped, and the cap is what keeps par computable** — a
   fixed group a fixed number of times, **par counts all of them whether raised or not**, and the counter
   lives on the *caster*. `EndangersTheLine` stopped being readable off the damage column.
37bt. **A boss may take what the hill *owes* the player.** It **wants a crowd**, so it rides the last
   authored wave and the validator refuses a rung that deals it nothing to eat. **The view has to own the
   removal**, or a player whose ranks vanished has met a bug rather than a boss.
37bu. *(art)* **A cast of five bodies says the *kind* plainly and stops pretending to say the colour.**
37bv. *(art)* **A second reel per raider costs a *frame*, not a texture** — the swing is cut on a bigger
   canvas at the walk's scale and the view reads the ratio off the two sprites.
37bw. **The shelf is ordered by reach, so "further up" is not "stronger"** — one rung up held 72 of 90 and
   two rungs up 47. The chapter's gate plays the starter and one bought line.
37bx. **A boss body has to survive the board, and "projects sideways" is necessary and not sufficient.**
   Candidates are surveyed at the board's camera **and on the hill at true relative scale**, and **a slim
   body needs a bigger number than a wide one**.
37by. **A chapter's raiders carry more health than the chapter before it, and that is the only lever a
   fifth and sixth chapter has — because the hill fills up.** What it really costs the player is the clock.
   Derived from the ordinal and written into the body, and carried **on the board** rather than looked up,
   because the hold simulation builds its layouts from an inline table. **Health only; a blow is never
   surged.** The first two chapters are the baseline and do not move.
37bz. **A tenth of health is a long way** — the same ten rungs read 54 of 90 unsurged, 28 at one tenth more,
   5 at three tenths. The lever is a **cliff rather than a slope**, and **it does not touch the grade**.
37ca. **An ability that does what the seat beside it already does is decoration** — a prism firing at the
   next colour was answered at full weight by the neighbouring seat, and the built-in roster read its
   magnitude as tenths while `progression.json` authored a count of colours (5b's shape). Both rungs carry a
   **stun**, **refused while it is running** where every other lasting state is refreshed, because a stun
   taking the longer of two would hold a colour off the hill for the whole run.
   <br>**And a siege authors its own star lines, because its par overstates.** Bombs, cogs, an overcharge
   and the elemental double all pay more than the formula credits, so real runs come in *under* par — what
   shipped was a ladder with one rung, which is 5d asked of the grade. Factors are authored **per chapter**
   from that chapter's own sweep, and the gate asks for a *share* rather than a count.
37cb. **A tougher chapter grades easier unless its lines move with it** — par scales with a surge and a
   run's matches do not. **Every chapter's star lines are set from its own sweep.**
37cc. **Every screen keeps its size in units on a tablet; a board laid out to the *width* does the
   opposite.** The extra width was bought to buy height and is not the board's to spend. Three costs: **the
   gate is *the ceiling never binds*** rather than a band width; **a tool whose job is proportions has to be
   able to draw every canvas the game produces** (`--tablet`); and the caption note had it backwards.
37cd. **A charm is a power riding on an ordinary gem, and that is why it cost the match rule nothing** — it
   keeps the cell a gem and sits in a **parallel array** moved through `Collapse` and `Settle` in lockstep.
   Three, each taking a different thing: a **prism** decides a colour, a **lance** a piece of the board, a
   **stormglass** a moment on the hill.
37ce. **Dealing a charm costs no extra draw, and that is the whole of what let it ship** — a second
   `Next()` would have dealt a different gem into every column of every rung. Which charms a level deals is
   content; **how often** is the mode.
37cf. **A charm is never authored into a cell** — it would be a payoff its author placed, and one more thing
   the settled proof would have to know about.
37cg. **A free payoff that does not scale with the line flattens the shelf, and it is measurable.** **Before
   adding anything free to a mode with a shelf, ask what it is worth to somebody who has bought nothing.**
37ch. **"The shelf is worth N runs" stops being measurable once the starter is near the ceiling, so it is a
   share** — and the second half is **three-stars**, because runs held saturate and grades do not.
37ci. **A deterministic stream turns "rare" into "never" on some board, and averaging cannot see it.** One
   rung's first charm fell at deal 351 against a run ending at ~324, with a fixture over twenty thousand
   deals finding the rate exactly right. **Where a stream is deterministic, a probability is a promise to
   somebody and a lie to somebody else — ask for a bound.**
37cj. **A multiply mixes upward, so a slice of a product is only safe at the top.** `SiegeBoard.Avalanche`
   (lowbias32) is what a second decision out of one draw goes through.
37ck. **A wall is a universal and nine samples cannot establish one.** A suspected wall is re-asked at four
   times the resolution; only the suspect pays for the second sweep.
37cl. **A lesson about a dealt thing is raised when the board settles, never when the thing is minted** —
   the anchor refuses a gem that is not at its own centre, and a kind with nothing to ring is **held over**.
37cm. *(Withdrawn with the bake.)* **A body that ever *stops* needs two reels, and the bob is a gait rather
   than life.** "Differs from a photograph" and "reads as moving" are two bars.
37cn. **A payoff is a sequence, and a charm was four frames pretending to be one.** The one finding that is
   not about art: **`ProtoView.Shockwave`'s third argument is a *scale* and every call passed it
   `Cell * n`**, so every charm flashed the whole screen — **a wrong *unit* in a drawing is invisible to
   every gate**, because the number is plausible and the picture is never opened.
37co. **Where the thing made more frequent is a free payoff, the rate is swept against the gates and the
   wall is sharp** — eight steps is the whole margin. **And the payment does not work**: the first two
   chapters sit against *the line is never reached* rather than against a fail rate, so **a lever that
   priced a chapter sharply when measured prices it at nothing once free power has pushed it to the
   ceiling.**
37cp. **A window bounds the gap between two events and says nothing about the gap before the first.** *I
   never see them* came back twice at two different rates, which is the tell that the number being moved was
   not the one at fault. **A fixture that walks thousands of deals off one board cannot see this at all.**
37cq. **A mode with a clock can bend it, and the one place that is affordable is where the model is handed
   the seconds** — `SiegeView.Dilate` scales what `Advance` is given, so hill, wards, muster and fuel slow
   together, free, because the model advances by *delivered* seconds. **What is forbidden is a hold on the
   drawing alone**, which leaves the clock running and makes difficulty a function of an animation constant.
   A **wind-up may not be dilated** when what it waits for is booked in model time, and the hold is a
   **deadline, not a duration**.
37cr. **Anything that kills a crowd in one instant and draws it over a second needs the corpses claimed**,
   in its own claim list, or two effects drop each other's bodies mid-flight.
37cs. *(art)* **A sprite's alpha profile decides what it can be, and no amount of scaling changes it** — a
   filament is not a bar. **And the render mirror reads the thicknesses off the view rather than typing
   them.**
37ct. **A terminal reading has to be monotone, or it is not an ending — it is a coincidence somebody has to
   be looking at.** `_felled == RaiderCount` was exact until a boss *made* raiders: the tally crossed while
   the boss stood, and the view (which may not judge while the field is coming apart) missed the crossing,
   so the run never ended at all. **Before trusting a measurement, ask what the thing being measured counts
   as finishing.**
37cu. *(Withdrawn with the bake; the measurement is in `CRAFT.md`.)* **A walk reel has to *stride*, and
   "differs from a photograph" cannot see the difference** — what separates them is vertical.
37cv. **A tell drawn for one spell is not a tell for "everything that is not aimed"** — a ring behind
   `if (!aimed)` silently collected the two crafts added after it.
37cw. **Two boss verbs is a chapter's price, and 37br's bill came due exactly where it said it would.** A
   **shackler** takes the line's *time* — the deliberate inverse of a douse, and the two must not be folded
   into one field, because a douse is answered by pouring more in and a shackle **banks**. An **ironclad**
   is 37bq read backwards. **Neither touches par**, which is what made both affordable.
37cx. *(Withdrawn with the bake.)* **A bought body ships unarmed, and the socket is the seam** — the pack
   authored the sockets, so identity is the right transform. **Gear is fitted before the skin is built**, or
   it renders and never reaches the measurement that frames the shot.
37cy. *(art)* **A swing must share a pixel scale with its walk and must not share a canvas.** The square is
   capped at the importer's 512.
37cz. **A cast is told apart by its bodies or it is not told apart at all, and the lane one tap away
   counts.** The Infinite lane is the one deliberate exception — a medley of all four chapter casts.
37da. **The cheapest rung of a shelf may be close to the worst answer to a chapter, and a gate that plays
   only it is measuring the wrong thing** — an *ability* answers a *material*.
37db. **A survey that is written down is not a survey, and this one cost two casts, three boss bodies, an
   Editor tool and 24 MB of models.** **So the survey is a command** — `make_siege_art.py --survey`. Three
   further findings. **The number that decides is the *shortest* body's upscale, deshadowed**; **rejected on
   a measurement rather than on a look is the only kind of rejection that stays rejected.** **Facing
   outranks sharpness** — every cast this game ships is square-on, the most visible property a cast has and
   the one never written down, and **no resolution recovers a body that is facing the wrong way.** And **a
   pre-rendered PNG is somebody else's export decision, not the art**: this pack shipped `.ai` vectors and a
   Spine rig beside the small PNGs, so frames are re-baked at whatever height the board wants
   (`Tools/spine_bake.py`). **Look for what the pack was exported *from*.**
37dc. **A boss's spell is a drawing of its own, and sharing one is invariant 37z's fault said about the
   picture.** Three of the eight wore the warbringer's two reels under colours of their own — a gravemaw
   and a bonecaller are *grounded*, which is a fact about the rule and never about what the spell looks
   like. Every gate was green, because a shared address is real, registered, audited, loaded and drawn.
   What sees it is a **collision**: `SiegeArtTests.EveryBossSpellIsItsOwnDrawing` walks
   `SiegeMode.Bosses` and refuses two bosses naming one picture.
37dd. **A `switch` on the caster is the same trap as a `switch` with a live `default` (44e), and four
   bosses paid it.** Only four of the eight had an arm in `SiegeView.Unleash` and `Aftermath`; the rest
   fell into the overlord's, so two bosses that throw *nothing* launched the overlord's pair of rockets
   at themselves, drawn as `Art.Glow` because `SpellArt` correctly answered null. **A clause naming one
   of a family and letting the rest fall through is a clause that has already gone stale** — ask
   `SiegeTuning.AimsAtAWard`, never a list of kinds.
37de. **A reel has to be a *picture*, and that is measurable.** `Tools/verify/fxreels.py` measures every
   baked reel's lit box against its own frame; of 266 on disk three were slivers and all three were boss
   spells (`snare` at **2.1 %** across, an arrow two hundredths of a cell wide on the board). **Framing is
   a fact about the source**, so it is a field on the row (`Shot.Comet`) rather than one policy for a whole
   table — the same move `Shot.Toward` already made. **And a gate can only ever prove a reel is a picture,
   never that it is the right one**: `Hit_Cube02` passed it at 26 % and is a stack of white boxes.
37dg. **Fixing every particle seed makes a bake reproducible in its *particles* and says nothing about
   its *pixels*.** `Spawn` has cleared `useAutoRandomSeed` for chapters, and `Verify Siege Projectiles` was
   still failing on correct art: four of this pack's seven shader graphs scroll off a **Time** node, which
   in the Editor is the wall clock, so every bake rendered the same particles through a texture at a
   different phase — a mean of **12.8 levels** on a ward's bolt against a tolerance of six. A scroll moves
   *everywhere at once*, so no tolerance can separate it from a real change. The bake pins `_Time` to the
   effect's **own** simulated clock (`Clock`), advancing with the particles rather than frozen, and hands
   it back in a `finally`. **"Hands it back" has to mean restoring what was there**, and the first cut
   wrote *nought* under a comment asserting the engine rewrites `_Time` every frame so nothing was worth
   saving. Measured: set `_Time` to a sentinel, read it back a frame later, the sentinel is still there.
   Unity does not put that global back — so the bake was pinning every scrolling material in the project
   at time nought and walking away, which is the exact failure the comment described while dismissing it.
   **An assertion in a comment is not a measurement, wherever it appears.**
37dh. **A derived float that decides a pixel must be quantised, and the tell is drift that *grows with the
   frame index*.** Two reels survived the clock fix. `Sample` reads `Renderer.bounds` after simulating and
   a GPU need not land that on the same last bit twice — harmless anywhere else, fatal in two places: the
   speed a comet is flown at **multiplies distance travelled**, so the ironclad's blade drifted 6.2 levels
   at frame 3 and 8.5 by frame 17, monotonically; and the aspect handed to `Pixels` is quantised to
   sixteens, so a reel sitting on a boundary changes **texture size** and reports as 255/255, which reads
   as the art having been replaced. Both are rounded to hundredths now — far finer than the bands they sit
   in, so nothing about the framing moves. **The signature is the shape of the drift, not its size.**
   **Quantise as finely as the noise allows, never as coarsely as it tolerates.** The first cut rounded to
   *hundredths* — ample for a last-bit wobble, and ten times coarser than needed, which pushed eight
   shipped frost turret reels across a boundary in `Pixels` and drew every one of them **14 % longer**: a
   change whose entire purpose was to stop framing moving, moving framing. Thousandths absorbs the same
   noise and perturbs the value a tenth as far. **It was not, however, what moved the frost reels** — that
   was the clock pin itself: a different scroll phase is different *rendered content*, so the measured
   shape genuinely differs and eight reels land on the other side of a 16-pixel step. Diagnosed, changed,
   re-baked, hypothesis wrong. The granularity rule above stands on its own merits; the frost reels are a
   real consequence of determinism and a taste call for the owner (`Tools/siege_frost_rebake.png`). **It is still a mitigation and not a proof** — rounding
   moves the knife edge rather than removing it, and the cause of the wobble in `Renderer.bounds` is
   unknown. What makes that acceptable is that the bake is *provably* repeatable (three runs agreeing to
   the decimal) and `Verify` now says so loudly.
37df. **A wake is a direction and a burst has none — and the cheap-looking one was the expensive one.**
   A boss's orb dropped its trail through `Burst.Sparks` every 50 ms, which builds four GameObjects a
   call and pools none: **108 of them inside 0.45 s** for a warlord's three orbs, to draw a scatter of
   dots. **The cost lands on the canvas, not on the objects** — every `Image` added or re-tinted re-meshes
   its whole canvas, and this one carries the ground, the line, the field and a dozen raiders. Pooled
   embers (`SiegeView.Cinder`) and **a nested `Canvas` on the effects layer** fix both halves; the canvas
   costs one batch and `overrideSorting` must stay **off**, or the layer leaves the parent's draw order.
37di. **A boss is a fight, and a fight is a promise the rules keep, not a number the line races.** Eight
   bosses shipped dying on the walk in or before their first spell — a fed line lands ~230 a second on a
   lone boss and the charges banked through the quiet before one deliver more in an instant than any boss
   stood with — and every gate was green because none measured a boss's life. So a boss walks on
   **untouchable** and opens a **stand** the frame it plants. A stand is a **floor** under its health
   (`SiegeTuning.PhaseFloor`: thirds from the top, integer arithmetic) that lifts when the stand has done
   what it is for — its opening spell landed *and* `PhaseLeast` held, `PhaseMost` the deadline — and the
   last stand's floor is **one** until then, because the third with no threshold under it is the one a
   banked line could otherwise end the instant it opened. Three stands, each **faster**
   (`PhasePaceHundredths`, never under `FastestCast`). 5d asked of a finale: what it rejects is *kill it in
   one dump*, which was the only arrangement anybody was playing. **Par does not move**; the fight is
   entirely what the sweep sees. **The gate is `SiegeRuleTests.EveryShippedBossRungIsAFight`**: on every
   rung that sends a boss, at every rhythm, a boss that fell threw at least a spell a stand and stood at
   least ten seconds, and it fell at some rhythm; the same file holds the rules one by one, and **a new
   chapter is added to `ShippedChapters` in the change that ships it** — Dustcrown shipped six rungs and two
   bosses that no fixture in the mode ever fought.
37dj. **Every point of harm goes through one door** — `SiegeBoard.Wound` — because eight sites wrote
   `Health -= damage` themselves and new rules in eight places is eight ways to disagree. It answers what
   *really* came off, which is what a utility is charged against (39), so a firepot dropped on a boss
   resting on its floor costs the run nothing and is handed back. **A boss that cannot be hurt is not a
   target** (`SiegeRaider.Impervious`, the one reading): `Aim` and `Furthest` skip it, so a ward banks (the
   ironclad's answer made general) and a charge tapped over nothing else is refused and kept. **And a
   phase turns in `Fights` and nowhere else** — it used to turn inside `Wound`, which opened a phase from
   the middle of a firepot's call stack; the floor holds the boss whether or not anything is reporting, so
   the turn can be one reading of health against a threshold, taken once a step.
37dk. **A boss's state is read, never announced.** A phase turns on a bolt inside `Advance` and just as
   readily on a firepot, a bomb or an overcharge outside it, where nothing is reporting — so the view
   compares `Phase` with what it last drew, once a frame (`SiegeView.Fight`), and only the opener is an
   event (`SiegeCast.Opens`). A view that waited to be told would miss half the turns.
37dl. **A floor in front of a spell that will never be thrown is a wall.** A bonecaller with its raises
   spent settles its stand at once (`SiegeBoard.Settle`); one that finds nothing to aim at settles at the
   deadline; a stun runs the stand's clock down like any other second. And **only the opener sunders**: a sunder on every cast was bounded only by the
   overlord's life, and once it lived long enough to fight it stripped every rank and the duel ran six
   hundred seconds without ending. The finale's blow went 5 → 4 in the same change, measured
   (`AnUnhurriedPlayerHoldsThisLine`: Thornwatch 78 → 84 of 90, the finale 3 → 6 of 9 with the line bled
   to 0–16 of 56).
37dm. **The fight is drawn on its edges, and slow motion is the one instrument on every beat** — the
   plant, the roar on each turn (louder each time), the landing, the
   fall — all through `Dilate`, affordable only because the model is handed the seconds (37cq). The bar is
   cut into phases and fills on the walk in; the boss has a voice of its own for arriving, roaring and
   falling (`boss`, `roar`, `felled`), and the synthesised cast gesture is a **strike whose peak is the
   release**, not a sine that is back on the stand by the time the spell leaves — and **which frames sit at
   the peak decides the canvas**, so the re-cut left three slim bosses filling 3–5 % less of their frame and
   `TallOf` moved by the measured ratio (the mirror's rows, three of them drifted, were re-read off the C#
   in the same change). **What only a render could see**: the guard ring at 14 of 128 was a purple wall a body wide and 7 read as a halo — the whole widget is withdrawn now (37ei), and the reading is kept because it is the one thing a render answers about any ring this mode ever draws again.

37dn. **A boss comes in alone, wears no colour, and every one of its spells takes health** — the owner's
   verdict from the first played build of the fight, and four rules follow. **Alone, on both sides**: a boss is always a
   wave of its own; `Muster` holds the clock for it until the hill is cleared, and **holds every wave
   behind it while it lives** (`SiegeBoard.BossStanding`, one reading of the hill rather than one rule per
   lane, so an authored ladder and a derived endless ramp inherit it without being taught). Only the first
   half was ever written down, which was enough for a chapter — a chapter's boss rides its last wave, so
   nothing was ever scheduled behind one — and was no rule at all on the Infinite lane, where the schedule
   carried straight on and a warbringer spent the back half of its fight inside the next wave's escort — so the three that rode a wave for company
   no longer do, an escort on the Infinite lane is nought, and a rally over an empty hill charges nothing
   (the warbringer's roar is its four-ward smite now; the charge is decoration, accepted). **No colour**:
   every ward reaches every boss at full weight (`BossReachTenths`), no ward is doubled against one, the
   ironclad's aegis no longer locks three wards out, the crown wears no gem, and the token's letter is
   worn by nothing but a bonecaller's raised creepers (the layout's colour check skips a boss; the aegis
   colour error is gone). **Twice the health** — four times was the ask and was measured first: four wards at full weight are
   1.6 times the damage a line landed before, not four, so fourfold made a 90-second duel of 35 spells
   that lost every finale; twice keeps the sweep where the phased fight left it, with the overlord's
   blow at two and the warbringer's roar at two a ward (a roar over an empty hill charges nothing). **Every spell smites**: the four that took none (`Blight`,
   `Gravemaw`, `Bonecaller`, `Shackler`) take two on top of their verb, a devour and a raise land theirs
   on the freshest ward, and every landing is drawn as a hit with the verb's own drawing over it — which
   is what "I watched him attack but my turrets didn't take any damage" was (the blightcaller, by
   design, until now). `EndangersTheLine` is true of every boss and `Threatens` follows.

37do. **A fifth chapter costs what a fourth did, and the bill is written in two places: two boss
   verbs (37br) and one charm (`SiegeCharms.Upto`).** Paid on 2026-09-16 for both at once, because
   the fourth chapter had shipped dealing three charms where the ladder said four. **The two new
   charms take the two things the first three left**: a **furnace** hands the *line* a charge (one
   banked overcharge on the ward of its colour, refused on a fallen or full ward and the refusal
   drawn), and an **hourglass** takes the *hill's time* (three seconds standing still while the line
   fires; the wave clock is deliberately not held, or an empty-hill hourglass would buy a quiet). Both
   are booked to land with the match's own fuel (37s), a second hourglass **extends rather than
   stacks**, and both scale with the line without a number - a charge is that ward's capacity at that
   ward's weight, a stopped hill is worth what the standing turrets land in it (37cg). **A guard runs
   down through a stopped hill** (37dl). Adding a charm is: a member appended to `SiegeCharm`, a row on
   `SiegeCharms.Roster`, a case in `SiegeBoard.Spring`, a stone in `make_siege_art.CHARM_GEMS` and
   `SiegeMode.Cast`, a face in `SiegeView.CharmFace` and `render_siege.CHARM_ART`, a `Sprung` arm, a
   `Mechanic` and its `Taught` arm, and two loc keys - and every chapter body at or past that ordinal is
   re-dealt, because the deal picks uniformly over the set it names.
37dp. **The ninth thing a boss could take is the *charge*, and it is the one resource the player holds
   by choice.** A **thunderer** drains the ward holding the most banked overcharges and lands each one
   back as `ThundererDrain` more off it, so the tell is an invitation to throw: a charge thrown lands
   on the boss at full weight, a charge held lands on the line at two a piece. It is 37di read from
   the other end - banking through the quiet before a boss is what killed eight of them on the walk
   in, and this is the boss that makes it the wrong idea. **Bounded by arithmetic**: at most
   `ThundererCast + 2 x MostCharges` in one blow, held under half a ward by a fixture. **The hold
   simulation cannot see the verb**, because the model player throws every charge the moment it is
   armed; what it measures is the smite, and the fight gate is what proves the boss fights.
37dq. **The tenth is the player's *hands*.** A **colossus** buries a ward under `RubbleTaps` pieces
   of rubble: the ward keeps fuel, rank and charges and cannot fire, and a second boulder goes
   elsewhere - a burial is a bind bought back with attention, which is the one currency this mode's
   clock prices (37bl). **But a burial that only hands could lift was a stalemate, and that is the
   one failure a verb may not have**: a line nobody dug went quiet post by post, and with nothing
   left to bury the boss held its cast (`CastRetry`) - reported from play as *I cannot shoot and he
   does not attack*. So a piece weathers off every `RubblePiece` seconds and a pile is gone inside
   `ColossusBury` (**four**, the owner's figure), a boulder is **refused on a post already buried**
   rather than re-setting its clock, and a cast that finds every post buried **throws at one
   anyway** - the burial is what a colossus cannot repeat, never the blow. **Damage and cadence
   moved together** (2 every 6s to **4 every 7.5s**, the owner's reading that it hit far too
   lightly): at 4 every 6s the finale fell at none of nine rhythms, which is the wall
   `EveryShippedBossRungIsAFight` exists to refuse; at 4 every 7.5 it reads where it shipped (2 of
   9). **A dig is answered on the
   call and never on the next report** (the overcharge's shape): a tap lands between two steps of
   the clock and `Advance` clears the report on its way in, so a record written by the tap would be
   gone before the view read it - which is exactly how the first fixture failed. **The model player
   digs a post clear in one beat** (three taps is under a second for a thumb) - measured one piece
   a beat it never matched again once the boulders started and the finale read as a wall no player
   would meet; measured one post a beat the finale fell. And the rubble is a **cut rock**, drawn
   three times at three sizes, never three discs (47i).
37dr. **The fifth cast is seven of the fifteen unit bodies, and the eight left are the sixth
   chapter's.** The wild: two stone golems (bulwarks), a yeti and a minotaur (brutes), a mud clod
   (creeper), Zeus (the thunderer) and a cyclops (the colossus) - every one looked down on and walking
   toward the player, cut with the packs' own attacks on the walk's scale (37bv). Uncut and spoken
   for: the skeleton knight and archer, Medusa, Horus, the pharaoh and the three wizards.
   **The Infinite lane's medley was re-dealt over five casts** so every chapter cast still lands on it,
   and a fifth ordinal draws `map1` again - the map wraps at four paintings (`mapart.map_of`), so a
   fifth chapter cost no map cut.

37ds. **A bolt anchored near its head may not be drawn at full length before it has travelled.** A
   comet's reel is anchored at `SiegeView.HeadAt`, so three or four cells of trail hang *behind* the
   anchor - and the anchor is the barrel mouth. Drawn whole on the frame it is fired, every shot in this
   mode painted its tail straight back down through the chassis and out under the ward line, and what a
   player reads is the whole lit shape: a turret with a flame through it. Reported in one sentence -
   **they do not come out of the barrels**. The trail is now **as long as the flight so far**
   (`SiegeView.Emerged`), which is the honest rule rather than a tuned ramp and needs no constant per
   turret: at the muzzle there is a head and nothing behind it, and the trail unrolls out of the barrel.
   The preview stage and `render_siege.py` carry the same rule, because a turret judged on the loadout
   panel has to leave its barrel the way it does on the hill.
37du. **Unroll a trail by *cropping* it, never by scaling it — and four turrets is how that was
   learned.** The first cut squashed the reel toward its head, which is invisible on a symmetric column
   and mangles anything drawn off the centre-line: a chain that forks sideways, a shot that zig-zags
   between bounces, a meteor trailing sparks and a corona with rays across it all dragged their tail
   art up beside the head and read as a **squiggle at the barrel**. Exactly those four were reported and
   the other six were not, which is the tell. `Image.Type.Filled` crops instead - it draws a quad off
   the sprite's outer UVs, so it is safe on these reels' tight mesh - and a crop distorts nothing.
   **Every borrower of a pooled widget gets `Simple` back**, or a flash inherits a half-drawn frame.
37dw. **The tail end of a trail is what parks at the barrel, so it has to be the *quietest* part
   of the reel — and two rounds of fixing the geometry could not fix art drawn the other way up.**
   The crop draws a reel from its head down to however far the shot has flown, so the far end of
   what is drawn sits at the muzzle for the whole flight. Measured, two of the ten legendaries were
   **wider at the tail than at the head** (a jet and a meteor, both drawn widening away from the
   head the way real exhaust does) and **all ten were fully opaque there** — so what lay across the
   turret's shoulder was a wide, flat-cut, full-brightness slab. That is what "a weird tail at the
   very first moment" was, and no amount of moving the cut could have removed it. Trails now
   **narrow and fade** toward the tail (`make_legend_fx.TRAIL_WANE/TRAIL_FALL`), which is what a
   comet does anyway. **Check the far end before adding a bolt: it is the end the player sees
   parked on the turret.**
37dx. **Two builds said the fix was not landing and the build was not the problem.** Before
   assuming a stale APK, `global-metadata.dat` inside the shipped `.apk` carries every method name
   — searching it for the symbol that was added settles in one command whether the code shipped.
   It had; the geometry was right and the art was wrong, and a third round was spent because the
   obvious explanation was never tested.
37dv. **A crop is a straight edge, so three things conspire to hide it.** Measured, every reel in this
   mode was opaque along its whole trail - there is *no* depth at which a cut falls on faint pixels. So
   `HeadRoom` is a distance in **cells** tied to the muzzle flash's own reach (its ink covers .58 of a
   cell below the barrel) rather than a multiple of the bolt's width; **`Flash` is drawn after the
   bolt**, which is the honest order anyway; and in the art a trail now **wanes** along its length
   (`make_legend_fx.Sheet.wane`) instead of stopping dead at the frame edge. A halo centred on the head
   must also end above where the cut lands - `make_legend_fx.HALO_FLOOR`, and six of the ten were drawn
   wider than that and were trimmed. **Check that before widening any glow on a bolt.**
37dt. **The origin was never wrong, and only a measurement could say so.** `_lineY + Cell` sits on the
   barrel mouth of all thirty turrets to within a hundredth of a cell - measured off the sprites rather
   than argued - so the obvious fix (move the muzzle up) would have made it worse. **And the first probe
   written to look at it was wrong in both directions**: `HeadAt` and `MuzzleAt` are measured from the
   frame's **bottom**, and a probe that read them from the top drew the bolt three cells high and the
   flash a cell and a half low. It happened to point at the same conclusion, which is the dangerous
   case. 44d, met from a third direction: a mirror written to diagnose one fault will invent another.

37dy. **The one charm whose whole payoff is a moment was drawn with a borrowed sprite.** An
   hourglass stops the hill for three seconds, and what announced it was the stormglass's `laser`
   stretched to the width of the board - a bar is flat at full alpha across its middle and has no
   leading edge, so what crossed the hill was a blue stripe sliding past. It is `beam`'s own lesson
   (a still gradient has no event in it) arriving on the most expensive beat in the mode.
   `make_siege_art.stillwave` is a **front**: a hot edge, graduations hanging off it at a fixed
   pitch and shards crystallising behind, so time visibly *freezes across* the hill rather than a
   shape being flown over it.
37dz. **A wave says something arrived; only a clock says time stopped.** So the stop now hangs a
   **dial** over the hill for the whole window (`SiegeView.Dial`, `make_siege_art.stilldial`) -
   a graduated ring with the **hourglass itself** inside it, which is the shape of the stone the
   player just matched, set inside a graduated ring with a **running minute hand**. It arrives
   turning, holds, and **breaks** when the sand runs out, because a charm that ended by fading
   would end by omission.
37ea. **It was drawn frozen first, and the argument for that was sound and the drawing was
   dead.** A ticking clock is a clock that is working, and what the charm has to say is that it is
   *not* - so the first cut held the hands still and let a glint travel the rim. The owner's
   verdict was that it could not be *felt*: a still face over a still hill is a decal. A minute
   hand sweeping a full turn a second, with the smear that rate needs or it reads as teleporting,
   is the only thing plainly moving on a stopped board - which is the job the motes were added
   for. **`STILLDIAL_FRAMES` and the sweep are one fact**: the hand turns exactly once across the
   reel, so a loop never shows a seam.
37eb. **"More white, denser, let me feel it" is a note about *alpha profile*, not about size.**
   The wavefront was a feathered edge over a haze and read as something sliding past; what it is
   now is a **face** - flat at full alpha for a real depth, then a shoulder, which is `laser`'s own
   rule applied to the one thing on this board that genuinely is a wall. It also stopped fading on
   a square curve, which had it a ghost by halfway through the very sweep a player is watching.

37ec. **A sixth chapter costs what a fifth did, and the bill came due in the same two places:
   two boss verbs (37br) and one charm (`SiegeCharms.Upto`).** Paid on 2026-09-17. **The two
   verbs left were the ones nothing had taken from the *player* rather than from the line**: a
   **gorgon**'s glare turns a ward stone-struck - it fires on its own cadence, burns the fuel each
   shot costs and lands nothing - so it takes the ward's *work*, and the only answer is to stop
   feeding that colour. That is the third way of taking a turret out of a fight and the three must
   not be folded (37cw's rule, asked again): a douse takes what is in the tube and is answered by
   pouring more in, a shackle takes the seconds and *banks* the fuel, a glare takes whatever is
   poured in next. A **sunlord** seals a ward and gives the player twelve seconds to fill its tube
   or lose it - it takes the player's *agenda*, and **it is the first boss verb in this mode that
   can be answered**. That is what separates it from a gravemaw's devour, which takes a ward full
   stop: one is a loss and the other is a decision. **It may never seal the last ward standing**,
   refused both where the target is chosen and again where it lands, so a verb cannot end a run on
   its own. **Neither reaches par**, which is what made both affordable.
37ed. **And the charm left was *distance*, which is the one axis six charms in nothing had
   touched.** An **anvil** drives everything walking back up the slope by a fifth of the hill
   (`SiegeTuning.AnvilHeave`) and hurts nothing at all. **It is the first defensive payoff this
   mode has had**: a siege is lost when the last ward falls (37b), and until it the only answer to
   something already at the line was to kill it faster. **It is not the hourglass said twice**, and
   the difference is which board the player reads: an hourglass buys three seconds *at the range
   things already stand*, so it is worth most on a full hill; an anvil undoes progress, so it is
   worth most when something is *close*. A stop is time and a shove is ground, so
   `StopsTheHill` and `ShovesTheHill` are two predicates. **It scales with the line twice over
   and with no number** (37cg): the seconds it buys are the standing turrets' fire, and the shove
   is a share of the *hill*, so a bulwark at `BulwarkMarch` is pushed back more than twice as long
   as a creeper at `CreeperMarch` - the charm is worth most against exactly the thing a surged
   chapter is made of. **A boss does not move** (37di: a fight is measured from where it stands),
   so an anvil on a boss wave - which is always a wave of its own (37dn) - buys nothing, and the
   refusal is drawn.
37ee. **A knock-back is a debt the model works off, never a position the view is handed.** The
   view draws a raider wherever the model says it is, once a frame, so a shove written straight
   into `March` teleports a hill of bodies and reads as a glitch. `SiegeRaider.Heave` carries the
   ground still owed and `Walk` pays it off at `AnvilPace` - so the drawing follows for free, the
   hold simulation walks through it exactly as a player does, and a body shoved off the line stops
   swinging with no second rule saying so (`AtTheLine` reads the march). The lean is the view's
   and is read off the model every frame, for the douse's and the rubble's reason.
37ef. **A surge is a cliff and composition on top of one is a wall, and this is the measurement
   that says so.** The sixth chapter was authored at four tenths of surge *and* four to six more
   bodies a wave than the fifth, on the reasoning that both levers had always moved together. It
   held **14 of 90** runs on the starter against Thundercrag's 45, with five rungs held at no
   rhythm at all. Cut back to the fifth chapter's own composition at the new surge it reads where
   a sixth chapter should. **At four tenths, the surge is the whole lever**: 37bz measured a tenth
   as a cliff, and what that means for an author is that a chapter past the fourth gets its
   difficulty from `ToughnessFor` and its *identity* from its cast, its bosses and its charm.
37eg. **A boss spell may be drawn rather than baked, and the fifth chapter's reels are the
   argument.** Every spell reel before the sixth chapter's came out of `SiegeShotBake` and a
   licensed particle pack, and Thundercrag's two are still owed months later because a batch-mode
   bake of that pack renders every frame shader-pink - only a running Editor can cut them. A drop
   whose art cannot be produced without somebody sitting in front of the Editor is a drop that
   ships with red names in it. The gorgon's and the sunlord's six reels are numpy and Pillow
   (`make_siege_art.gaze`, `.decree`), which is the move 42j already made for the legendary band
   and buys the same thing: a spell can be a **behaviour** rather than a photographed shape.
37eh. **A cast row may name its own attack, and a row that cannot find one is silent.**
   `walk_and_swing` answers an *empty* swing for an animation folder that is not there and the
   build writes nothing - so the skeleton archer, whose pack calls its attack `Front - Shooting`
   rather than `Front - Attacking` because it has a bow, came out of the first cut of the sixth
   cast with no swing reel at all. It imported, addressed, audited and drew; what it did was reach
   the line and keep walking. `COURT_SET` carries the animation per row and the build **refuses**
   a body with no swing rather than warning.
37ei. **A promise paid out of the player's turrets reads as a broken game; pay it out of the boss's health
   bar.** The phased fight (37di) shipped as an *invulnerability*: every stand opened with a guard, so for
   three to four seconds out of each of three stands a fed, lit line stood pointed at a boss and did
   nothing, and a spinning ring was drawn round the boss to explain it. Reported from play, repeatedly, as
   *the turrets start attacking too late* and *I do not want that circle*, and both are one fault. **The
   guard is a floor now**: the line fires from the frame the boss plants, the bolts land, the bar walks
   down to its notch and rests there while the boss casts. **The arithmetic is unchanged** — `BossPhases`
   × `PhaseLeast` is still the shortest a fight can be, `PhaseMost` is still the deadline, the cadence
   ladder is untouched — and **for any line that cannot chew a third of a boss inside `PhaseLeast` the two
   are identical to the frame**, which is why nothing shipped moved: eleven of the twelve shipped boss
   rungs read the same or better, and the sixth chapter's sweep moved 40 → 39, 59 → 60 and 83 → 83 of 90.
   Three things follow. **`Impervious` is the narrow window** — the walk in, and whatever seconds a line
   quick enough to reach the floor early has bought itself — where it used to be half the fight. **The
   overcharge was the same fault wearing a button**: the control was drawn off the tube (`Armed`) and
   refused by the hill, so it pulsed and swallowed taps for exactly those seconds; `SiegeBoard.CanOvercharge`
   is one reading now, asked by the drawing and by the throw, and *lit* and *lands* are the same sentence.
   And **the ring went with the rule** rather than being restyled — a circle laid over a boss reads as a
   shape drawn on top of the board, which is the verdict that already took every other ring in this mode
   off the hill (37dm's measurement is what is left of it).

37ej. **The hill holds one caption at a time in a place, and none of them may leave the board
   sideways.** Four things draw across it - the chain banner, the wave banner, the forecast band
   and the count-in - which is six pairs, and one of them was answered. Measured
   (`SiegeView.Captions`), the band and the banner share **1.31 cells of row** on a 19.5:9 phone
   and the count-in and the banner **1.15**, so a boss falling wrote its name through the gem
   counts, and every run this mode has ever opened drew "WAVE 1 OF 8" over its own GO!. The pairs
   that can be stacked are stacked by the ladder and **the rest are held** - news outranks a
   readout and the readout waits, the held thing's *sound* is never given up, and the first wave
   is not announced at all because GO! already announced it. Sideways is the second half: a
   caption here is one unbroken line with wrapping off, so one too wide is not clipped, not
   wrapped and not reported - it is drawn off both ends ("THE BLIGHTCALLER FALLS" measures **9.1
   cells on a board of 8**). Every one is fitted to `Captions.Room`, and **what gives on a long
   name is the pop rather than the letters** (`Captions.Pop`), or a boss arrives announced
   smaller than an ordinary wave. **Only a render can measure the width**, because the answer is
   the shipped face's own metrics: `render_siege.py --captions`.

37ek. **A lasting ability is a *state* the raider wears, and reporting each tick of one as a bolt is
   what made an ember turret read as a machine gun.** A `SiegeBolt` means *something left a barrel*,
   and the view answers one the only way it can - the turret recoils, the muzzle flashes, a comet
   crosses the hill, something lands. A burn ticked every frame it took a whole point, so one ember
   seat drew about **thirty complete shots a second out of one barrel**, and the damage-over-time the
   player had paid for was invisible. **Nothing was wrong with the arithmetic**, which is exactly why
   no gate and no fixture could see it: the *type* of the record was the bug. It is a `SiegeBurn` now,
   paid on a cadence (`SiegeTuning.BurnTick`), and the drawing is a **looping reel that stands on the
   body for as long as the model says it is alight** (`SiegeView.Burn`) with the ticks as small flares
   over it. Three consequences worth their own lines. **The cadence costs the rule nothing** - the
   remainder has always been carried, so what a burn is worth over its own seconds is unchanged
   whether it is handed over in two hundred slivers or six instalments - **and the last instalment is
   paid on the beat the burn ends**, or a turret quietly pays less than the figure on its card in a
   way no arithmetic anywhere could see. **The cadence is armed where a burn *starts* and never on
   the refresh**: a ward fires every `FireEvery`, which is shorter than the tick, so a counter
   re-armed by every bolt would be pushed past its own boundary for ever and a raider under
   continuous fire from the turret bought to burn it would take *nothing* - which is `Stagger`'s trap
   read from the other side, where a refresh makes an ability never end and here it would make one
   never begin. And **the flame is the player's seat rather than the raider's colour**
   (`WardModel.BurnFor`), because what a burn has to say is which of the four turrets is being paid
   for.

37el. **A dealt gem does not land already matched, and how often that rule is waived is one
   number.** Nothing used to stop it: a refill was a free draw per cell, so every collapse rolled
   the board a fresh chance of three alike and the chain that followed was the deal agreeing with
   itself rather than anything the player did. Measured over ninety runs a chapter, **39% of every
   match cascaded** and the deepest reached **x15** — on the three-colour opening rungs a match
   cleared **13 gems** against a par crediting 5.5. A cascade nobody set up rejects no play, so it
   says nothing about any (invariant 5d), and the owner's report was *one match and CHAIN x15 all
   the time*. `SiegeLayout.Lined` asks whether one cell is in a run — the same definition
   `Runs` uses, held to it by a differential over random fields, because a second reading of one
   rule is what 5b refuses — and `SiegeBoard.Settled` walks the bag from where the draw landed and
   takes the first gem that settles. **Still exactly one `Next()`**, which is why it is a rotation
   rather than a re-roll: a stream drawn a different number of times deals a different field from
   the same seed (41). **`Collapse` is two passes for this** — gravity over the whole field, then
   the refill — because the question has no true answer until every gem is in its final place.
   **Both ends of the dial are wrong**, so `SiegeTuning.RefillSettlesPercent` is a dial and not a
   switch: at 100 the board chains on 17% of matches and at 0 on 48%, and it ships at **60**. What
   is left at any setting is the chain the player caused — gravity dropping gems that were already
   there into line. **The lever for making this mode easier is this number and nothing else**: a
   chain is gems, gems are fuel and fuel is bolts, so every other route changes what a match
   *delivers* rather than what it *does*, and moves par, both star lines and every utility's charge
   with it.

37em. **A seventh chapter cost two boss verbs and nothing else, and the "nothing else" is the
   entry worth keeping.** 37br's bill came due for the fourth time on 2026-09-20: twelve verbs
   served six chapters, so Bonereach brought a **harrower** and a **hollowking** or repeated a
   fight. **But it brought no charm** - `SiegeCharms.Upto` clamps at the roster's own length, so
   the seventh chapter deals exactly what the sixth does, by the owner's call and by 37cg's
   arithmetic: six free payoffs is what this hill can carry, and a seventh would be priced
   against a mode already at the ceiling. **And it brought no cast** - `SiegeMode.MainCasts`
   wraps at six, so it draws the insects, which is invariant 7c's bargain collecting rather than
   a shortage. What is left as the chapter's identity is its **map**, its **surge** and its **two
   fights**, and 37ef already said that is the right list.
37en. **The two verbs left were the two that hand something *back*, and that is what separates
   them from the twelve.** A **harrow** takes a rank off a ward and drops it on the hill as an
   ordinary `SiegeCog` bound to that ward - so it is an overlord's sunder read the way a
   sunlord's seal reads a gravemaw's devour: one is a loss and the other is a decision, and the
   decision is the bomber's (40i, *when*). It reuses the cog entirely, which is why it cost the
   board one method (`SiegeBoard.Scatter`) and the view one drawing: everything that already
   draws, ages, tramples and spends a cog works on it untaught, including `Take`'s re-asked
   ladder - a ward the harrower just robbed is by construction one rung short, so its cog is
   always takeable. A **wane** strikes every ward that has landed no bolt since the boss's last
   cast and leaves the rest alone, so it is **the only verb in this mode answered *before* it
   lands**: the instruction is *keep all four working*, which is the opposite of what every
   other fight here teaches, and a line fed evenly pays nothing at all. That is 5d passed by
   arithmetic rather than by argument - there is an arrangement it rejects and an arrangement it
   does not - and it is what stops it being a rally with a filter, since a roar takes the same
   from all four whatever the player did. **Neither reaches par**, which is the first question to
   ask of any new verb. Two things they cost that a reader will trip over: the wane is the second
   spell aimed at **no ward** (`AimsAtAWard`, the rally's clause), so it is settled in `Arrive`
   before the indexed path and has no flight reel at all; and what it reads is **booked bolts**
   rather than `SiegeWard.Shots`, because a stone-struck ward takes a shot, spends the fuel and
   lands nothing (37ec) and would otherwise count as working.

**Adding a boss rung, or a boss.** A rung: author `boss: "<kind>:<colour>"` on rung five or ten, no
other number; copy the rung into the chapter's table in `SiegeRuleTests.Chapters.cs` (`rungs.py` holds
it to the body); **add the chapter to `SiegeRuleTests.ShippedChapters` if it is not there** — that one
line is the whole of what a chapter costs the fight gate, and Dustcrown shipped without it, so its
gorgon and its sunlord went six weeks unmeasured; run `python Tools/verify/tests.py SiegeRuleTests` —
the fight gate, the chapter sweep and `NoBossVerbIsSentByAnyTwoChapters` are the whole verdict, and a
rung on which the boss never fights is red, not a note for later. A **kind** (when the verbs are spent, 37br): append the enum member; its
health, march, hold, cadence and blow beside the others in `SiegeTuning` and its arm in `HealthOf`,
`MarchOf`, `HoldOf`, `CastEveryFor`, `CastOf`, `SpellOf`; a `SiegeSpell` member and its `Arrive` and
`Wanted` clauses; the token in `SiegeLayout.BossNames`; body and cast reels in `make_siege_art.BOSS_SET`
and its own spell row in `SiegeShotBake` (37dc); scope in `SiegeMode.Bosses`; a loc key in
`SiegeView.BossKey`; arms in `Unleash` and `Aftermath` (37dd); a row in `render_siege.BOSSES` and
`Tools/verify/siege.py`. Every fixture that matters walks the enum (`EveryBoss`, `SiegeArtTests`,
`SiegeCaptionTests`), so the one you forget is the one that goes red.

38. **A mode hides behind one manifest boolean, and deleting one is a session.** Hidden is `"disabled":
   true` and nothing else; deleted means class, board, view, screen, validator, reading, bodies, art,
   mirrors and tools all go, with the ids spent.
38a. **The front door is `LevelModes`' first entry** — what the switcher offers first *and* what a map with
   nothing remembered opens on.
39. **A utility buys a finish and never a grade, and in Thornwatch that is arithmetic rather than a
   policy** — everything follows from *how many matches would this have saved?*, because a grade reaches a
   public board.
39a. **The stock is two counters per id** — `earned` and `spent`, each monotonic, joined by a per-id `max`,
   with what is in hand derived and clamped at nought.
39b. **A chest may pay a utility**, one kind and an **item id**, because an enum member per item makes every
   addition a code change and a renumbered contract.
39c. **An icon is not content, so adding a utility is a build.** Both gates error on an entry whose icon the
   manifest does not name.
39d. **The bar is furniture, not three buttons** — five cells, three of them holding something, because a
   bar sized to the catalog would move every slot under a player's thumb the day a fourth ships.
39e. **A sound is a piece of news.** A library of *physical* noises has nothing that reads as a spell.
39f. **A target has to be a place, not a distance.** The hill is a **grid** — 33g at its strongest.
39g. **The board runs to the edges of the screen, and the field is laid out to the width.** **Round the end
   that is open and square the end that meets something.**
39h. **The kit is a shop shelf, and it lists no product and no good** — reachable only mid-run, on the one
   screen where "I want more" is answered elsewhere.
39i. **A panel over a run holds the run, and the rule had to live in the frame rather than in the panels.**
39j. **A stock prices how often across a lifetime; only a cooldown prices *when*.** *How quickly a player
   may act* is not a question anything else in this project asks.
39k. **A target is a picture, so the rule has to read the picture** — the drawn grid was not the played
   grid, under a comment claiming 33g held.
39l. **A scanner finds a wrong name and can never find a missing case.** A lookup taking a *kind* fell
   through its `default` and answered null, and every caller hands that straight to an `Image`.
39m. **A magnitude is meaningless on its own; what it is *measured in* decides whether it lasts.** Every
   magnitude declares a `UtilityUnit` derived from the kind: **hill health** climbs with the chapter, **ward
   health** and **fuel** do not. A climbing magnitude is authored against a baseline raider and converted at
   the point of contact through that raider's own `SiegeSurge.Hurt` — the same multiplier its health went
   through, so the share is exact by construction with no second number to keep in step. **Scaling a bought
   utility with the player's *line* is wrong twice**: it pays least to the player it is for, and it would
   make a purchase decide a graded number. **A free payoff scales with the line; a bought one with the
   board.**
39n. **A damage figure is a readout, not an effect, so it gets a layer above every effect.**
40. **A mechanic that attacks the *field* was the hole this mode had** — everything could only hurt the
   wards, so the field was a fuel tap the player operated while looking somewhere else.
40a. **A mechanic nobody authored into a wave does not exist, and it shipped that way for two chapters** —
   a level that sends none of something validates perfectly.
40h. **The hill may not reach into the gem board.** The gem board is the player's; **the connection is the
   player reaching up, not the hill reaching down.**
40i. **A bomber is a creeper the player is pleased to see** — it leaves a live bomb standing on the hill,
   and **it goes off where it stands, immediately**, because a bomb is already somewhere and **the decision
   is *when***.
40j. **A raider that pays the player has to be priced as one**, and the measurement inverted the obvious
   answer: a brute-weight bomber cost 16 runs in 90 and a creeper-weight one gained 4.
41. **A random stream is part of a level's content, and anything that changes how often it is drawn from is
   a content change.** The hill's lanes are drawn from the *field's* stream, and that is load-bearing.
42. **A player chooses the line, and the load-bearing rule is that no turret may make a bolt weaker.**
42a. **A shop that only lets you see what you have already bought is asking for a decision it will not show
   you.** The preview costs the held case one extra tap, which is the right price.
42b. **The map carries the line it is about to send** — the bar is a readout along the foot and is also the
   door. Both rows are spread by **even shares**; a kit nobody holds shows an **empty well**.
42c. **A turret is bought for one colour, and a keeper level is the whole of what opens a rung.** Per
   colour, because which colour a trick is worth having on is what makes the shelf a choice. **The
   sequential unlock is gone** — reach the level, buy in any order.
42d. **A preview that does not show the thing being paid for is a thumbnail with a sentence over it** — it
   fires exactly what the board would report, **mirroring the rules rather than staging a demonstration**.
42e. **What paid for the seal was the strictness of the wall, and what replaces it is the band.** Ties are
   legal and only a **fall** is refused; `WardTier`'s three bands became the wall (I under 20, II 20–29, III
   30–40) and a rung outside its own band is refused — invisible to every other gate, because such a file
   parses, prices, validates and plays. **The lock means the wall**, the veil alone means "not yours yet",
   and the strip says **Level 26** rather than LOCKED.
42f. **The whole shelf is priced in credits, and what that removes is a second question the player was
   being asked.** The prices **continue the credit ladder rather than converting at the shop's rate**, or
   the shelf would get cheaper halfway up. It cost no code and no re-seed. **What it does cost is the gem
   sink.**
42g. **A legendary turret wears no colour, and that one fact is two rules at once** — it is cut once
   rather than four times, and it fires at **everything** on the hill. Spelled twice they could drift, so
   it is **one authored flag** (`WardModel.Legendary`) and never a reading of the rung: `WardTier` is
   punctuation over the shelf's order, so a band that decided what a turret *does* would make a re-rung
   shelf silently change four turrets' rules. What holds the flag to the LEGENDARY header is
   `WardCatalog.LadderProblem`, in **both directions**. **There was a third and it is spent** (42k): it
   was bought once rather than once per seat, which is a rule about a *purchase* riding on a fact about a
   picture.
42h. **It cost the save nothing, and the shape it was chosen for is spent.** A legendary's row in
   `wardsOwned` was the **bare id**, which has meant *every colour* since colours shipped — it is what a
   build that owned turrets outright wrote and the only reading a union merge could safely give one — so
   there was no schema version, no `hasOnly` release and no migration. **A legendary is written
   `{id}:{colour}` like everything else now** (42k) and it still costs the save nothing, for the plainer
   reason that it is the row every other turret already had. **Both readers keep the bare clause as
   legacy**, on the client and on the server, or a five-star legendary bought under the old rule publishes
   at one star on all four seats with nothing saying so — and a bare row still means all four seats, so
   nobody loses what they paid for.
42i. **It is still strictly an addition, and that is arithmetic rather than an argument.** Par is the
   hill's health over a perfect match computed against the baseline bolt, so a turret reaching every
   colour only ever fires bolts that would otherwise not have been fired: a run ends sooner and par
   over-states what a good one needs. **No star line moves.** The band sits at keeper 45–60 and is priced
   in **credits** — gems until 2026-09-18, moved at the owner's instruction as a flat **x25** on the
   authored gem ladder, so the band's shape is untouched and one multiplier retunes the whole of it.
   Like 42f it cost no code and **no re-seed**, and for a sharper reason than that one: the server is
   published only `{level, free}` per turret and `free` is `gems <= 0 && coins <= 0`, so a band that
   swaps currency without changing its walls publishes a byte-identical `config/grove.wards`. **What it
   re-opens is the hole 42f named** — this mode now sells nothing for gems at all.
42j. **The band could not be drawn from the projectile pack, and that is a fact about the pack.** It holds
   twenty families and the roster wears nineteen, so ten more would be ten *third variants* — the same
   silhouette in a different hue, which is 37z in one sentence. They are **drawn** instead
   (`Tools/make_legend_fx.py`, numpy and Pillow, offline with `--check` and `--contact`), which also buys
   what a bake cannot: a legendary can be a **behaviour** rather than a shape — lightning that forks
   differently every frame, a shot that visibly bounces, a jet whose turbulence scrolls.
42k. **A legendary is bought for a seat, exactly like every other turret on the shelf — and that is the
   *second* answer to one hole.** A colourless turret's row was the bare id, which means every colour, so
   one payment stood four Eclipses: the line was four turrets at a quarter of the price and **every gate
   was green**, because the ledger, the holding and the line are each right and nothing anywhere asked how
   many of one turret a line may stand. The first answer was a **copy row** (`eclipse#2`) counted against
   the seats already standing it — correct about the money and wrong about everything else. It gave the
   band a shape no other rung has, it hid the second purchase behind a key on a panel rather than putting
   a price on the card, and it left **one star ladder under four turrets**, because a copy is not a seat
   and only a seat has a ladder: four Eclipses paid for separately climbed together. The owner reported
   all three as one complaint.
   **Buying per seat is the same bound arrived at by the rule that was already there**, so it *deletes*
   rather than repairs — `WardLedger.Copies`, `OfferAnother`, `TryBuyAnother`, `WardLine.Resolve`'s cap,
   `WardPreviewKeys.Buys`, the shelf's corner chip and the server's `copiesOf` all go, and the star ladder
   becomes per seat with **no line written about it**, because `WardHolding.Row` is the one place a
   holding is spelled (15a). It costs no schema version, no `hasOnly` release and no migration, and the
   rules' bounds do not move: a legendary occupies at most four rows, which is what every per-colour
   turret has always occupied.
   **The copy mark is retired in place on the wire** — nothing writes one, `IdOf` still reads one and
   `Spellable` still refuses one in an id, because a rolled-back client can still write a row into a
   union-joined set nothing may prune. **A legendary bought under either old rule keeps all four seats**,
   through the bare row, which is the only direction a rule about somebody's purchases may move.
   **What it deliberately does not take away is the band's own rule**: which seats a player buys is still
   theirs, and a legendary still ignores the colour lock on the hill.
   **The one thing that got tighter** is the star ledger's headroom: thirty turrets on four seats is 120
   rows against `MaxRows` 128, where the band used to carry one row each. It is a fixture rather than a
   comment (`WardLoadoutTests.TheStarLedgerHoldsTheWholeShelf`), because the failure is the quiet kind —
   truncation drops upgrades somebody paid for.
42l. **A key that cannot act is worse than one that is not drawn, and the cheapest fix for one is a rule
   that stops producing the state.** The panel's lower key was EQUIP over anything held, which became a
   button that did nothing the moment a turret could be *owned here and standable nowhere* — so it learned
   to say the price of the copy that would put it here, which is a fourth state on a panel that had three.
   Buying per seat (42k) means held implies standable again for the whole shelf, so the key is EQUIP or
   EQUIPPED and **never carries a price**, and the shelf says what a seat costs on the **card**, where
   every other price on this screen is said. **The corner chip is gone with it** — and it was placed by
   `render_loadout.py` and by nothing else: written as a strip, which is where every other number on a
   cell goes, it drew straight through the star row, because a held cell drew stars and no strip and an
   unheld one drew a strip and no stars, so the two had never been drawn together.
43. **A mode may have a second ladder, and it is a *track* rather than a mode or a chapter.** `GameTrack`
   is one level finer than `GameMode`, the index lanes on the **pair**, and `ChaptersIn(mode)` answers the
   **main** track alone.
43a. **A lane with no ladder gets no map, and what it gets instead is a *hub*.** `GameTrack.Laddered` is
   **declared, never derived** — the obvious reading is equally true of a mode whose second chapter has not
   shipped yet.
43b. **A screen made of loose parts is not a screen.** What fixed it was **furniture**, and **a lane graded
   on how far you got puts how far you got in the middle of the screen**. **An unplayed state is not a
   nought.**
43c. **Placing anything against the map's header means measuring the header, and the constant lies** —
   `HeaderUnderside` is the *mode* switcher's slot, and a map never noticed because a map scrolls under its
   own header. Same shape at the other end: `LoadoutBar.Overhang`. **And everything a piece draws has to fit
   the box the stack gave it.**
43d. **A lane's first chapter has nothing behind it, so a star gate cannot reach it.** A chapter may carry a
   **keeper wall**, `minKeeperLevel`, **inside `ChapterGate`** rather than beside it, and **the wall is the
   refusal reported when both stand**. Three non-obvious things: `IsUnlocked` short-cut on "nothing before
   it" before asking any gate, making the wall dead code on the only lane that has one; the monotonic clause
   reads a second ledger, because an endless run is never *cleared*; and the wall is content, so both gates
   error above the level curve's top and warn above the catalog's.
43e. **A lane with no ladder is bought at the gate, and every ending of it is free.** A charged run is
   paid for by *failing* it, which works because a glade is a thing you either finish or fail; a watch has
   no finish, so every one of them ends in a defeat and pricing the defeat would take a heart off a player
   for beating their own record. `HeartPrice.Entry` is a fourth price rather than a bool, because the door
   asks whether a heart is *needed*, the ending asks whether one is *owed*, and the defeat panel has to
   **say which silence it is looking at**. Three consequences worth their own line: it is asked of
   `GameTrack.Laddered` rather than of the lane's id, so a second endless lane costs nothing; the charge is
   in **`RunScreen.Commit`**, the one place that means "the run is now owed for", because three of the four
   doors into a run never touch `PlayRoute`; and it must be read **before** both free clauses, since the
   lane's one level is the first of its own lane *and* becomes a finished glade after one starred watch.
   **Leaving is free and restarting is not** — the fresh watch is bought at the gate like every other — so
   the two exits are priced apart and the restart's confirmation carries its own sentence.
