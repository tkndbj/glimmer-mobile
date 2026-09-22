using System.Collections.Generic;
using System.IO;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Frames;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The name frames: the catalog against the files it names, and the life in a frame.
    ///
    /// <para>
    /// <b>The two copies of a frame's geometry are held together here.</b> The hole and the eye
    /// are measured in <c>Tools/make_frame_rig.py</c> and written into the rig; the runtime
    /// cannot read a rig (it is Editor data), so <see cref="FrameCatalog"/> carries the same
    /// two as fractions. A drift between them is a name laid out on the gold bar and an eye
    /// glowing on a tooth, which nothing else could see. Read through <see cref="TestJson"/>
    /// rather than <c>JsonUtility</c>, so the offline runner runs it (29e).
    /// </para>
    /// </summary>
    public sealed class FrameTests
    {
        static string Repo => TestJson.RepoRoot();

        static string Painting(FrameDefinition f)
            => Path.Combine(Repo, "Assets", "Game", "Art", "Frames", f.Id + ".png");

        static string RigFile(FrameDefinition f)
            => Path.Combine(Repo, "Assets", "Game", "Editor", "FrameRigs", f.Id + ".rig.json");

        [Test]
        public void EveryFrameHasAPaintingAndARig()
        {
            Assert.That(FrameCatalog.All.Count, Is.GreaterThan(0));
            foreach (var frame in FrameCatalog.All)
            {
                Assert.That(File.Exists(Painting(frame)), Is.True, frame.Id + ": no painting at " + Painting(frame));
                Assert.That(File.Exists(RigFile(frame)), Is.True, frame.Id + ": no rig at " + RigFile(frame));
            }
        }

        [Test]
        public void TheCatalogCarriesWhatTheRigMeasured()
        {
            foreach (var frame in FrameCatalog.All)
            {
                var rig = TestJson.Object(TestJson.Parse(File.ReadAllText(RigFile(frame))));
                float w = TestJson.Int(rig, "width"), h = TestJson.Int(rig, "height");
                Assert.That(w, Is.GreaterThan(0f));
                Assert.That(h, Is.GreaterThan(0f));

                var eye = Numbers(TestJson.Children(rig, "eye"));
                var hole = Numbers(TestJson.Children(rig, "hole"));

                Assert.That(frame.Eye.x, Is.EqualTo(eye[0] / w).Within(.001f), frame.Id + ": eye x");
                Assert.That(frame.Eye.y, Is.EqualTo(eye[1] / h).Within(.001f), frame.Id + ": eye y");
                Assert.That(frame.Hole.xMin, Is.EqualTo(hole[0] / w).Within(.001f), frame.Id + ": hole left");
                Assert.That(frame.Hole.yMin, Is.EqualTo(hole[1] / h).Within(.001f), frame.Id + ": hole bottom");
                Assert.That(frame.Hole.xMax, Is.EqualTo(hole[2] / w).Within(.001f), frame.Id + ": hole right");
                Assert.That(frame.Hole.yMax, Is.EqualTo(hole[3] / h).Within(.001f), frame.Id + ": hole top");

                // The nodding bone is named for the runtime; a rig without it stands still, which
                // is allowed, but a rig naming a different bone is a nod nobody sees.
                Assert.That(TestJson.Str(rig, "nodBone"), Is.EqualTo(FrameDefinition.NodBone), frame.Id + ": nod bone");
            }
        }

        [Test]
        public void TheRigIsSoundForThePackage()
        {
            foreach (var frame in FrameCatalog.All)
            {
                var rig = TestJson.Object(TestJson.Parse(File.ReadAllText(RigFile(frame))));
                var bones = TestJson.Children(rig, "bones");
                var verts = Numbers(TestJson.Children(rig, "vertices"));
                var tris = Numbers(TestJson.Children(rig, "triangles"));
                var index = Numbers(TestJson.Children(rig, "boneIndex"));
                var weight = Numbers(TestJson.Children(rig, "boneWeight"));

                int count = verts.Count / 2;
                Assert.That(count, Is.GreaterThan(2));
                Assert.That(count, Is.LessThanOrEqualTo(65535), "a sprite mesh indexes with ushort");
                Assert.That(index.Count, Is.EqualTo(count * 4));
                Assert.That(weight.Count, Is.EqualTo(count * 4));

                // Parent-first, root first: how the package walks a skeleton.
                Assert.That(TestJson.Int(TestJson.Object(bones[0]), "parent"), Is.EqualTo(-1));
                for (int b = 1; b < bones.Count; b++)
                {
                    int parent = TestJson.Int(TestJson.Object(bones[b]), "parent");
                    Assert.That(parent, Is.InRange(0, b - 1), "bone " + b + " must follow its parent");
                }

                for (int t = 0; t < tris.Count; t++)
                    Assert.That(tris[t], Is.InRange(0, count - 1));

                for (int v = 0; v < count; v++)
                {
                    float sum = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        Assert.That(index[v * 4 + k], Is.InRange(0, bones.Count - 1));
                        sum += weight[v * 4 + k];
                    }
                    Assert.That(sum, Is.EqualTo(1f).Within(.0005f), "vertex " + v + " weights sum to one");
                }
            }
        }

        [Test]
        public void TheAddressIsBuiltFromTheId()
        {
            var frame = FrameCatalog.Find("dragon");
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame.Address, Is.EqualTo(AssetManifest.FrameRoot + "dragon"));
            Assert.That(FrameCatalog.Find("no_such_frame"), Is.Null);
            Assert.That(FrameCatalog.Find(null), Is.Null);
        }

        // ------------------------------------------------------------------ the life
        [Test]
        public void TheHeadStaysWithinItsNodAndTheEyesWithinNoughtAndOne()
        {
            var idle = new FrameIdle(7u);
            const float dt = 1f / 60f;
            for (int i = 0; i < 60 * 60; i++)
            {
                idle.Advance(dt);
                Assert.That(idle.HeadDegrees, Is.InRange(FrameIdle.NodDegrees - FrameIdle.SwayDegrees - .01f,
                                                         FrameIdle.LiftDegrees + FrameIdle.SwayDegrees + .01f));
                Assert.That(idle.EyeGlow, Is.InRange(0f, 1f));
                Assert.That(idle.ShinePosition == -1f
                            || (idle.ShinePosition >= -FrameIdle.ShineOverrun - .001f
                                && idle.ShinePosition <= 1f + FrameIdle.ShineOverrun + .001f),
                            "shine position " + idle.ShinePosition);
            }
        }

        [Test]
        public void ItNodsAndShinesOccasionallyRatherThanOnceOrAlways()
        {
            var idle = new FrameIdle(11u);
            for (int i = 0; i < 60 * 60; i++) idle.Advance(1f / 60f);

            // A minute at the slowest cadence is seven nods and nine sheens; at the fastest,
            // twelve and seventeen. Either way it is neither a statue nor a metronome.
            Assert.That(idle.Nods, Is.InRange(6, 14));
            Assert.That(idle.Shines, Is.InRange(8, 20));
        }

        [Test]
        public void TheEyesFlareOnTheDrop()
        {
            var idle = new FrameIdle(3u);
            float before = 0f, peak = 0f;
            bool inNod = false;
            for (int i = 0; i < 60 * 20; i++)
            {
                idle.Advance(1f / 60f);
                if (idle.Nods == 1 && !inNod) { inNod = true; before = idle.EyeGlow; }
                if (inNod) peak = Mathf.Max(peak, idle.EyeGlow);
            }
            Assert.That(inNod, Is.True);
            Assert.That(peak, Is.GreaterThan(before + .5f), "the spark should lift the eyes well above their shimmer");
        }

        [Test]
        public void ANodIsALiftADropAndASettle()
        {
            Assert.That(FrameIdle.NodCurve(0f), Is.EqualTo(0f));
            Assert.That(FrameIdle.NodCurve(FrameIdle.LiftSeconds), Is.EqualTo(FrameIdle.LiftDegrees).Within(.01f));
            Assert.That(FrameIdle.NodCurve(FrameIdle.LiftSeconds + FrameIdle.DropSeconds),
                        Is.EqualTo(FrameIdle.NodDegrees).Within(.01f));
            Assert.That(Mathf.Abs(FrameIdle.NodCurve(FrameIdle.NodSeconds - .001f)), Is.LessThan(.1f));
            Assert.That(FrameIdle.NodCurve(FrameIdle.NodSeconds), Is.EqualTo(0f));
        }

        [Test]
        public void TwoSeedsDifferAndOneSeedRepeats()
        {
            var a = new FrameIdle(1u);
            var b = new FrameIdle(2u);
            var c = new FrameIdle(1u);
            int firstA = -1, firstB = -1, firstC = -1;
            for (int i = 0; i < 60 * 10; i++)
            {
                a.Advance(1f / 60f); b.Advance(1f / 60f); c.Advance(1f / 60f);
                if (firstA < 0 && a.Nods > 0) firstA = i;
                if (firstB < 0 && b.Nods > 0) firstB = i;
                if (firstC < 0 && c.Nods > 0) firstC = i;
            }
            Assert.That(firstA, Is.GreaterThan(0));
            Assert.That(firstA, Is.EqualTo(firstC));
            Assert.That(firstA, Is.Not.EqualTo(firstB));
        }

        [Test]
        public void ARefusedStepChangesNothing()
        {
            var idle = new FrameIdle(5u);
            idle.Advance(-1f);
            idle.Advance(0f);
            idle.Advance(float.NaN);
            Assert.That(idle.Elapsed, Is.EqualTo(0f));
        }

        static List<float> Numbers(List<object> raw)
        {
            var list = new List<float>(raw.Count);
            foreach (var o in raw) list.Add(System.Convert.ToSingle(o));
            return list;
        }
    }
}
