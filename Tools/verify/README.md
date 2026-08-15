# Verifying without the Editor

The Unity Editor is usually closed, and the MCP bridge is unavailable whenever scripts
fail to compile — which is exactly when a check is wanted. These four run offline.

```
python Tools/verify/compile.py            # every assembly, in dependency order
python Tools/verify/tests.py [Fixture]    # the EditMode suite
python Tools/verify/content.py            # the shipped levels and the manifest
python Tools/verify/loc.py                # every key-shaped literal resolves
```

`compile.py` is the one that proves the layering. Each assembly is built separately with
its own reference set, so Domain compiling without `UnityEngine.UI` or Presentation *is*
the check for invariant 3 rather than an assumption about it.

`tests.py` reflects over the compiled test assembly and invokes NUnit's attributes
directly. It reports three outcomes, and the third matters: a test that reaches a native
Unity call — `JsonUtility`, `Application.dataPath`, anything deriving from `Object` — is
counted as **needs the Editor** rather than as a failure, because it cannot run here and
calling that a failure trains everyone to ignore the number. Those still have to go
through Test Runner before shipping.

Current baseline: 227 pass offline, 58 need the Editor.

None of this replaces `Glimmer Grove ▸ Validate Content`, `▸ Validate Art` or the build
gate. It replaces *not checking*, which is what being unable to open the Editor used to
mean.
