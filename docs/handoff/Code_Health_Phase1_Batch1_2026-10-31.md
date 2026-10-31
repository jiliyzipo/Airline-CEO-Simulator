# Phase 1 首批流程演练 · 2026-10-31

**已实现，待复验。目标为零、仅验证流程（shakedown）；不计入 Phase 1 的指标进度，不宣称结构优化已开始。**

## 范围、授权与基线

用户确认共享实现放 RegressionTriggerBridge，两个同级 regression 仅保留 private 转发；ReadField 删除采用 Cursor 已独立复验的结论，不重新要求对方举证。清单原称3源码但明确点名4个；本批按明确4个源码执行，文档1个、任务记录1个，合计6个人工/任务文件。自动生成的DEL、events、CURRENT为协作流水记录；本轮仅生成1个DEL，不修改既有历史。

基线 HEAD d0966c2，ack revision 833。行数目标已由用户在别处关闭（239227→删除76340行，tools 109198→32870），本批不为它计功。千行文件25→25；导入/sync未触及。out后续比较只采用用户已重录的7.6 GiB /149269文件/21目录，旧119.79 GiB为历史采样；本批不清理out。PNG保持现状。

## 文件与为何等价

- Unity Project/Assets/Scripts/UI/Setup/SetupFlowController.cs：仅删除未调用的ReadField；保留TMPro及仍使用它的代码，无调用路径变化。
  - before SHA256 b07463d2d455a534d974c7b74f783f798d48526963509a25fc6fe771db5c2e81
  - after SHA256 c08e99f902bacf95b55a3533664a444b415e6ca562f7319ad62563eae6c30fc3
- Unity Project/Assets/Scripts/Editor/RegressionTriggerBridge.cs：仅加入internal static Trim，原函数体原样迁入；无static字段、构造、轮询、白名单或事件订阅变化；仍在UNITY_EDITOR内。
  - before SHA256 04fde0044cae71763429c047cc5a7ab5cb404678b05d754b9287de50bafe11f4
  - after SHA256 f60a70e9eda6b8659081e8e6df653a11ed966cbbe2accb1f3e36aecbc72c1f7f
- Unity Project/Assets/Scripts/Editor/CeoCommandRegression.cs：只替换private Trim的函数体为单次转发；签名、参数、调用点、日志、断言和执行顺序不变。
  - before SHA256 b9d6d9f51ae3424c76efbe81e5b76c0a679a1327c6fda7c2880b807991b6cba4
  - after SHA256 8408908b461c8007d209057b03c534b163ccea074e103d998b349bfa4b9a3dc3
- Unity Project/Assets/Scripts/Editor/OpenRouteStudyRegression.cs：只替换private Trim的函数体为单次转发；签名、参数、调用点、日志、断言和执行顺序不变。
  - before SHA256 227be70be0fdf129139102076c842ef9b4832c0a2a7cfa285e9eecc2fa7ac715
  - after SHA256 d746414b8894a89f442fdf3b21abd0d4f3c542860410dfc9c2c4f33354b80fcd
- docs/handoff/Code_Health_Phase1_Batch1_2026-10-31.md：本说明与证据，非运行输入。
- docs/collaboration/tasks/S46-CodeHealthShakedown.json：本批范围、机器AC和快照依赖定义，不改已有任务。

## 删除证据

ReadField：采信Cursor的 `git grep -n 'ReadField' -- '*.cs'` 结果，全仓仅1行定义、零调用。TMPro仍由ContentType使用，不删除using。仅删除以下方法及其紧邻空行：

```csharp
static string ReadField(TMP_InputField field, string fallback)
{
    if (field == null || string.IsNullOrWhiteSpace(field.text)) return fallback;
    return field.text.Trim();
}
```

本批不删除其余private helper；两处Trim继续保留private签名。真实反射入口ShowNavPanel/EnterGameplay不触及；不改Serializable字段/类型、公开组件API、UnityEvent、生命周期、断言、资源、meta或GUID。

## 方法体前后并列与逐行去向

### Unity Project/Assets/Scripts/Editor/CeoCommandRegression.cs

| 删除前private方法体 | 修改后private方法体 | 原语句去向 |
|---|---|---|
| `if (string.IsNullOrEmpty(s)) return "";` | `return RegressionTriggerBridge.Trim(s, max);` | 共享Trim第一句，原样保留 |
| `s = s.Replace("\r", " ").Replace("\n", " ").Trim();` | 同上单次转发 | 共享Trim第二句，Replace/Trim顺序不变 |
| `return s.Length <= max ? s : s.Substring(0, max) + "…";` | 同上单次转发 | 共享Trim第三句，U+2026、UTF-16截断与异常策略不变 |

### Unity Project/Assets/Scripts/Editor/OpenRouteStudyRegression.cs

| 删除前private方法体 | 修改后private方法体 | 原语句去向 |
|---|---|---|
| `if (string.IsNullOrEmpty(s)) return "";` | `return RegressionTriggerBridge.Trim(s, max);` | 共享Trim第一句，原样保留 |
| `s = s.Replace("\r", " ").Replace("\n", " ").Trim();` | 同上单次转发 | 共享Trim第二句，Replace/Trim顺序不变 |
| `return s.Length <= max ? s : s.Substring(0, max) + "…";` | 同上单次转发 | 共享Trim第三句，U+2026、UTF-16截断与异常策略不变 |

共享实现：

```csharp
internal static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
```

程序化检查：迁入的方法体与两份原方法体逐字节一致；新私有包装只单次转发。共享桥已经由InitializeOnLoad在编辑器域初始化，未新增初始化动作或改变注册时序。新增的转发栈帧不等于承诺异常stacktrace文本字节不变；业务输出、异常类型和参数保持一致。

四个原调用点（原行383/451/573/97，仅因编辑可能发生行号偏移）：

```csharp
                          + (string.IsNullOrEmpty(laterMsg) ? "" : $"（回执：{Trim(laterMsg, 60)}）"));
```
```csharp
                string dispWhy = capDispatch ? "" : $"（topic={dispAns.ask.topic ?? "null"} · 回执={Trim(dispReceipt, 40)}）";
```
```csharp
                                            + $" · note={Trim(legAns.note, 48)}）";
```
```csharp
                Debug.Log($"开航纪要单一出处 → 先否决后算账={rejectOk}（{Trim(bad.reject, 40)}）"
```
对这些整行进行了before/after字符串比较，均相同。调用顺序与上限分别60/40/48/40（按上述源码顺序），没有修改任何断言、日志正文或动作。

## C#输出对照

17种输入×12个max（-1/0/1/2/3/4/5/8/40/48/60/200）=204组。每组比较旧Ceo、旧Open、实际新共享函数体与两个实际新转发体，全部按UTF-16码元序列相等；异常比较类型与ArgumentException.ParamName。函数体从修改前保存字节与修改后源码提取后编译，不是手写一份近似JS实现。此独立小程序只验证纯文本函数，不模拟Unity静态初始化；实际程序集编译和隔离引擎回归另列。

覆盖null、空串、纯空白、仅CR、仅LF、CR/LF混合、长度恰等/多1/远大于max、max=0、首尾空格、内部多空格、中文、完整与被切断的代理对、孤立高/低代理、NBSP。未修复既有Substring切断代理对的行为。

| 输入 | max | 五路共同输出（UTF-16十六进制码元，空白表示空串；或异常） | 一致 |
|---|---:|---|---|
| null | -1 | (empty) | true |
| null | 0 | (empty) | true |
| null | 1 | (empty) | true |
| null | 2 | (empty) | true |
| null | 3 | (empty) | true |
| null | 4 | (empty) | true |
| null | 5 | (empty) | true |
| null | 8 | (empty) | true |
| null | 40 | (empty) | true |
| null | 48 | (empty) | true |
| null | 60 | (empty) | true |
| null | 200 | (empty) | true |
| empty | -1 | (empty) | true |
| empty | 0 | (empty) | true |
| empty | 1 | (empty) | true |
| empty | 2 | (empty) | true |
| empty | 3 | (empty) | true |
| empty | 4 | (empty) | true |
| empty | 5 | (empty) | true |
| empty | 8 | (empty) | true |
| empty | 40 | (empty) | true |
| empty | 48 | (empty) | true |
| empty | 60 | (empty) | true |
| empty | 200 | (empty) | true |
| space | -1 | System.ArgumentOutOfRangeException:length | true |
| space | 0 | (empty) | true |
| space | 1 | (empty) | true |
| space | 2 | (empty) | true |
| space | 3 | (empty) | true |
| space | 4 | (empty) | true |
| space | 5 | (empty) | true |
| space | 8 | (empty) | true |
| space | 40 | (empty) | true |
| space | 48 | (empty) | true |
| space | 60 | (empty) | true |
| space | 200 | (empty) | true |
| tab | -1 | System.ArgumentOutOfRangeException:length | true |
| tab | 0 | (empty) | true |
| tab | 1 | (empty) | true |
| tab | 2 | (empty) | true |
| tab | 3 | (empty) | true |
| tab | 4 | (empty) | true |
| tab | 5 | (empty) | true |
| tab | 8 | (empty) | true |
| tab | 40 | (empty) | true |
| tab | 48 | (empty) | true |
| tab | 60 | (empty) | true |
| tab | 200 | (empty) | true |
| cr | -1 | System.ArgumentOutOfRangeException:length | true |
| cr | 0 | (empty) | true |
| cr | 1 | (empty) | true |
| cr | 2 | (empty) | true |
| cr | 3 | (empty) | true |
| cr | 4 | (empty) | true |
| cr | 5 | (empty) | true |
| cr | 8 | (empty) | true |
| cr | 40 | (empty) | true |
| cr | 48 | (empty) | true |
| cr | 60 | (empty) | true |
| cr | 200 | (empty) | true |
| lf | -1 | System.ArgumentOutOfRangeException:length | true |
| lf | 0 | (empty) | true |
| lf | 1 | (empty) | true |
| lf | 2 | (empty) | true |
| lf | 3 | (empty) | true |
| lf | 4 | (empty) | true |
| lf | 5 | (empty) | true |
| lf | 8 | (empty) | true |
| lf | 40 | (empty) | true |
| lf | 48 | (empty) | true |
| lf | 60 | (empty) | true |
| lf | 200 | (empty) | true |
| mixed | -1 | System.ArgumentOutOfRangeException:length | true |
| mixed | 0 | 2026 | true |
| mixed | 1 | 0041 2026 | true |
| mixed | 2 | 0041 0020 2026 | true |
| mixed | 3 | 0041 0020 0020 2026 | true |
| mixed | 4 | 0041 0020 0020 0042 2026 | true |
| mixed | 5 | 0041 0020 0020 0042 0020 2026 | true |
| mixed | 8 | 0041 0020 0020 0042 0020 0043 0020 0044 | true |
| mixed | 40 | 0041 0020 0020 0042 0020 0043 0020 0044 | true |
| mixed | 48 | 0041 0020 0020 0042 0020 0043 0020 0044 | true |
| mixed | 60 | 0041 0020 0020 0042 0020 0043 0020 0044 | true |
| mixed | 200 | 0041 0020 0020 0042 0020 0043 0020 0044 | true |
| exact | -1 | System.ArgumentOutOfRangeException:length | true |
| exact | 0 | 2026 | true |
| exact | 1 | 0061 2026 | true |
| exact | 2 | 0061 0062 2026 | true |
| exact | 3 | 0061 0062 0063 2026 | true |
| exact | 4 | 0061 0062 0063 0064 | true |
| exact | 5 | 0061 0062 0063 0064 | true |
| exact | 8 | 0061 0062 0063 0064 | true |
| exact | 40 | 0061 0062 0063 0064 | true |
| exact | 48 | 0061 0062 0063 0064 | true |
| exact | 60 | 0061 0062 0063 0064 | true |
| exact | 200 | 0061 0062 0063 0064 | true |
| long | -1 | System.ArgumentOutOfRangeException:length | true |
| long | 0 | 2026 | true |
| long | 1 | 0078 2026 | true |
| long | 2 | 0078 0078 2026 | true |
| long | 3 | 0078 0078 0078 2026 | true |
| long | 4 | 0078 0078 0078 0078 2026 | true |
| long | 5 | 0078 0078 0078 0078 0078 2026 | true |
| long | 8 | 0078 0078 0078 0078 0078 0078 0078 0078 2026 | true |
| long | 40 | 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 2026 | true |
| long | 48 | 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 2026 | true |
| long | 60 | 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 2026 | true |
| long | 200 | 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 0078 | true |
| outer | -1 | System.ArgumentOutOfRangeException:length | true |
| outer | 0 | 2026 | true |
| outer | 1 | 0061 2026 | true |
| outer | 2 | 0061 0062 2026 | true |
| outer | 3 | 0061 0062 0063 | true |
| outer | 4 | 0061 0062 0063 | true |
| outer | 5 | 0061 0062 0063 | true |
| outer | 8 | 0061 0062 0063 | true |
| outer | 40 | 0061 0062 0063 | true |
| outer | 48 | 0061 0062 0063 | true |
| outer | 60 | 0061 0062 0063 | true |
| outer | 200 | 0061 0062 0063 | true |
| internal | -1 | System.ArgumentOutOfRangeException:length | true |
| internal | 0 | 2026 | true |
| internal | 1 | 0061 2026 | true |
| internal | 2 | 0061 0020 2026 | true |
| internal | 3 | 0061 0020 0020 2026 | true |
| internal | 4 | 0061 0020 0020 0020 2026 | true |
| internal | 5 | 0061 0020 0020 0020 0062 | true |
| internal | 8 | 0061 0020 0020 0020 0062 | true |
| internal | 40 | 0061 0020 0020 0020 0062 | true |
| internal | 48 | 0061 0020 0020 0020 0062 | true |
| internal | 60 | 0061 0020 0020 0020 0062 | true |
| internal | 200 | 0061 0020 0020 0020 0062 | true |
| zh | -1 | System.ArgumentOutOfRangeException:length | true |
| zh | 0 | 2026 | true |
| zh | 1 | 4E2D 2026 | true |
| zh | 2 | 4E2D 6587 2026 | true |
| zh | 3 | 4E2D 6587 6D4B 2026 | true |
| zh | 4 | 4E2D 6587 6D4B 8BD5 | true |
| zh | 5 | 4E2D 6587 6D4B 8BD5 | true |
| zh | 8 | 4E2D 6587 6D4B 8BD5 | true |
| zh | 40 | 4E2D 6587 6D4B 8BD5 | true |
| zh | 48 | 4E2D 6587 6D4B 8BD5 | true |
| zh | 60 | 4E2D 6587 6D4B 8BD5 | true |
| zh | 200 | 4E2D 6587 6D4B 8BD5 | true |
| pair | -1 | System.ArgumentOutOfRangeException:length | true |
| pair | 0 | 2026 | true |
| pair | 1 | 0041 2026 | true |
| pair | 2 | 0041 D83D 2026 | true |
| pair | 3 | 0041 D83D DE00 2026 | true |
| pair | 4 | 0041 D83D DE00 005A | true |
| pair | 5 | 0041 D83D DE00 005A | true |
| pair | 8 | 0041 D83D DE00 005A | true |
| pair | 40 | 0041 D83D DE00 005A | true |
| pair | 48 | 0041 D83D DE00 005A | true |
| pair | 60 | 0041 D83D DE00 005A | true |
| pair | 200 | 0041 D83D DE00 005A | true |
| splitPair | -1 | System.ArgumentOutOfRangeException:length | true |
| splitPair | 0 | 2026 | true |
| splitPair | 1 | D83D 2026 | true |
| splitPair | 2 | D83D DE00 | true |
| splitPair | 3 | D83D DE00 | true |
| splitPair | 4 | D83D DE00 | true |
| splitPair | 5 | D83D DE00 | true |
| splitPair | 8 | D83D DE00 | true |
| splitPair | 40 | D83D DE00 | true |
| splitPair | 48 | D83D DE00 | true |
| splitPair | 60 | D83D DE00 | true |
| splitPair | 200 | D83D DE00 | true |
| loneHigh | -1 | System.ArgumentOutOfRangeException:length | true |
| loneHigh | 0 | 2026 | true |
| loneHigh | 1 | D83D | true |
| loneHigh | 2 | D83D | true |
| loneHigh | 3 | D83D | true |
| loneHigh | 4 | D83D | true |
| loneHigh | 5 | D83D | true |
| loneHigh | 8 | D83D | true |
| loneHigh | 40 | D83D | true |
| loneHigh | 48 | D83D | true |
| loneHigh | 60 | D83D | true |
| loneHigh | 200 | D83D | true |
| loneLow | -1 | System.ArgumentOutOfRangeException:length | true |
| loneLow | 0 | 2026 | true |
| loneLow | 1 | DE00 | true |
| loneLow | 2 | DE00 | true |
| loneLow | 3 | DE00 | true |
| loneLow | 4 | DE00 | true |
| loneLow | 5 | DE00 | true |
| loneLow | 8 | DE00 | true |
| loneLow | 40 | DE00 | true |
| loneLow | 48 | DE00 | true |
| loneLow | 60 | DE00 | true |
| loneLow | 200 | DE00 | true |
| nbsp | -1 | System.ArgumentOutOfRangeException:length | true |
| nbsp | 0 | 2026 | true |
| nbsp | 1 | 5B57 | true |
| nbsp | 2 | 5B57 | true |
| nbsp | 3 | 5B57 | true |
| nbsp | 4 | 5B57 | true |
| nbsp | 5 | 5B57 | true |
| nbsp | 8 | 5B57 | true |
| nbsp | 40 | 5B57 | true |
| nbsp | 48 | 5B57 | true |
| nbsp | 60 | 5B57 | true |
| nbsp | 200 | 5B57 | true |

对照程序（从源码提取后实际编译运行的完整代码，便于复验；没有新增或修改任何现有测试断言）：

```csharp
using System.Text.Json;
using AirlineCEO.Balance.Stress;
if(args[0]=="matrix"){File.WriteAllText(args[1],JsonSerializer.Serialize(StressTestRunner.Run(verbose:false),new JsonSerializerOptions{WriteIndented=true}));return;}
var cases=new (string name,string s)[]{("null",null),("empty",""),("space","   "),("tab","\t"),("cr","\r"),("lf","\n"),("mixed"," A\r\nB\nC\rD "),("exact","abcd"),("long",new string('x',200)),("outer","  abc  "),("internal","a   b"),("zh","中文测试"),("pair","A\ud83d\ude00Z"),("splitPair","\ud83d\ude00"),("loneHigh","\ud83d"),("loneLow","\ude00"),("nbsp","\u00a0字\u00a0")};
var maxima=new[]{-1,0,1,2,3,4,5,8,40,48,60,200};var rows=new List<object>();int failures=0;
string Eval(Func<string,int,string> f,string s,int n){try{string v=f(s,n);return string.Join(" ",v.Select(ch=>((int)ch).ToString("X4")));}catch(ArgumentException e){return e.GetType().FullName+":"+e.ParamName;}catch(Exception e){return e.GetType().FullName;}}
foreach(var test in cases)foreach(int n in maxima){var values=new[]{Eval(OldC,test.s,n),Eval(OldO,test.s,n),Eval(RegressionTriggerBridge.Trim,test.s,n),Eval(NewC,test.s,n),Eval(NewO,test.s,n)};bool ok=values.All(v=>v==values[0]);if(!ok)failures++;rows.Add(new{test.name,max=n,oldCeo=values[0],oldOpen=values[1],shared=values[2],newCeo=values[3],newOpen=values[4],ok});}
File.WriteAllText(args[1],JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Trim output comparisons={rows.Count}, failures={failures}, UTF16 ordinal comparison; methods extracted from source");Environment.ExitCode=failures==0?0:1;
static string OldC(string s,int max){
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";}
static string OldO(string s,int max){
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";}
static string NewC(string s,int max){
            return RegressionTriggerBridge.Trim(s, max);}
static string NewO(string s,int max){
            return RegressionTriggerBridge.Trim(s, max);}
static class RegressionTriggerBridge {internal static string Trim(string s,int max){
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";}}

```

本机可重跑：`node .collab-local/codehealth-shakedown/equiv.mjs`；`dotnet run --project .collab-local/codehealth-shakedown/harness/Harness.csproj -c Release -- equiv .collab-local/codehealth-shakedown/equivalence.json`。本地工具/日志不属于生产改动。

## 数值before/after

取StressTestRunner.Run(verbose:false)的60组合，比较相同难度/航司/情景的所有返回属性；前后标记完全一致，期末现金与净利最大差均0万（门槛0.01万）。

```json
{
  "rows": 60,
  "identityAndFlagsIdentical": true,
  "maxClosingCashDeltaWan": 0,
  "maxNetProfitDeltaWan": 0,
  "allPropertiesIdentical": true,
  "distribution": {
    "GREEN": 45,
    "BANKRUPT": 13,
    "YELLOW": 1,
    "RED": 1
  }
}
```

bridgecheck额外70/70（60现金桥+10不变量），0失败、0跳过。现金桥自身容差0.5万/最大残差0.03万不是本批前后差门槛；本批前后差为0，单独满足0.01万要求。不能用70/70代替60组合前后对照。

## 开发自测与隔离预检

- semcheck：errors=0、tests-asm: errors=0、enc: errors=0；保留已有新增中文提示，未改编码基线。
- slotcheck：SLOTCHECK OK。
- Trim：204/204，0差异。
- 数值：60组合所有属性一致；bridgecheck 70/70。
- 隔离快照：EditMode 59/59；PlayMode 默认 171 通过，另有2个Explicit未选中；显式补跑 2/2、0失败、0跳过；按fullname合并共 173 个PlayMode用例全部执行通过，无剩余未执行项。原始默认XML中的2个Explicit标记未改写。通过≠等价，仍以逐字节迁移、调用点保留和204组输出对照为等价性论证。
- 千行结构指标：25→25；本批实际diff净行数以后附git numstat为准，任何正负均不计指标进度。

## 交付与复验边界

目标为零、仅验证流程。stage-close顶层拒绝Codex deliver；不改工具绕过角色检查。按已授权开发流程task-add(codex)→deliver(codex, scoped developer authorization)→在隔离快照做开发预检，不创建自签review。开发授权仅记录本聊天人类批准范围，不是Cursor复验授权。

唯一DEL的ID、三类指纹与deciding profile见下方绑定数据。Cursor反向复验必须由用户提供与它们绑定的authorization文件，拿到前保持待复验；本轮不生成或签署Cursor review授权。未git add/commit/push，未关闭Unity，未执行clean-checkout-verify，未修改历史交付/检测/事件。

## 本批唯一DEL与实际预检记录

**已实现，待复验；目标为零、仅验证流程。**

```json
{
  "projectId": "Airline-CEO-Simulator",
  "taskId": "S46-CodeHealthShakedown",
  "deliveryId": "DEL-20261031130233-d734b9",
  "developerActor": "codex",
  "reviewerActor": "cursor",
  "decidingProfile": "unity-editmode",
  "subjectFingerprints": {
    "implementation": "493ee3d9baee5a027b853d8452cf14fe743e35302aaa5f3796a787cf90467adf",
    "verification": "baf2ac00d3999ca7f5c37949cf61cf32287a417cbca2b7d25b01a6b28cb33f2f",
    "contract": "50ac389a7c206ac48298d525ea4ffed886dbed49ddf957584e83ecd85ccef80b"
  }
}
```

- edit：Passed，total=59 / passed=59 / failed=0 / skipped=0。
  - XML：out\collab\DEL-20261031130233-d734b9\Unity Project\Logs\Tasks20261017_codehealth_shakedown_edit.xml
  - XML SHA256：a5fc6feb624a89207ba323764729160586e84f22a66ca342e335e4eb32d24bce
  - 日志：out\collab\DEL-20261031130233-d734b9\Unity Project\Logs\Tasks20261017_codehealth_shakedown_edit.log
  - 日志 SHA256：1348d4e42da0102957164ccb4775a5d68c0fb386c698f78a3fd4f7bdd95f909f
- play：Passed，total=173 / passed=171 / failed=0 / skipped=2。
  - XML：out\collab\DEL-20261031130233-d734b9\Unity Project\Logs\Tasks20261017_codehealth_shakedown_play.xml
  - XML SHA256：3f979c11bd19bdf205849054227928962f4e619c085b29a82cad17b1b79c84b6
  - 日志：out\collab\DEL-20261031130233-d734b9\Unity Project\Logs\Tasks20261017_codehealth_shakedown_play.log
  - 日志 SHA256：283e5ceddc49a81d9e6a31c51d2a2402ef6413e802fd4ed8708e513755b4c467
- explicit：Passed，total=2 / passed=2 / failed=0 / skipped=0。
  - XML：out\collab\DEL-20261031130233-d734b9\Unity Project\Logs\Tasks20261017_codehealth_shakedown_explicit.xml
  - XML SHA256：f1ffef3501cf37e92b6ba7744006a568dac3f7bef7b804af4cddf678fe65c26c
  - 日志：out\collab\DEL-20261031130233-d734b9\Unity Project\Logs\Tasks20261017_codehealth_shakedown_explicit.log
  - 日志 SHA256：1f52422add4ce08a623d55ad5488c90ce413a691f70ff2f8d91fa6461af7b99d

合并状态（不篡改原始XML）：

```json
{
  "edit": 59,
  "playDefault": 171,
  "defaultExplicitNotSelected": 2,
  "explicitCompleted": 2,
  "uniquePlayPassed": 173,
  "unexecutedAfterExplicitCompletion": 0,
  "failed": 0
}
```

实际源码numstat（相对本批前HEAD；不作为任何指标收益）：

```text
1	3	Unity Project/Assets/Scripts/Editor/CeoCommandRegression.cs
1	3	Unity Project/Assets/Scripts/Editor/OpenRouteStudyRegression.cs
8	0	Unity Project/Assets/Scripts/Editor/RegressionTriggerBridge.cs
0	6	Unity Project/Assets/Scripts/UI/Setup/SetupFlowController.cs
```

4个源码中，批准改动区域外的字节完全相同；原LF保留；snapshot manifest完整性在Unity退出后复核。PlayMode运行中临时InitTestScene及其meta为Test Runner生成，退出后自动移除；没有删除或修改项目meta来掩盖差异。全套测试断言不变。

开发者自测记录没有生成REV。未执行stage-review，未自签独立结论，未代替Cursor或用户写授权。交付记录保持immutable，预检结果在本报告和out交接单追加，未覆盖DEL历史。

## 用户授权后Cursor如何复验

上面的JSON只列绑定数据，不是授权。用户需提供 schemaVersion=1、grantedBy=user、actor=cursor、developerActor=codex 的真实授权文件，包含本DEL/task/project、三类subjectFingerprints、profiles至少覆盖unity-editmode、actions覆盖review-start/review-run/review-evidence/review-finish（如需cancel也应包含）和非空statement。

拿到文件后由Cursor执行（此处不执行）：

```powershell
node tools/collab/cli.mjs stage-review --delivery DEL-20261031130233-d734b9 --actor cursor --profile unity-editmode --authorization <用户提供的授权文件.json>
```

没有该文件就保持待复验，不绕过角色检查。独立复验和用户裁决完成前，不进入下一批，不宣称本批验收通过。

## 最终范围与测试缓存收尾

- Git源码范围复核为恰好4个获准文件，测试/工具/.meta/资源改动为0；新增的本机C#输出对照程序位于忽略的.collab-local，不进入Assets、DEL源码或Unity导入，未新增交付.cs文件。
- 本轮唯一DEL：DEL-20261031130233-d734b9；任务S46-CodeHealthShakedown；report1+task record1+source4=6个范围文件。自动生成的state/CURRENT/NEXT/events/DEL是协作流水记录，不归为额外业务改动；旧Phase0.out-sizes.json是本轮前已经存在的未跟踪附件，未改动、未暂存。
- 本DEL的EditMode/PlayMode预检结束后，调用原有pruneSnapshotArtifacts；先核实解析后的绝对目标仅位于out/collab/DEL-20261031130233-d734b9，再保护全部manifest文件、6份运行XML/日志及manifest本身。清理1966747350 bytes测试生成缓存，保留6份日志/XML；清理后verifySnapshotIntegrity再次成功。这是已有测试缓存收尾，不是Phase 3新功能，不计任何指标进度，不触及历史快照/资源。
- 未创建任何S46独立REV；任务仍queued，等待用户授权Cursor。无自动git add/commit/push。
