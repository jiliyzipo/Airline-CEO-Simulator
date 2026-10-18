#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using AirlineCEO.Systems;
using AirlineCEO.Logic;
using AirlineCEO.UI;
using AirlineCEO.UI.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AirlineCEO.EditorTools
{
    [InitializeOnLoad]
    public static class DevelopmentPlaySmoke
    {
        const string Pending = "AirlineCEO.DevelopmentSmoke.Pending";
        static int frames;
        static bool enteredSetup;
        static int stage;
        static readonly HashSet<string> selectedDirectors = new HashSet<string>();
        static readonly List<string> runtimeErrors = new List<string>();
        static DevelopmentPlaySmoke()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, kind) =>
            {
                if (SessionState.GetBool(Pending, false)
                    && (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
                    && (stack.Contains("AirlineCEO.") || stack.Contains("Assets/Scripts/")))
                    runtimeErrors.Add(message);
            };
        }

        public static void Run()
            => Begin(false);

        public static void RunTakeover()
            => Begin(true);

        static void Begin(bool takeover)
        {
            if (Application.companyName != "AirlineCEORegression")
                throw new Exception("Smoke must run in the isolated baseline export to protect player data.");
            var scene = EditorBuildSettings.scenes.FirstOrDefault(x => x.enabled);
            if (scene == null || scene.path != "Assets/MVP V1.0.unity")
                throw new Exception("Default build scene must be the gameplay scene.");
            EditorSceneManager.OpenScene(scene.path);
            frames = 0; stage = 0; enteredSetup = false; selectedDirectors.Clear(); runtimeErrors.Clear();
            SessionState.SetBool(Pending, true);
            SessionState.SetBool(Pending + ".takeover", takeover);
            SessionState.SetString(Pending + ".started", DateTime.UtcNow.ToString("O"));
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            try
            {
                if (DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Pending + ".started", ""),
                    null, System.Globalization.DateTimeStyles.RoundtripKind) > TimeSpan.FromMinutes(3))
                    throw new Exception("Smoke timeout waiting for Play Mode UI.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || ++frames < 8) return;
                ExternalDataFetcher.Enabled = false; ExternalDataFetcher.QuoteEnabled = false;
                if (UnityEngine.Object.FindFirstObjectByType<MVPBootstrap>() == null
                    || SetupFlowController.Instance == null || GameState.Instance == null)
                    throw new Exception("Gameplay bootstrap or setup state did not initialize.");
                if (!enteredSetup)
                {
                    var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                        .FirstOrDefault(b => b.interactable && b.GetComponentsInChildren<TMPro.TMP_Text>()
                            .Any(t => t.text.Contains(SessionState.GetBool(Pending + ".takeover", false) ? "新游戏 · 接管" : "从零创建")));
                    if (button == null) throw new Exception("Main menu new-airline button missing.");
                    button.onClick.Invoke();
                    enteredSetup = true;
                    frames = 0;
                    Debug.Log("SMOKE_MENU_PASS: clicked actual new-game button");
                    return;
                }
                if (stage == 0)
                {
                    Fill("I_姓名", "Baseline CEO"); Click("Next"); stage = 1; frames = 0; return;
                }
                if (stage == 1)
                {
                    if (SessionState.GetBool(Pending + ".takeover", false))
                    { Click("Next"); stage = 5; frames = 0; return; }
                    Fill("I_公司中文名", "基线航空"); Fill("I_公司英文名", "Baseline Air");
                    Fill("I_IATA代码", "Z9"); Fill("I_ICAO代码", "ZBL");
                    Click("Next"); stage = 2; frames = 0; return;
                }
                if (stage == 2)
                {
                    if (selectedDirectors.Count < 8)
                    {
                        var director = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                            .FirstOrDefault(b => b.name.StartsWith("Dir_") && !selectedDirectors.Contains(b.name));
                        if (director == null) throw new Exception("No board candidate available.");
                        selectedDirectors.Add(director.name); director.onClick.Invoke(); frames = 0; return;
                    }
                    Click("Next"); stage = 3; frames = 0; return;
                }
                if (stage == 3) { Click("Start"); stage = 4; frames = 0; return; }
                if (stage == 5) { Click("Go"); stage = 4; frames = 0; return; }
                if (!SetupFlowController.Instance.IsInGameplay
                    || UnityEngine.Object.FindFirstObjectByType<PrefabNavIntegration>()?.GameplayReady != true)
                    throw new Exception("New-game flow did not enter actual gameplay.");
                if (runtimeErrors.Count > 0) throw new Exception("Runtime errors during startup: " + string.Join("; ", runtimeErrors));
                Debug.Log("SMOKE_PASS: default scene -> main menu -> profile -> "
                    + (SessionState.GetBool(Pending + ".takeover", false) ? "takeover overview" : "company -> board -> goals")
                    + " -> actual gameplay");
                SessionState.SetBool(Pending, false);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                SessionState.SetBool(Pending, false);
                EditorApplication.Exit(1);
            }
        }

        static void Fill(string name, string value)
        {
            var field = UnityEngine.Object.FindObjectsByType<TMPro.TMP_InputField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.name == name);
            if (field == null) throw new Exception("Missing setup field: " + name);
            field.text = value;
        }

        static void Click(string name)
        {
            var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.name == name && x.interactable
                    && x.transform.parent != null && x.transform.parent.name == "Actions");
            if (button == null) throw new Exception("Missing setup action: " + name);
            button.onClick.Invoke();
        }
    }
}
#endif
