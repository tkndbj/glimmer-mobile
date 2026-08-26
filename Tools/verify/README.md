# Verifying without the Editor

The Unity Editor is usually closed, and the MCP bridge is unavailable whenever scripts
fail to compile — which is exactly when a check is wanted. These run offline.

```
python Tools/verify/compile.py            # every assembly, in dependency order
python Tools/verify/tests.py [Fixture]    # the EditMode suite
python Tools/verify/content.py            # the shipped levels and the manifest
python Tools/verify/loc.py                # every key-shaped literal resolves
python Tools/verify/weave.py              # every Lightweave grove, on both runtimes
python Tools/verify/names.py              # the keeper-name fold, on Unity's own Mono
```

`weave.py` and `names.py` are the two that run the shipped code on **Unity's Mono** as
well as on the bundled .NET, because both cover arithmetic the two runtimes have already
been caught disagreeing about — a walk budget in one case and Unicode tables in the other.
`weave.py` diffs the two rather than checking either against a table, so there is nothing
to go stale; it also reports each grove's `slack`, which is the least total detour any
arrangement of it has over and above every pair's own shortest route. Zero means every
pair can go as directly as it possibly could, all at once, so the grove is joined by
drawing the obvious line at each critter and asks the player nothing.

`make_chapter_art.py` and `import_grove_art.py` sit beside them in `Tools/` and are
not checks at all — they are the two art pipelines, and both are re-runnable so the
diff shows the mapping rather than the result.

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

Current baseline: 1019 pass offline, 103 need the Editor.

None of this replaces `Glimmer Grove ▸ Validate Content`, `▸ Validate Art` or the build
gate. It replaces *not checking*, which is what being unable to open the Editor used to
mean.
