#if UNITY_EDITOR
using AirlineCEO.Data;
using AirlineCEO.Logic;
using UnityEditor;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>Phase 1.4：建档流 ScriptableObject 骨架。</summary>
    public static class CreateSetupProfileAssets
    {
        const string Folder = "Assets/ScriptableObjects/Setup";

        [MenuItem("AirlineCEO/Phase 1/Create Setup Profile Assets")]
        public static void CreateAll()
        {
            Ensure("Assets", "ScriptableObjects");
            Ensure("Assets/ScriptableObjects", "Setup");

            var player = LoadOrCreate<PlayerProfile>($"{Folder}/DefaultPlayerProfile.asset");
            player.playerName = "机长";
            player.nationality = "中国";
            EditorUtility.SetDirty(player);

            var company = LoadOrCreate<CompanyProfile>($"{Folder}/DefaultCompanyProfile.asset");
            company.companyNameZh = "新星航空";
            company.icaoCode = "NVA";
            company.hubIata = "PEK";
            company.initialCashWan = 500f;
            EditorUtility.SetDirty(company);

            CreateDifficulty("Difficulty_Easy", DifficultyMode.Easy, 1.25f, 0.8f, "宽松：启动资金更高，事件更温和");
            CreateDifficulty("Difficulty_Normal", DifficultyMode.Normal, 1f, 1f, "标准难度");
            CreateDifficulty("Difficulty_Hard", DifficultyMode.Hard, 0.8f, 1.25f, "严苛：资金更紧，事件更重");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Setup Profiles", $"已写入 {Folder}", "确定");
        }

        static void CreateDifficulty(string file, DifficultyMode mode, float cashMul, float eventMul, string desc)
        {
            var d = LoadOrCreate<DifficultyConfig>($"{Folder}/{file}.asset");
            d.mode = mode;
            d.startingCashMultiplier = cashMul;
            d.eventSeverityMultiplier = eventMul;
            d.description = desc;
            EditorUtility.SetDirty(d);
        }

        static void Ensure(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
    }
}
#endif
