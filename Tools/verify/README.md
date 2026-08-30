# Verifying without the Editor

The Unity Editor is usually closed, and the MCP bridge is unavailable whenever scripts
fail to compile — which is exactly when a check is wanted. These run offline.

```
python Tools/verify/compile.py            # every assembly, in dependency order
python Tools/verify/tests.py [Fixture]    # the EditMode suite
python Tools/verify/content.py            # the shipped levels and the manifest
python Tools/verify/loc.py                # every key-shaped literal resolves
python Tools/verify/names.py              # the keeper-name fold, on Unity's own Mono
python Tools/verify/difficulty.py         # what each glade actually asks of a player
python Tools/verify/sfxnames.py           # every sound the code plays exists, and vice versa
```

`names.py` is the one that runs the shipped code on **Unity's Mono** as well as on the
bundled .NET, because it covers arithmetic the two runtimes have already been caught
disagreeing about. There used to be a second — Lightweave's boards were *generated*, so
"the same seed deals the same board everywhere" was the property that mode rested on, and
a float in a walk budget dealt two different opening groves. The mode is retired and the
diff went with it; what is left is the rule, which is that **nothing that decides a cell
may be a float**. Every mode shipping now authors its board in the file and searches it in
integers, and a mode that ever generates one again needs that diff back before it ships a
single board.

`content.py` is where the three non-glade modes are proved, because all three author their
whole level in the file: `fall.py`, `keeper.py` and `bud.py` are the mirrors it runs,
each pinned against its shipping C# copy by a vector file (`fall-vectors.json`,
`keeper-vectors.json`, `bud-vectors.json`) that the Editor suite runs through the other
side. Every vector case carries a **play** as well as a par, because two copies can agree
about what a board costs and still disagree about what happened on the way.

`sfxnames.py` closes for audio the gap the loc gate has always covered for strings: it
proves what the code plays, what is on disk and what `AssetManifest.Sfxs` preloads all
agree, in both directions. A misspelled sound name used to be a runtime
`InvalidKeyException` and a silence that shipped green. Two details are load-bearing. It
scans **`Presentation` only**, because `Audio` lives there and Domain may never reference
it — the first version scanned everything and failed on a PlayerPrefs key called `KeySfx`
in a file invariant 2 freezes. And it takes literals at the call's **own** argument depth,
so `bed ? "chime" : "lit"` yields both arms while a nested `Loc.Get("ui.x")` yields
nothing. It is not yet wired into `ContentBuildGate`.

`make_chapter_art.py`, `import_grove_art.py` and `make_sfx.py` sit beside them in `Tools/`
and are not checks at all — they are the art and sound pipelines, and all three are
re-runnable so the diff shows the mapping rather than the result. `make_sfx.py --check`
gates reproduction the way the art tools do, and `--contact` is its equivalent of a contact
sheet: a page that *plays* the set, because sound cannot be judged by looking at it.

`author.py` is the odd one out: it checks a board that does not exist yet. Describe a
glade as cells and the edges between them and it derives the arm masks, proves the same
rules `content.py` proves, draws the wiring as a picture and prints the rows to paste
into a chapter file. Typing masks by hand is how an arm ends up pointing at nothing, and
that got worse with taproots, briars and crossings — a briar draws four arms and conducts
two, every conduit on a taproot has to agree on one number of turns, and a crossing's two
strands must not turn out to be joined somewhere else on the board. None of the three is
visible in a grid of tokens. `Board.cross`, `Board.root` and `Board.path` are the
authoring side of those: `root` derives every member's start rotation from the number of
taps you want the root to cost, rather than leaving you to type four numbers that have to
agree. It is an aid, not a gate: `Validate Content` and the build
gate remain the authority.

`compile.py` is the one that proves the layering. Each assembly is built separately with
its own reference set, so Domain compiling without `UnityEngine.UI` or Presentation *is*
the check for invariant 3 rather than an assumption about it.

`tests.py` reflects over the compiled test assembly and invokes NUnit's attributes
directly. It reports three outcomes, and the third matters: a test that reaches a native
Unity call — `JsonUtility`, `Application.dataPath`, anything deriving from `Object` — is
counted as **needs the Editor** rather than as a failure, because it cannot run here and
calling that a failure trains everyone to ignore the number. Those still have to go
through Test Runner before shipping.

Current baseline: 1349 pass offline, 166 need the Editor.

None of this replaces `Glimmer Grove ▸ Validate Content`, `▸ Validate Art` or the build
gate. It replaces *not checking*, which is what being unable to open the Editor used to
mean.
