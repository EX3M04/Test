// Baldi Mod Maker — Editor-инструменты. Лежит в Assets/Editor/.
// Меню Unity: Baldi Mod -> 1. Применить настройки APK / 2. Построить школу из мода
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BaldiMod
{
    [Serializable] public class PieceData { public string t; public string tex; public float x, y, z, ry, sx, sy, sz; public string name; public string prm; }
    [Serializable] public class MapData { public PieceData[] pieces; }
    [Serializable] public class ApkData { public string name, pkg, versionName; public int versionCode, minSdk, targetSdk, orientation; public bool buildSchool; }
    [Serializable] public class PathEntry { public string id; public string path; }
    [Serializable] public class PathList { public PathEntry[] items; }

    public static class ModTools
    {
        const string Dir = "Assets/ModPackage/";

        [MenuItem("Baldi Mod/1. Применить настройки APK")]
        public static void ApplyApk()
        {
            var a = JsonUtility.FromJson<ApkData>(File.ReadAllText(Dir + "apk.json"));
            PlayerSettings.productName = a.name;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, a.pkg);
            PlayerSettings.bundleVersion = a.versionName;
            PlayerSettings.Android.bundleVersionCode = a.versionCode;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)Mathf.Clamp(a.minSdk, 16, 28);
            PlayerSettings.defaultInterfaceOrientation = a.orientation == 1 ? UIOrientation.Portrait : a.orientation == 2 ? UIOrientation.AutoRotation : UIOrientation.LandscapeLeft;
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "icon.png");
            if (icon != null) PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
            AssetDatabase.SaveAssets();
            Debug.Log("[BaldiMod] Настройки APK применены: " + a.name + " / " + a.pkg);
        }

        static Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Dictionary<string, string> paths;

        static Material Mat(string tex)
        {
            Material m;
            if (mats.TryGetValue(tex, out m)) return m;
            if (paths == null)
            {
                paths = new Dictionary<string, string>();
                var pl = JsonUtility.FromJson<PathList>(File.ReadAllText(Dir + "unity_paths.json"));
                foreach (var e in pl.items) paths[e.id] = e.path;
            }
            m = new Material(Shader.Find("Unlit/Transparent Cutout"));
            string p;
            if (paths.TryGetValue(tex, out p)) m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            string matPath = "Assets/ModPackage/Mats/" + tex + ".mat";
            Directory.CreateDirectory("Assets/ModPackage/Mats");
            AssetDatabase.CreateAsset(m, matPath);
            mats[tex] = m;
            return m;
        }

        static GameObject Quad(Transform parent, string name, string tex, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = name; g.transform.SetParent(parent, false);
            g.transform.position = pos; g.transform.rotation = rot; g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = Mat(tex);
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.NavigationStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.BatchingStatic);
            return g;
        }

        [MenuItem("Baldi Mod/2. Построить школу из мода (открытая сцена School)")]
        public static void BuildSchool()
        {
            var map = JsonUtility.FromJson<MapData>(File.ReadAllText(Dir + "map.json"));
            var oldEnv = GameObject.Find("Environment");
            var root = new GameObject("ModEnvironment").transform;
            // шаблоны оригинальных объектов для клонирования (двери, блокноты)
            var nbTemplate = UnityEngine.Object.FindObjectOfType<NotebookScript>();
            GameObject nbProto = nbTemplate != null ? UnityEngine.Object.Instantiate(nbTemplate.gameObject) : null;
            if (nbProto != null) nbProto.SetActive(false);
            if (oldEnv != null)
            {
                foreach (var nb in UnityEngine.Object.FindObjectsOfType<NotebookScript>()) if (nb.gameObject != nbProto) nb.gameObject.SetActive(false);
                oldEnv.SetActive(false);
            }
            int count = 0, books = 0;
            foreach (var p in map.pieces)
            {
                var pos = new Vector3(p.x, p.y, p.z);
                switch (p.t)
                {
                    case "floor": Quad(root, "Floor", p.tex, pos, Quaternion.Euler(90, 0, 0), new Vector3(p.sx, p.sz, 1)); count++; break;
                    case "ceiling": Quad(root, "Ceiling", p.tex, pos, Quaternion.Euler(-90, 0, 0), new Vector3(p.sx, p.sz, 1)); count++; break;
                    case "wall": case "window": case "poster": case "door": case "swing": case "exit":
                        var g = Quad(root, p.t, p.tex, pos, Quaternion.Euler(0, p.ry, 0), new Vector3(p.sx, p.sy, 1));
                        var back = Quad(g.transform, "Back", p.tex, pos, Quaternion.Euler(0, p.ry + 180f, 0), Vector3.one);
                        back.transform.localScale = Vector3.one; back.GetComponent<MeshCollider>().enabled = false;
                        back.transform.localPosition = Vector3.zero; back.transform.localRotation = Quaternion.Euler(0, 180, 0);
                        count++; break;
                    case "box":
                        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        c.name = string.IsNullOrEmpty(p.name) ? "Box" : p.name; c.transform.SetParent(root, false);
                        c.transform.position = pos; c.transform.rotation = Quaternion.Euler(0, p.ry, 0); c.transform.localScale = new Vector3(p.sx, p.sy, p.sz);
                        c.GetComponent<MeshRenderer>().sharedMaterial = Mat(p.tex); count++; break;
                    case "notebook":
                        if (nbProto != null && books < 100)
                        {
                            var n = UnityEngine.Object.Instantiate(nbProto, root);
                            n.name = "ModNotebook" + books; n.transform.position = new Vector3(p.x, 4f, p.z); n.SetActive(true); books++;
                        }
                        break;
                    case "spawn": MoveSpawn(p); break;
                }
            }
            Debug.Log("[BaldiMod] Построено элементов: " + count + ", блокнотов: " + books + ". Теперь: Window → AI → Navigation → Bake. Двери и предметы нужно расставить из оригинальных префабов (см. README).");
            AssetDatabase.SaveAssets();
        }

        static void MoveSpawn(PieceData p)
        {
            string obj = null;
            switch (p.name)
            {
                case "player": obj = "Player"; break;
                case "baldi": obj = "Baldi"; break;
                case "tutor": obj = "TutorBaldi"; break;
                case "principal": obj = "Principal of the Thing"; break;
                case "playtime": obj = "Playtime"; break;
                case "sweep": obj = "Gotta Sweep"; break;
                case "bully": obj = "Its a Bully"; break;
                case "prize": obj = "1st Prize"; break;
                case "crafters": obj = "Arts and Crafters"; break;
            }
            if (obj == null) return;
            var go = FindIncludingInactive(obj);
            if (go != null) go.transform.position = new Vector3(p.x, go.transform.position.y, p.z);
        }

        /// <summary>Сборка из командной строки / GitHub Actions: настройки APK, школа, NavMesh, Android-билд.</summary>
        public static void BuildAll()
        {
            ApplyApk();
            var a = JsonUtility.FromJson<ApkData>(File.ReadAllText(Dir + "apk.json"));
            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
            if (a.buildSchool)
            {
                string school = scenes.Find(x => x.EndsWith("School.unity"));
                if (school != null)
                {
                    var sc = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(school);
                    BuildSchool();
                    UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(sc);
                }
            }
            Directory.CreateDirectory("build/Android");
            var opts = new BuildPlayerOptions { scenes = scenes.ToArray(), locationPathName = "build/Android/mod.apk", target = BuildTarget.Android, options = BuildOptions.None };
            var rep = BuildPipeline.BuildPlayer(opts);
            if (rep.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        static GameObject FindIncludingInactive(string name)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
            return null;
        }
    }
}
#endif
