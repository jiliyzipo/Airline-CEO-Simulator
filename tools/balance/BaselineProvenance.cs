using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AirlineCEO.Balance;

/// <summary>
/// 基线来源指纹与对比口径。
///
/// 解决的问题：`baseline/` 里的数字若没有版本标记，任何人都无法回答
/// 「这条基线是哪一版生成的、当前工作区能不能拿它比」。K13 之前 README 写的
/// 更新时间与实际文件时间差了近两个月，对比脚本只做整串相等判断 —— 一旦模型改过，
/// 它要么报「不一致」（读者以为是回归），要么在 ±25% 的偏差上照旧打印结果。
///
/// 口径：**能不能比，由输入文件的哈希决定，不由 commit 决定**。
/// commit 只是给人看的坐标；改了文档而没动模型时，commit 变了但基线仍然可比。
/// 反之，工作区脏着改模型时 commit 没变，但基线已不可比 —— 只有哈希能分辨这两种情况。
/// </summary>
public sealed class BaselineProvenance
{
    public const string FileName = "BASELINE_SOURCE.json";

    public int Seed { get; set; }
    public int Year { get; set; }
    public string GeneratedAt { get; set; } = "";
    public string Commit { get; set; } = "";
    public string Branch { get; set; } = "";
    public bool Dirty { get; set; }
    public string Delivery { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public int SourceFileCount { get; set; }
    public string SummarySha256 { get; set; } = "";
    public string Command { get; set; } = "";
    public string Note { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>项目目录：含 AirlineCEO.Balance.csproj 的目录。</summary>
    public static string LocateProjectDir()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AirlineCEO.Balance.csproj")))
                return dir.FullName;
            string nested = Path.Combine(dir.FullName, "tools", "balance", "AirlineCEO.Balance.csproj");
            if (File.Exists(nested))
                return Path.Combine(dir.FullName, "tools", "balance");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到 tools/balance/AirlineCEO.Balance.csproj（请在仓库内运行）");
    }

    /// <summary>仓库根：tools/balance 往上两级。</summary>
    public static string LocateRepoRoot(string projectDir)
    {
        var d = Directory.GetParent(projectDir);
        var root = d?.Parent;
        if (root == null) throw new InvalidOperationException("无法从 tools/balance 推导仓库根");
        return root.FullName;
    }

    /// <summary>
    /// 参与哈希的输入文件：工具自身的 .cs + csproj 显式链入的 Unity 逻辑源码 + csproj 本身。
    /// 不读 bin/obj/out —— 那些是产物，不是输入。
    /// </summary>
    public static List<string> SourceFiles(string projectDir)
    {
        var files = new List<string>();

        foreach (string f in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(projectDir, f).Replace('\\', '/');
            if (rel.StartsWith("bin/", StringComparison.Ordinal) ||
                rel.StartsWith("obj/", StringComparison.Ordinal) ||
                rel.StartsWith("out/", StringComparison.Ordinal))
                continue;
            files.Add(f);
        }

        string csproj = Path.Combine(projectDir, "AirlineCEO.Balance.csproj");
        if (File.Exists(csproj))
        {
            files.Add(csproj);
            string scriptsDir = Path.GetFullPath(Path.Combine(projectDir, "..", "..", "Unity Project", "Assets", "Scripts"));
            string text = File.ReadAllText(csproj);
            foreach (Match m in Regex.Matches(text, "<Compile\\s+Include=\"([^\"]+)\""))
            {
                string inc = m.Groups[1].Value.Replace("$(UnityScriptsDir)", scriptsDir);
                string full = Path.GetFullPath(Path.Combine(projectDir, inc));
                if (File.Exists(full)) files.Add(full);
            }
        }

        var repoRoot = LocateRepoRoot(projectDir);
        return files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>对输入集合算 SHA256：逐文件「相对路径 \0 内容 \0」。</summary>
    public static string ComputeSourceHash(string projectDir, out int count)
    {
        var repoRoot = LocateRepoRoot(projectDir);
        var files = SourceFiles(projectDir);
        using var sha = SHA256.Create();
        using var stream = new MemoryStream();
        foreach (string f in files)
        {
            string rel = Path.GetRelativePath(repoRoot, f).Replace('\\', '/');
            byte[] pathBytes = Encoding.UTF8.GetBytes(rel);
            stream.Write(pathBytes, 0, pathBytes.Length);
            stream.WriteByte(0);
            byte[] body = File.ReadAllBytes(f);
            stream.Write(body, 0, body.Length);
            stream.WriteByte(0);
        }
        count = files.Count;
        stream.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public static string Sha256OfFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static string RunGit(string repoRoot, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            string stdout = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return p.ExitCode == 0 ? stdout.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    public static BaselineProvenance Create(int seed, int year, string summaryPath, string delivery = "", string note = "")
    {
        string projectDir = LocateProjectDir();
        string repoRoot = LocateRepoRoot(projectDir);

        var stamp = new BaselineProvenance
        {
            Seed = seed,
            Year = year,
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Commit = RunGit(repoRoot, "rev-parse HEAD"),
            Branch = RunGit(repoRoot, "rev-parse --abbrev-ref HEAD"),
            Dirty = !string.IsNullOrEmpty(RunGit(repoRoot, "status --porcelain --untracked-files=no")),
            Delivery = delivery,
            SourceHash = ComputeSourceHash(projectDir, out int n),
            SourceFileCount = n,
            SummarySha256 = File.Exists(summaryPath) ? Sha256OfFile(summaryPath) : "",
            Command = $"dotnet run -- --seed {seed} --year {year} --out out",
            Note = note,
        };
        return stamp;
    }

    public static void Save(string dir, BaselineProvenance stamp)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, FileName),
            JsonSerializer.Serialize(stamp, JsonOpts) + Environment.NewLine, new UTF8Encoding(false));
    }

    public static BaselineProvenance? Load(string dir)
    {
        string path = Path.Combine(dir, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<BaselineProvenance>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>人读摘要：打印一份指纹时统一走这里，避免各处格式不一。</summary>
    public static string Describe(BaselineProvenance s)
    {
        string when = s.GeneratedAt.Length >= 10 ? s.GeneratedAt[..10] : s.GeneratedAt;
        string commit = s.Commit.Length >= 8 ? s.Commit[..8] : s.Commit;
        string dirty = s.Dirty ? " · **工作区脏**" : "";
        string del = string.IsNullOrEmpty(s.Delivery) ? "未关联交付" : s.Delivery;
        return $"seed={s.Seed} year={s.Year} · {when} · commit {commit}（{s.Branch}）{dirty} · {del} · 输入 {s.SourceFileCount} 文件 · hash {s.SourceHash[..Math.Min(12, s.SourceHash.Length)]}";
    }

    public sealed class FieldDiff
    {
        public string Key = "";
        public string Baseline = "";
        public string Current = "";
        public double Deviation = double.NaN; // 相对偏差；非数值字段为 NaN
        public bool Numeric;
    }

    public sealed class CompareResult
    {
        public bool Comparable;
        public string BlockReason = "";
        public readonly List<FieldDiff> OverTolerance = new();
        public readonly List<string> MissingInCurrent = new();
        public readonly List<string> ExtraInCurrent = new();
        public int MatchedKeys;
        public int NumericKeys;
        public double MaxDeviation = double.NaN;
        public string MaxDeviationKey = "";

        public bool Passed => Comparable && OverTolerance.Count == 0 &&
                              MissingInCurrent.Count == 0 && ExtraInCurrent.Count == 0;
    }

    private static Dictionary<string, string> ParseSummary(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line[..eq]] = line[(eq + 1)..];
        }
        return map;
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v);

    /// <summary>
    /// 对比当前摘要与基线。先判「能不能比」，再判「差多少」——
    /// 不可比时**不**输出百分比，因为那会把「换了模型」伪装成「回归了 N%」。
    /// </summary>
    public static CompareResult Compare(string baselineSummary, string currentSummary, BaselineProvenance? stamp,
                                        string projectDir, double tolerance = 0.03)
    {
        var result = new CompareResult();

        if (stamp == null || string.IsNullOrEmpty(stamp.SourceHash))
        {
            result.BlockReason = $"缺少 {FileName} 或其中没有 sourceHash：无法判断这条基线由哪一版生成";
            return result;
        }

        string currentHash = ComputeSourceHash(projectDir, out int count);
        if (!string.Equals(currentHash, stamp.SourceHash, StringComparison.OrdinalIgnoreCase))
        {
            result.BlockReason =
                $"基线输入哈希与当前工作区不一致（基线 {stamp.SourceHash[..12]} / 当前 {currentHash[..12]}，{stamp.SourceFileCount} → {count} 文件）。" +
                "模型已改动，这条基线不再可比；请重新录制（-UpdateBaseline），不要把它读成回归百分比";
            return result;
        }

        result.Comparable = true;
        var b = ParseSummary(baselineSummary);
        var c = ParseSummary(currentSummary);

        // 护栏：键名只允许标识符形状。没有这条，一行被破坏（例如末行丢换行导致两行粘连）
        // 会解析成一个合法但错误的键，进而把「文件坏了」显示成「某个字段漂移了 N%」。
        foreach (var kv in b.Concat(c))
        {
            if (!Regex.IsMatch(kv.Key, "^[A-Za-z0-9_]+$"))
            {
                result.Comparable = false;
                result.BlockReason = $"摘要格式损坏：键 `{kv.Key[..Math.Min(60, kv.Key.Length)]}` 不是合法字段名（疑似行粘连或丢换行）";
                return result;
            }
        }

        foreach (var kv in b)
        {
            if (!c.ContainsKey(kv.Key)) { result.MissingInCurrent.Add(kv.Key); continue; }
            string cv = c[kv.Key];
            var d = new FieldDiff { Key = kv.Key, Baseline = kv.Value, Current = cv };
            result.MatchedKeys++;

            if (TryNum(kv.Value, out double bv) && TryNum(cv, out double curv))
            {
                d.Numeric = true;
                result.NumericKeys++;
                double scale = Math.Max(Math.Abs(bv), Math.Abs(curv));
                double dev = scale < 1e-9 ? Math.Abs(curv - bv) : Math.Abs(curv - bv) / scale;
                d.Deviation = dev;
                if (double.IsNaN(result.MaxDeviation) || dev > result.MaxDeviation)
                {
                    result.MaxDeviation = dev;
                    result.MaxDeviationKey = kv.Key;
                }
                if (dev > tolerance) result.OverTolerance.Add(d);
            }
            else if (!string.Equals(kv.Value, cv, StringComparison.Ordinal))
            {
                result.OverTolerance.Add(d);
            }
        }

        foreach (var key in c.Keys)
            if (!b.ContainsKey(key)) result.ExtraInCurrent.Add(key);

        return result;
    }
}
