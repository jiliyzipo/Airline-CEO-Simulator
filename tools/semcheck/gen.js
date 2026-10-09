// 生成 tools/semcheck/semcheck.csproj：把 Assembly-CSharp(.Editor).csproj 里的 Unity 引用
// 原样搬过来，Compile 项换成 Assets/Scripts 全量 glob。
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

const items = [...refs.entries()]
  .map(([name, hint]) => `    <Reference Include="${name}">\n      <HintPath>${hint}</HintPath>\n      <Private>false</Private>\n    </Reference>`)
  .join('\n');

// NoWarn 口径：只压「Unity 工程固有、改不动」的噪声——
//   CS0649/CS0169/CS0414 序列化字段未赋值、CS0067 未用事件、CS1591 缺 XML 注释、
//   CS0618/CS0672 过时 API、CS8632 可空注解、NU1701/MSB3277 包与引用告警。
// 故意**不压** CS0108（成员遮蔽基类成员）与 CS0114（应用 override 却写成 new）：
// 这两类全是「派生类自己写错」的信号，且遮蔽后代码能编译、只在运行期悄悄拿错值，
// 靠编译器提示是唯一低成本的拦截点。2026-09 两面板 const PageId 遮蔽 BasePanel.PageId
// 就是这么漏出去的（Unity 报了、semcheck 因 NoWarn 反而看不见）。
const out = `<!-- 自动生成，请勿手改；重新生成：node tools/semcheck/gen.js -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <OutputType>Library</OutputType>
    <AssemblyName>SemCheck</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <NoWarn>$(NoWarn);CS0649;CS0169;CS0414;CS0067;CS1591;CS0618;CS0672;CS0728;CS8632;NU1701;MSB3277</NoWarn>
    <DefineConstants>UNITY_EDITOR;UNITY_6000_3_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_STANDALONE_WIN;UNITY_STANDALONE;DEVELOPMENT_BUILD</DefineConstants>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>
    <DebugType>none</DebugType>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="../../Unity Project/Assets/Scripts/**/*.cs" />
  </ItemGroup>

  <ItemGroup>
${items}
  </ItemGroup>

</Project>
`;

const outPath = path.join(__dirname, 'semcheck.csproj');
fs.writeFileSync(outPath, out, 'utf8');
console.log(`refs=${refs.size} -> ${outPath}`);
