// Baldi Mod Maker — рантайм-скрипт. Положите в Assets/Scripts/Mod/ (пакет уже содержит нужный путь).
// Читает Resources/mod_settings.txt и применяет настройки мода к сцене School и MainMenu.
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BaldiMod
{
    public class ModRuntime : MonoBehaviour
    {
        static Dictionary<string, string> S = new Dictionary<string, string>();
        static List<string[]> binds = new List<string[]>();
        GameControllerScript gc;
        NotebookScript[] notebooks;
        float scan, startTime;
        AudioSource chase;
        bool timeDone;
        float baseSchool = 1f, baseLearn = 1f;
        Dictionary<NotebookScript, float> openDist = new Dictionary<NotebookScript, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("BaldiModRuntime");
            DontDestroyOnLoad(go);
            go.AddComponent<ModRuntime>();
        }

        void Awake()
        {
            var ta = Resources.Load<TextAsset>("mod_settings");
            if (ta != null)
            {
                foreach (var raw in ta.text.Split('\n'))
                {
                    var line = raw.Trim('\r', ' ');
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    if (k.StartsWith("bind:")) { var cf = k.Substring(5).Split('.'); if (cf.Length == 2) binds.Add(new[] { cf[0], cf[1], v }); }
                    else S[k] = v;
                }
            }
            SceneManager.sceneLoaded += OnLoaded;
        }

        // ---------- helpers ----------
        static float F(string k, float d) { string v; float r; return S.TryGetValue(k, out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : d; }
        static int I(string k, int d) { return Mathf.RoundToInt(F(k, d)); }
        static bool B(string k, bool d) { string v; return S.TryGetValue(k, out v) ? v == "1" : d; }
        static string Str(string k) { string v; return S.TryGetValue(k, out v) ? v : ""; }
        static Color C(string k, Color d) { Color c; return ColorUtility.TryParseHtmlString(Str(k), out c) ? c : d; }

        void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            gc = null; notebooks = null; chase = null; timeDone = false; startTime = Time.time;
            if (scene.name == "School") StartCoroutine(ApplySchool());
            else if (scene.name == "MainMenu") StartCoroutine(ApplyMenu());
        }

        // ---------- школа ----------
        IEnumerator ApplySchool()
        {
            yield return null;
            gc = FindObjectOfType<GameControllerScript>();
            if (gc == null) yield break;
            ApplyBindings();
            if (B("gp.tap_solve", false))
            {
                // оригинальное открытие математики блокируем (дистанция 0), нажатие обрабатываем сами
                foreach (var nb in FindObjectsOfType<NotebookScript>()) { openDist[nb] = nb.openingDistance > 0f ? nb.openingDistance : 10f; nb.openingDistance = 0f; }
            }
            ApplyCharacter("baldi", gc.baldi); ApplyCharacter("principal", gc.principal); ApplyCharacter("playtime", gc.playtime);
            ApplyCharacter("sweep", gc.gottaSweep); ApplyCharacter("bully", gc.bully); ApplyCharacter("prize", gc.firstPrize); ApplyCharacter("crafters", gc.crafters);
            if (gc.baldiScrpt != null) { gc.baldiScrpt.baldiAnger = F("baldi.start_anger", gc.baldiScrpt.baldiAnger); }
            if (gc.playerCamera != null)
            {
                gc.playerCamera.fieldOfView = F("pl.fov", gc.playerCamera.fieldOfView);
                if (B("wo.fog", false)) gc.playerCamera.backgroundColor = C("wo.sky_color", gc.playerCamera.backgroundColor);
            }
            AudioListener.volume = F("au.master", 1f);
            if (gc.schoolMusic != null) { gc.schoolMusic.volume *= F("au.school_volume", 1f); var sm = Resources.Load<AudioClip>("mod_school_music"); if (sm != null) { gc.schoolMusic.clip = sm; gc.schoolMusic.Play(); } }
            if (gc.learnMusic != null) { gc.learnMusic.volume *= F("au.learn_volume", 1f); var lm = Resources.Load<AudioClip>("mod_learn_music"); if (lm != null) gc.learnMusic.clip = lm; }
            RenderSettings.ambientLight = C("wo.ambient_color", Color.white) * F("wo.ambient", 1f);
            RenderSettings.fog = B("wo.fog", false);
            RenderSettings.fogColor = C("wo.fog_color", Color.black);
            RenderSettings.fogDensity = F("wo.fog_density", 0.02f);
            RenderSettings.fogMode = FogMode.Exponential;
            Physics.gravity = new Vector3(0, -9.81f * F("wo.gravity", 1f), 0);
            if (!B("gp.baldi_tutor", true) && gc.baldiTutor != null) gc.baldiTutor.SetActive(false);
            if (!B("gp.show_hud", true)) { var cv = FindObjectOfType<Canvas>(); if (cv != null) cv.enabled = false; }
        }

        void ApplyBindings()
        {
            var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            foreach (var b in binds)
            {
                foreach (var o in all)
                {
                    if (o == null || !o.gameObject.scene.IsValid() || o.GetType().Name != b[0]) continue;
                    var f = o.GetType().GetField(b[1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f == null) continue;
                    try
                    {
                        if (f.FieldType == typeof(float)) f.SetValue(o, float.Parse(b[2], CultureInfo.InvariantCulture));
                        else if (f.FieldType == typeof(int)) f.SetValue(o, Mathf.RoundToInt(float.Parse(b[2], CultureInfo.InvariantCulture)));
                        else if (f.FieldType == typeof(bool)) f.SetValue(o, b[2] == "1");
                    }
                    catch { }
                }
            }
        }

        void ApplyCharacter(string p, GameObject go)
        {
            if (go == null) return;
            float scale = F(p + ".scale", 1f);
            if (Mathf.Abs(scale - 1f) > 0.001f) go.transform.localScale *= scale;
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.speed *= F(p + ".speed_mult", 1f);
                agent.acceleration *= F(p + ".accel", 1f);
                agent.angularSpeed *= F(p + ".turn", 1f);
            }
            foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) a.volume *= F(p + ".volume", 1f);
            if (!B(p + ".show_sprite", true)) foreach (var r in go.GetComponentsInChildren<SpriteRenderer>(true)) r.enabled = false;
        }

        void Update()
        {
            if (gc == null) return;
            // выключенные персонажи
            Disable("baldi", gc.baldi); Disable("principal", gc.principal); Disable("playtime", gc.playtime);
            Disable("sweep", gc.gottaSweep); Disable("bully", gc.bully); Disable("prize", gc.firstPrize); Disable("crafters", gc.crafters);
            InstantNotebook();
            TapSolve();
            ChaseMusic();
            TimeLimit();
        }

        void Disable(string p, GameObject go) { if (go != null && !B(p + ".enabled", true) && go.activeSelf) go.SetActive(false); }

        void InstantNotebook()
        {
            if (!B("gp.instant_notebook", false)) return;
            if (notebooks == null || Time.time > scan) { notebooks = FindObjectsOfType<NotebookScript>(); scan = Time.time + 1.5f; }
            float r = F("gp.instant_radius", 12f);
            foreach (var nb in notebooks)
            {
                if (nb == null || !nb.up) continue;
                Vector3 d = nb.transform.position - gc.playerTransform.position; d.y = 0;
                if (d.magnitude > r) continue;
                if (!B("gp.instant_through_walls", false))
                {
                    RaycastHit h;
                    if (Physics.Linecast(gc.playerTransform.position, nb.transform.position, out h) && !h.transform.IsChildOf(nb.transform) && h.transform != nb.transform && h.transform != gc.playerTransform) continue;
                }
                Collect(nb);
            }
        }

        void TapSolve()
        {
            if (!B("gp.tap_solve", false) || Time.timeScale == 0f) return;
            if (!Singleton<InputManager>.Instance.GetActionKey(InputAction.Interact)) return;
            var cam = Camera.main; if (cam == null) return;
            RaycastHit h;
            if (!Physics.Raycast(cam.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2, 0f)), out h) || h.transform.tag != "Notebook") return;
            var nb = h.transform.GetComponent<NotebookScript>();
            if (nb == null || !nb.up) return;
            float d; if (!openDist.TryGetValue(nb, out d)) d = 10f;
            if (Vector3.Distance(gc.playerTransform.position, nb.transform.position) < d) Collect(nb);
        }

        void Collect(NotebookScript nb)
        {
            var pos = nb.transform.position;
            nb.transform.position = new Vector3(pos.x, -20f, pos.z);
            nb.up = false; nb.respawnTime = F("gp.notebook_respawn_time", 120f);
            if (B("gp.instant_sound", true) && nb.audioDevice != null) nb.audioDevice.Play();
            gc.CollectNotebook();
            if (B("gp.instant_math_after", false))
            {
                var g = Instantiate(nb.learningGame);
                var m = g.GetComponent<MathGameScript>(); m.gc = gc; m.baldiScript = nb.bsc; m.playerPosition = gc.playerTransform.position;
                return;
            }
            if (I("gp.instant_mode", 0) == 1)
            {
                if (!gc.spoopMode) gc.ActivateSpoopMode();
                gc.baldiScrpt.GetAngry(F("gp.wrong_answer_anger", 1f));
            }
            var dummy = new GameObject("ModDummy");
            gc.DeactivateLearningGame(dummy);
        }

        void ChaseMusic()
        {
            var clip = Resources.Load<AudioClip>("mod_chase");
            if (clip == null) return;
            int mode = I("au.chase_start", 0);
            bool want = mode == 0 ? gc.spoopMode : mode == 1 ? gc.notebooks >= 1 :
                mode == 2 ? (gc.spoopMode && gc.baldi != null && Vector3.Distance(gc.baldi.transform.position, gc.playerTransform.position) < F("au.chase_near_dist", 60f)) : true;
            if (B("au.chase_stop_on_catch", true) && gc.player != null && gc.player.gameOver) want = false;
            if (chase == null)
            {
                chase = gameObject.AddComponent<AudioSource>();
                chase.clip = clip; chase.loop = B("au.chase_loop", true); chase.volume = 0f; chase.playOnAwake = false;
            }
            float target = want ? F("au.chase_volume", 0.8f) : 0f;
            float fade = want ? F("au.chase_fade_in", 1f) : F("au.chase_fade_out", 1f);
            chase.volume = fade <= 0.01f ? target : Mathf.MoveTowards(chase.volume, target, Time.deltaTime / fade * Mathf.Max(0.01f, F("au.chase_volume", 0.8f)));
            float pitch = F("au.chase_pitch", 1f);
            if (B("au.chase_pitch_by_anger", false) && gc.baldiScrpt != null) pitch += Mathf.Clamp(gc.baldiScrpt.baldiAnger * 0.03f, 0f, 0.6f);
            chase.pitch = pitch;
            if (want && !chase.isPlaying) chase.Play();
            if (!want && chase.isPlaying && chase.volume <= 0.001f) chase.Stop();
            if (want && B("au.chase_replace_default", true))
            {
                if (gc.schoolMusic != null) gc.schoolMusic.volume = 0f;
                if (gc.learnMusic != null) gc.learnMusic.volume = 0f;
            }
        }

        void TimeLimit()
        {
            if (timeDone || !B("gp.time_limit_on", false) || Time.timeScale == 0f) return;
            if (Time.time - startTime < F("gp.time_limit", 10f) * 60f) return;
            timeDone = true;
            int a = I("gp.on_time_out", 0);
            if (a == 0) SceneManager.LoadScene(gc.GameOverScene);
            else if (a == 1 && gc.baldiScrpt != null) gc.baldiScrpt.GetAngry(F("baldi.max_anger", 20f));
            else { gc.entrance_0.Raise(); gc.entrance_1.Raise(); gc.entrance_2.Raise(); gc.entrance_3.Raise(); }
        }

        // ---------- главное меню ----------
        IEnumerator ApplyMenu()
        {
            yield return null;
            var cam = Camera.main;
            if (cam != null && B("menu.bg_override", false)) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = C("menu.bg_color", Color.black); }
            Button("StoryButton", "menu.story");
            Button("EndlessButton", "menu.endless");
            Button("OptionsButton", "menu.options");
            Button("CreditsButton", "menu.credits");
            Button("Exit Button", "menu.exit");
            var title = GameObject.Find("TitleImage");
            if (title != null)
            {
                if (!B("menu.title_show", true)) title.SetActive(false);
                title.transform.localScale *= F("menu.title_scale", 1f);
                title.transform.localPosition += new Vector3(0, F("menu.title_offset_y", 0f), 0);
                var img = title.GetComponent<Image>(); if (img != null) img.color = C("menu.title_tint", Color.white);
                string sub = Str("menu.subtitle");
                if (sub.Length > 0)
                {
                    var go = new GameObject("ModSubtitle", typeof(RectTransform), typeof(Text));
                    go.transform.SetParent(title.transform.parent, false);
                    go.transform.position = title.transform.position + new Vector3(0, -F("menu.subtitle_offset", 140f), 0);
                    var t = go.GetComponent<Text>();
                    t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    t.text = sub; t.fontSize = I("menu.subtitle_size", 36); t.alignment = TextAnchor.MiddleCenter; t.color = C("menu.subtitle_color", Color.white);
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
            }
            var mm = Resources.Load<AudioClip>("mod_menu_music");
            foreach (var a in FindObjectsOfType<AudioSource>())
            {
                if (mm != null && a.loop) { a.clip = mm; a.Play(); }
                a.volume *= F("menu.music_volume", 1f); a.pitch = F("menu.music_pitch", 1f);
            }
        }

        void Button(string objName, string p)
        {
            var go = GameObject.Find(objName);
            if (go == null) return;
            if (!B(p + "_show", true)) { go.SetActive(false); return; }
            string txt = Str(p + "_text");
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) { if (txt.Length > 0) t.text = txt; t.color = C("menu.text_color", t.color); }
            foreach (var t in go.GetComponentsInChildren<Text>(true)) { if (txt.Length > 0) t.text = txt; t.color = C("menu.text_color", t.color); }
            var img = go.GetComponent<Image>(); if (img != null) img.color = C("menu.btn_color", img.color);
            go.transform.localScale *= F("menu.btn_scale", 1f);
        }
    }
}
