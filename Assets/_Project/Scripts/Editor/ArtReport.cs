#nullable enable
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CoD.EditorTools
{
    /// <summary>
    /// Prints what the art in Assets/_Project actually costs. Menu: CoD → Report
    /// Texture Budget, and CoD → Report Mesh and Material Counts.
    ///
    /// WHY THIS EXISTS
    /// guard-texture-budget.mjs enforces the per-texture rule — nothing imports
    /// above 1024 — but a rule per file says nothing about the total. Two hundred
    /// obedient 1024 textures is still 280 MB, and the binding constraint on this
    /// project is a 4 GB card, not a per-asset limit. This is the number the guard
    /// cannot produce, because it needs Unity to have imported the textures.
    ///
    /// The budget it is read against lives in docs/systems/rendering.md: 450 MB
    /// target, 700 MB hard cap, with the rest of the card going to render targets,
    /// meshes, shader variants, and whatever the driver decides to keep.
    ///
    /// WHAT THESE NUMBERS ARE NOT
    /// Editor figures, not shipping figures. In the editor a texture may be held
    /// uncompressed, mip streaming is not doing what it does in a player, and the
    /// editor's own tooling holds references to things the game never loads — so
    /// this OVERSTATES. It is the right tool for "which folder got fat this week"
    /// and the wrong tool for "does it fit". The answer to that one comes from a
    /// Memory Profiler snapshot taken against a DEVELOPMENT BUILD, never the
    /// editor. See the Budget section of docs/systems/rendering.md.
    ///
    /// Console output only. Nothing is written to disk, so running it can never
    /// dirty the working tree or cost an LFS object.
    /// </summary>
    public static class ArtReport
    {
        private const string ProjectRoot = "Assets/_Project";

        // Mirrors the table in docs/systems/rendering.md. Duplicated here on
        // purpose: a report that prints a total without saying what the total is
        // allowed to be forces the reader to go and look, which they will not do.
        private const long TargetBytes = 450L * 1000 * 1000;
        private const long CapBytes = 700L * 1000 * 1000;

        private const int TopOffenderCount = 12;

        [MenuItem("CoD/Report Texture Budget", false, 40)]
        public static void ReportTextureBudget()
        {
            // `t:Texture2D` silently excludes imported HDR cubemaps. The first
            // Poly Haven reflection exposed that blind spot: the report would
            // have claimed zero delta for the one source it was meant to measure.
            string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { ProjectRoot });

            var byFolder = new Dictionary<string, FolderTotal>();
            var textures = new List<TextureEntry>(guids.Length);
            long totalBytes = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // `is null`, not `== null`. UnityEngine.Object overloads the
                // equality operator, and an overloaded operator is invisible to
                // the compiler's null-state analysis — so `== null` leaves the
                // local "maybe null" and every use below it warns under
                // #nullable enable. A freshly loaded asset is never a destroyed
                // object, so the two tests mean the same thing here.
                Texture? texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (texture is null) continue;

                long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                totalBytes += bytes;

                string folder = FolderOf(path);
                if (!byFolder.TryGetValue(folder, out FolderTotal total))
                {
                    total = new FolderTotal();
                    byFolder.Add(folder, total);
                }
                total.Count++;
                total.Bytes += bytes;

                textures.Add(new TextureEntry
                {
                    Path = path,
                    Bytes = bytes,
                    Width = texture.width,
                    Height = texture.height,
                    Format = FormatOf(texture),
                });
            }

            var folders = new List<KeyValuePair<string, FolderTotal>>(byFolder.Count);
            foreach (KeyValuePair<string, FolderTotal> pair in byFolder) folders.Add(pair);
            folders.Sort((a, b) => b.Value.Bytes.CompareTo(a.Value.Bytes));
            textures.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

            var report = new StringBuilder(2048);
            report.Append("Texture budget — ").Append(ProjectRoot).AppendLine();
            report.AppendLine();

            if (textures.Count == 0)
            {
                report.AppendLine("  No Texture2D assets found. Every surface is still using a flat material.");
            }
            else
            {
                report.AppendLine("  BY FOLDER");
                foreach (KeyValuePair<string, FolderTotal> pair in folders)
                {
                    report.Append("    ").Append(Megabytes(pair.Value.Bytes).PadLeft(9)).Append(" MB  ")
                        .Append(pair.Value.Count.ToString().PadLeft(4)).Append("  ")
                        .AppendLine(pair.Key);
                }

                report.AppendLine();
                report.Append("  LARGEST ").Append(Mathf.Min(TopOffenderCount, textures.Count)).AppendLine(" TEXTURES");
                int shown = 0;
                foreach (TextureEntry entry in textures)
                {
                    if (shown++ >= TopOffenderCount) break;
                    report.Append("    ").Append(Megabytes(entry.Bytes).PadLeft(9)).Append(" MB  ")
                        .Append((entry.Width + "x" + entry.Height).PadLeft(9)).Append("  ")
                        .Append(entry.Format.PadRight(12)).Append("  ")
                        .AppendLine(entry.Path);
                }
            }

            report.AppendLine();
            report.Append("  TOTAL ").Append(Megabytes(totalBytes)).Append(" MB across ")
                .Append(textures.Count).AppendLine(" texture(s)");
            report.Append("  Target ").Append(Megabytes(TargetBytes)).Append(" MB, hard cap ")
                .Append(Megabytes(CapBytes)).Append(" MB — ")
                .AppendLine(Verdict(totalBytes));
            report.AppendLine();
            report.AppendLine("  Editor numbers overstate a shipping player. For the number that decides");
            report.AppendLine("  whether it fits on a 4 GB card, take a Memory Profiler snapshot against a");
            report.AppendLine("  DEVELOPMENT BUILD — see the Budget section of docs/systems/rendering.md.");

            Debug.Log(report.ToString());
        }

        [MenuItem("CoD/Report Audio Budget", false, 42)]
        public static void ReportAudioBudget()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { ProjectRoot });
            long totalBytes = 0;
            int count = 0;
            var byFolder = new Dictionary<string, FolderTotal>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AudioClip? clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip is null) continue;

                long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(clip);
                totalBytes += bytes;
                count++;
                string folder = FolderOf(path);
                if (!byFolder.TryGetValue(folder, out FolderTotal total))
                {
                    total = new FolderTotal();
                    byFolder.Add(folder, total);
                }
                total.Count++;
                total.Bytes += bytes;
            }

            var folders = new List<KeyValuePair<string, FolderTotal>>(byFolder);
            folders.Sort((a, b) => b.Value.Bytes.CompareTo(a.Value.Bytes));
            var report = new StringBuilder(1024);
            report.AppendLine("Audio memory — Unity runtime object estimate");
            foreach (KeyValuePair<string, FolderTotal> pair in folders)
            {
                report.Append("  ").Append(Megabytes(pair.Value.Bytes).PadLeft(6)).Append(" MB  ")
                    .Append(pair.Value.Count.ToString().PadLeft(3)).Append("  ").AppendLine(pair.Key);
            }
            report.Append("  TOTAL ").Append(Megabytes(totalBytes)).Append(" MB across ")
                .Append(count).AppendLine(" clip(s)");
            report.AppendLine("  Audio assets consume no VRAM; this is CPU/audio memory, not texture memory.");
            Debug.Log(report.ToString());
        }

        public static void ReportAudioBudgetHeadless()
        {
            try
            {
                ReportAudioBudget();
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Audio budget report failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// The other half of the VRAM bill. Cheap because it never touches vertex
        /// data: <see cref="Mesh.GetIndexCount"/> reads the sub-mesh description,
        /// where <c>mesh.triangles</c> would allocate the whole index array and
        /// throw outright on a mesh imported without Read/Write enabled — which is
        /// how every mesh in a shipping game should be imported.
        /// </summary>
        [MenuItem("CoD/Report Mesh and Material Counts", false, 41)]
        public static void ReportMeshAndMaterialCounts()
        {
            var report = new StringBuilder(1024);
            report.Append("Mesh and material counts — ").Append(ProjectRoot).AppendLine();
            report.AppendLine();

            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh", new[] { ProjectRoot });
            long vertices = 0;
            long triangles = 0;
            int meshCount = 0;
            foreach (string guid in meshGuids)
            {
                Mesh? mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
                if (mesh is null) continue;
                meshCount++;
                vertices += mesh.vertexCount;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    triangles += (long)mesh.GetIndexCount(sub) / 3;
                }
            }

            report.Append("  MESHES  ").Append(meshCount).Append(" asset(s), ")
                .Append(vertices).Append(" verts, ").Append(triangles).AppendLine(" tris");
            if (meshCount == 0)
            {
                report.AppendLine("    None. The grey box is built from GameObject.CreatePrimitive, whose");
                report.AppendLine("    meshes live in Unity's built-in resources and are not counted here.");
            }

            report.AppendLine();

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { ProjectRoot });
            var byShader = new Dictionary<string, int>();
            int materialCount = 0;
            foreach (string guid in materialGuids)
            {
                Material? material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material is null) continue;
                materialCount++;
                string shader = material.shader is null ? "(missing shader)" : material.shader.name;
                byShader.TryGetValue(shader, out int count);
                byShader[shader] = count + 1;
            }

            report.Append("  MATERIALS  ").Append(materialCount).AppendLine(" asset(s)");
            foreach (KeyValuePair<string, int> pair in byShader)
            {
                report.Append("    ").Append(pair.Value.ToString().PadLeft(4)).Append("  ").AppendLine(pair.Key);
            }
            report.AppendLine();
            report.AppendLine("  Material count is a draw-call and shader-variant signal, not a memory one:");
            report.AppendLine("  the SRP Batcher batches by SHADER VARIANT, so every extra variant is a");
            report.AppendLine("  batch break on the exact hardware the frame-time question is about.");

            Debug.Log(report.ToString());
        }

        private static string Verdict(long bytes)
        {
            if (bytes > CapBytes) return "OVER THE CAP.";
            if (bytes > TargetBytes) return "over target, under cap.";
            return "within target.";
        }

        private static string Megabytes(long bytes) => (bytes / 1000000.0).ToString("F1");

        private static string FormatOf(Texture texture) => texture switch
        {
            Texture2D texture2D => texture2D.format.ToString(),
            Cubemap cubemap => cubemap.format.ToString(),
            _ => texture.graphicsFormat.ToString(),
        };

        private static string FolderOf(string assetPath)
        {
            int lastSlash = assetPath.LastIndexOf('/');
            return lastSlash < 0 ? assetPath : assetPath.Substring(0, lastSlash);
        }

        /// <summary>A class, not a struct: it is mutated in place through the dictionary.</summary>
        private sealed class FolderTotal
        {
            public int Count;
            public long Bytes;
        }

        private struct TextureEntry
        {
            public string Path;
            public long Bytes;
            public int Width;
            public int Height;
            public string Format;
        }
    }
}
