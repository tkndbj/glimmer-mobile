using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Writes a name frame's rig into its painting's import, so the sprite the game loads
    /// carries bones, weights and a mesh the 2D Animation package can deform.
    ///
    /// <para>
    /// <b>An importer hook, never a menu item</b> (7a's rule, about a rig rather than an
    /// address): the rig is applied when the painting lands and again whenever the rig file
    /// changes, through the package's own data providers — the same door the Skinning Editor
    /// writes through — and the package's own <c>SpritePostProcess</c> then bakes it into the
    /// sprite on the reimport this asks for. Opening the painting in the Skinning Editor
    /// afterwards shows exactly the rig <c>Tools/make_frame_rig.py</c> derived, and a hand
    /// refinement there is honoured until the next time the rig file changes.
    /// </para>
    /// <para>
    /// <b>The rig lives under <c>Editor/FrameRigs/</c>, not beside the painting</b>, because
    /// everything under <c>Art/</c> is given an address by <c>AddressableAutoRegister</c>, and
    /// a rig is a build-time instruction rather than a thing the game loads.
    /// </para>
    /// <para>
    /// <b>It applies only what is different</b>, or the reimport it asks for would call it
    /// again for ever: the bones and the vertex count already on the importer are compared to
    /// the file first, and a rig already in place costs no reimport.
    /// </para>
    /// </summary>
    public sealed class FrameRigImport : AssetPostprocessor
    {
        public const string ArtFolder = "Assets/Game/Art/Frames/";
        public const string RigFolder = "Assets/Game/Editor/FrameRigs/";
        public const string RigSuffix = ".rig.json";

        [Serializable]
        sealed class RigBone
        {
            public string name;
            public int parent;
            public float x, y, angle, length;
        }

        [Serializable]
        sealed class Rig
        {
            public string source;
            public int width, height;
            public string nodBone;
            public RigBone[] bones;
            public float[] vertices;
            public int[] triangles;
            public int[] boneIndex;
            public float[] boneWeight;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var paintings = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in imported)
            {
                string p = path.Replace('\\', '/');
                if (p.StartsWith(ArtFolder, StringComparison.Ordinal) && p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    paintings.Add(p);
                else if (p.StartsWith(RigFolder, StringComparison.Ordinal) && p.EndsWith(RigSuffix, StringComparison.Ordinal))
                    paintings.Add(PaintingFor(p));
            }

            foreach (var painting in paintings)
                if (File.Exists(painting)) Apply(painting);
        }

        /// <summary>The painting a rig file describes: the same stem under the art folder.</summary>
        public static string PaintingFor(string rigPath)
        {
            string stem = Path.GetFileName(rigPath);
            stem = stem.Substring(0, stem.Length - RigSuffix.Length);
            return ArtFolder + stem + ".png";
        }

        /// <summary>The rig file for a painting, whether or not it exists.</summary>
        public static string RigFor(string paintingPath)
            => RigFolder + Path.GetFileNameWithoutExtension(paintingPath) + RigSuffix;

        /// <summary>
        /// Apply the painting's rig to its importer. Answers whether a reimport was asked for.
        /// A painting with no rig file is left exactly as it is — a still frame is allowed.
        /// </summary>
        public static bool Apply(string paintingPath)
        {
            string rigPath = RigFor(paintingPath);
            if (!File.Exists(rigPath)) return false;

            var importer = AssetImporter.GetAtPath(paintingPath) as TextureImporter;
            if (importer == null) return false;

            var rig = JsonUtility.FromJson<Rig>(File.ReadAllText(rigPath));
            if (rig == null || rig.bones == null || rig.bones.Length == 0 || rig.vertices == null)
            {
                Debug.LogError("FrameRigImport: " + rigPath + " is not a rig.");
                return false;
            }

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) return false;
            provider.InitSpriteEditorDataProvider();

            var rects = provider.GetSpriteRects();
            if (rects == null || rects.Length == 0)
            {
                // Not yet a sprite: `ArtImportRules` makes it one on this same import, and the
                // reimport that follows lands here again with a rect to rig.
                return false;
            }

            var guid = rects[0].spriteID;
            var boneProvider = provider.GetDataProvider<ISpriteBoneDataProvider>();
            var meshProvider = provider.GetDataProvider<ISpriteMeshDataProvider>();
            if (boneProvider == null || meshProvider == null)
            {
                Debug.LogError("FrameRigImport: the 2D Animation package is not providing bone and mesh data.");
                return false;
            }

            var bones = BonesOf(rig, Path.GetFileNameWithoutExtension(paintingPath));
            var vertices = VerticesOf(rig);

            if (SameRig(boneProvider.GetBones(guid), bones, meshProvider.GetVertices(guid), vertices))
                return false;

            boneProvider.SetBones(guid, bones);
            meshProvider.SetVertices(guid, vertices);
            meshProvider.SetIndices(guid, rig.triangles);
            meshProvider.SetEdges(guid, Array.Empty<Vector2Int>());
            provider.Apply();

            // The importer's settings are now the rig; the sprite is baked from them on the
            // reimport asked for here. Asked for directly rather than through
            // `EditorApplication.delayCall`, which never fired for an Editor driven from
            // outside its own window (found 2026-09-21: the rig sat in the importer, unbaked,
            // until a reimport was asked for by hand). Inside an import batch this queues.
            importer.SaveAndReimport();

            Debug.Log("FrameRigImport: rigged " + paintingPath + " (" + bones.Count + " bones, "
                      + vertices.Length + " vertices).");
            return true;
        }

        static List<SpriteBone> BonesOf(Rig rig, string stem)
        {
            var list = new List<SpriteBone>(rig.bones.Length);
            for (int i = 0; i < rig.bones.Length; i++)
            {
                var b = rig.bones[i];
                list.Add(new SpriteBone
                {
                    name = b.name,
                    guid = StableGuid(stem + ":" + b.name),
                    position = new Vector3(b.x, b.y, 0f),
                    rotation = Quaternion.Euler(0f, 0f, b.angle),
                    length = b.length,
                    parentId = b.parent,
                    color = Color.white,
                });
            }
            return list;
        }

        static Vertex2DMetaData[] VerticesOf(Rig rig)
        {
            int count = rig.vertices.Length / 2;
            var out_ = new Vertex2DMetaData[count];
            bool weighted = rig.boneIndex != null && rig.boneWeight != null
                         && rig.boneIndex.Length == count * 4 && rig.boneWeight.Length == count * 4;

            for (int i = 0; i < count; i++)
            {
                var w = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                if (weighted)
                {
                    int k = i * 4;
                    w.boneIndex0 = rig.boneIndex[k];     w.weight0 = rig.boneWeight[k];
                    w.boneIndex1 = rig.boneIndex[k + 1]; w.weight1 = rig.boneWeight[k + 1];
                    w.boneIndex2 = rig.boneIndex[k + 2]; w.weight2 = rig.boneWeight[k + 2];
                    w.boneIndex3 = rig.boneIndex[k + 3]; w.weight3 = rig.boneWeight[k + 3];
                }

                out_[i] = new Vertex2DMetaData
                {
                    position = new Vector2(rig.vertices[i * 2], rig.vertices[i * 2 + 1]),
                    boneWeight = w,
                };
            }
            return out_;
        }

        static bool SameRig(List<SpriteBone> haveBones, List<SpriteBone> wantBones,
                            Vertex2DMetaData[] haveVerts, Vertex2DMetaData[] wantVerts)
        {
            if (haveBones == null || haveBones.Count != wantBones.Count) return false;
            if (haveVerts == null || haveVerts.Length != wantVerts.Length) return false;

            for (int i = 0; i < wantBones.Count; i++)
            {
                if (haveBones[i].name != wantBones[i].name) return false;
                if (haveBones[i].parentId != wantBones[i].parentId) return false;
                if ((haveBones[i].position - wantBones[i].position).sqrMagnitude > 1e-4f) return false;
                if (Quaternion.Angle(haveBones[i].rotation, wantBones[i].rotation) > .01f) return false;
                if (Mathf.Abs(haveBones[i].length - wantBones[i].length) > 1e-3f) return false;
            }

            for (int i = 0; i < wantVerts.Length; i++)
            {
                if ((haveVerts[i].position - wantVerts[i].position).sqrMagnitude > 1e-4f) return false;
                if (Mathf.Abs(haveVerts[i].boneWeight.weight0 - wantVerts[i].boneWeight.weight0) > 1e-3f) return false;
                if (haveVerts[i].boneWeight.boneIndex0 != wantVerts[i].boneWeight.boneIndex0) return false;
            }

            return true;
        }

        /// <summary>A bone guid the package can bind by, derived so a re-run writes the same one.</summary>
        static string StableGuid(string key)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                var sb = new StringBuilder(32);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
