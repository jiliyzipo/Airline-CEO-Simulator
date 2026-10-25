// 生成 tools/semcheck 下的两个 csproj，分别对应 Unity 侧的两条程序集边界：
//
//   semcheck.csproj        ← Assets/Scripts   ≈ AirlineCEO.Runtime.asmdef（运行时代码）
//   semcheck_tests.csproj  ← Assets/Tests     ≈ AirlineCEO.PlayModeTests.asmdef（测试代码）
//
// 为什么必须拆成两个工程：把两边塞进同一个工程时，引用集是两者的**并集**（含 Unity.TextMeshPro），
// 于是「测试里碰到 TMP 类型」这类程序集边界错误 semcheck 全绿、Unity 一编译就红：
//   CS0012 The type 'TextMeshProUGUI' is defined in an assembly that is not referenced.
//   You must add a reference to assembly 'Unity.TextMeshPro'.
// 2026-10-10 实例：SetupFlowPlayModeTests 读了主菜单句柄上的 TextMeshProUGUI 字段。
// 测试 asmdef 只引用 Runtime + TestRunner（拿不到 TMPro），故测试工程也照此把 TMPro 系列剔除，
// 才能真正复现并拦下这类错误 —— 只加 glob 不加边界，等于给了个「测试已被覆盖」的假象。
// 本工程同时生成 Unity 里 Assembly-CSharp(.Editor).csproj 的引用集。
const fs = require('fs');
const path = require('path');

const repo = path.resolve(__dirname, '..', '..');
const unityProj = path.join(repo, 'Unity Project');

const csprojs = ['Assembly-CSharp.csproj', 'Assembly-CSharp-Editor.csproj'];
const refs = new Map(); // name -> hintPath

for (const f of csprojs) {
  const p = path.join(unityProj, f);
  if (!fs.existsSync(p)) continue;
  const xml = fs.readFileSync(p, 'utf8');
  const re = /<Reference\s+Include="([^"]+)"[^>]*>\s*<HintPath>([^<]+)<\/HintPath>/g;
  let m;
  while ((m = re.exec(xml)) !== null) {
    const name = m[1];
    let hint = m[2];
    if (!path.isAbsolute(hint)) hint = path.join(unityProj, hint);
    if (!fs.existsSync(hint)) continue;          // 缺失的程序集直接跳过
    if (!refs.has(name)) refs.set(name, hint);
  }
}

// 测试程序集**看不到**的程序集（照 AirlineCEO.PlayModeTests.asmdef 的口径）。
// 只列 Unity.TextMeshPro / .Editor 两项：这是实测被 Unity 拒绝的那一支。
// 不在此处顺手扩大封锁名单 —— 名单开错会把 Unity 本来允许的写法判成错误（假阳性比漏报更坏）。
//
// 名单必须**读 asmdef**，不能手写死：2026-10-25 起测试 asmdef 加了 "Unity.TextMeshPro"
// （T038 的界面用例要读 TMP 文本），若这里仍按旧口径剔除，semcheck 会把 Unity 编译通过的
// 代码判成 CS0246 —— 正是本文件开头要防的那类假阳性，只是这次方向反过来。
const testAsmdef = path.join(unityProj, 'Assets', 'Tests', 'PlayMode', 'AirlineCEO.PlayModeTests.asmdef');
const testAsmRefs = new Set();
try {
  const asm = JSON.parse(fs.readFileSync(testAsmdef, 'utf8'));
  for (const r of asm.references || []) {
    const s = String(r);
    // GUID 形式（GUID:xxxx）在这里无法反查名字 —— 按「未引用」处理，宁可多报也不少报。
    if (/^GUID:/i.test(s)) continue;
    const at = s.lastIndexOf(':');
    testAsmRefs.add(at >= 0 ? s.slice(at + 1) : s);
  }
} catch (e) {
  // 读不到 asmdef（工程未就绪）时退回旧行为：全部剔除 TMPro，与 2026-10 之前的边界一致。
}
const notInTestAsm = name => /^Unity\.TextMeshPro($|\.)/.test(name) && !testAsmRefs.has(name);

const refItems = filter => [...refs.entries()]
  .filter(([name]) => !filter || !filter(name))
  .map(([name, hint]) => `    <Reference Include="${name}">\n      <HintPath>${hint}</HintPath>\n      <Private>false</Private>\n    </Reference>`)
  .join('\n');

// NoWarn 口径：只压「Unity 工程固有、改不动」的噪声——
//   CS0649/CS0169/CS0414 序列化字段未赋值、CS0067 未用事件、CS1591 缺 XML 注释、
//   CS0618/CS0672 过时 API、CS8632 可空注解、NU1701/MSB3277 包与引用告警。
// 故意**不压** CS0108（成员遮蔽基类成员）与 CS0114（应用 override 却写成 new）：
// 这两类全是「派生类自己写错」的信号，且遮蔽后代码能编译、只在运行期悄悄拿错值，
// 靠编译器提示是唯一低成本的拦截点。2026-09 两面板 const PageId 遮蔽 BasePanel.PageId
// 就是这么漏出去的（Unity 报了、semcheck 因 NoWarn 反而看不见）。
const commonProps = `    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <OutputType>Library</OutputType>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <NoWarn>$(NoWarn);CS0649;CS0169;CS0414;CS0067;CS1591;CS0618;CS0672;CS0728;CS8632;NU1701;MSB3277</NoWarn>
    <DefineConstants>UNITY_EDITOR;UNITY_6000_3_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_STANDALONE_WIN;UNITY_STANDALONE;DEVELOPMENT_BUILD</DefineConstants>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>
    <DebugType>none</DebugType>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>`;

// ① 运行时代码（AssemblyName 保持 SemCheck：tools/slotcheck 以 ProjectReference 引用本工程取运行期类型）
const runtimeProject = `<!-- 自动生成，请勿手改；重新生成：node tools/semcheck/gen.js -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
${commonProps}
    <AssemblyName>SemCheck</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="../../Unity Project/Assets/Scripts/**/*.cs" />
  </ItemGroup>

  <ItemGroup>
${refItems(null)}
  </ItemGroup>

</Project>
`;

// ② 测试代码：独立程序集 + 剔掉测试 asmdef 拿不到的程序集（见文件头说明）
const testsProject = `<!-- 自动生成，请勿手改；重新生成：node tools/semcheck/gen.js -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
${commonProps}
    <AssemblyName>SemCheckTests</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="../../Unity Project/Assets/Tests/**/*.cs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="semcheck.csproj" />
  </ItemGroup>

  <ItemGroup>
${refItems(notInTestAsm)}
  </ItemGroup>

</Project>
`;

fs.writeFileSync(path.join(__dirname, 'semcheck.csproj'), runtimeProject, 'utf8');
fs.writeFileSync(path.join(__dirname, 'semcheck_tests.csproj'), testsProject, 'utf8');
const blocked = [...refs.keys()].filter(notInTestAsm);
console.log(`refs=${refs.size} (tests block ${blocked.length}: ${blocked.join(', ')}) -> tools/semcheck/*.csproj`);
