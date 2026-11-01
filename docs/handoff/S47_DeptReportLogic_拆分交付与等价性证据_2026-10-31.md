# S47 DeptReportLogic partial 拆分 · 开发交付与等价性证据

**已实现，待复验。本批收益为单文件体积下降，行数与导入时间均为零收益。**

## 范围与基线

用户已确认S47方案及两段Departments补充，基线HEAD 1027ceb，开始时源码clean，ack revision 840。只处理DeptReportLogic：修改原主文件，新增Departments/Actions两个partial及各自meta，新增源码/meta共4个；1个DEL。没有拆RoutePermitLogic/OpsRotationLogic、CapacityDirectiveLogic或任何其他巨文件，没有新增或修改asmdef，不动GameState存档字段、测试断言、Debug.Log、产品文案与业务数值。

本批开发由Codex完成，自测不等于独立验收。沿用既有task-add(codex)+deliver(codex,开发授权)例外路径；stage-close目前的顶层角色门仍拒绝Codex deliver，不为本批修改该工具。开发授权记录本聊天用户批准，不是Cursor反向review授权。

## 文件、职责与行数

- Unity Project/Assets/Scripts/Logic/DeptReportLogic.Departments.cs：373 行。
- Unity Project/Assets/Scripts/Logic/DeptReportLogic.cs：547 行。
- Unity Project/Assets/Scripts/Logic/DeptReportLogic.Actions.cs：682 行。
- 主文件：Build两个重载、DeptAnswer、三个字段/常量、航线查询与公共文本辅助，26方法保留。
- Departments：公司与部门报表11方法。Actions：可行性分析、确认与执行管线25方法。都在AirlineCEO.Logic、public static partial class DeptReportLogic、原Runtime程序集内。
- 新增.meta 2份，主文件.meta原字节不变，无GUID替换。
- 新增本证据文档与S47任务记录；协作工具自动生成DEL/events/CURRENT等台账，不覆盖既有历史。

实测Assets下≥1000行.cs：25→24；主文件1573→547，新分片373/682均<1000。计数口径为split LF含终端空行，与当前指令一致；新分片未暂存也纳入实际目录计数。三文件合计1602行，原1573行，总文本行数增加29（外壳/using/空行等），没有减少代码量。单文件编辑读入体积下降，不能推断导入、编译或运行时加速。

## Departments必须按两段描述

**不是单一连续块。** 段1为原L105–L268（164行，含CompanyOverview XML注释）；段2为原L274–L465（192行）。两段之间的L270分节注释和L272 CrewPosts留在主文件。公司整体分节标题L103–L104作为单独的注释片段搬到Departments；它不混入两段证据。Actions按原L467–L1134整组搬移，包括它自己的分节注释。

- company-section-heading：原 L103–L104 → Unity Project/Assets/Scripts/Logic/DeptReportLogic.Departments.cs L12–L13，2行 / 189 bytes。
  - before SHA256：c53d176ebe10e7ad36c681a929aeda81bbce118ea29a173d692910ebd49f2dfa
  - after SHA256：c53d176ebe10e7ad36c681a929aeda81bbce118ea29a173d692910ebd49f2dfa
  - IDENTICAL: true
- Departments段1：原 L105–L268 → Unity Project/Assets/Scripts/Logic/DeptReportLogic.Departments.cs L14–L177，164行 / 10262 bytes。
  - before SHA256：d3bd4e05ea667e8e6ab9fa51f00b065ed52e5bac8f90f31cf5da44c1d05efa5e
  - after SHA256：d3bd4e05ea667e8e6ab9fa51f00b065ed52e5bac8f90f31cf5da44c1d05efa5e
  - IDENTICAL: true
- Departments段2：原 L274–L465 → Unity Project/Assets/Scripts/Logic/DeptReportLogic.Departments.cs L179–L370，192行 / 11769 bytes。
  - before SHA256：53c9c06f2642365391e4f9f73e6baab9416e1736928d7ac53b109459736c8411
  - after SHA256：53c9c06f2642365391e4f9f73e6baab9416e1736928d7ac53b109459736c8411
  - IDENTICAL: true
- Actions完整方法组及分节注释：原 L467–L1134 → Unity Project/Assets/Scripts/Logic/DeptReportLogic.Actions.cs L12–L679，668行 / 40555 bytes。
  - before SHA256：e7fad90faa0e823c03a16fdca7427f228e80614def34a7cfca47e5f171da0781
  - after SHA256：e7fad90faa0e823c03a16fdca7427f228e80614def34a7cfca47e5f171da0781
  - IDENTICAL: true

## 字段、Build及全文守恒

- CrewPosts和它的L270分节注释保留主文件，数组内容/顺序不变；三个字段/常量（CloseCancelNotice、CrewPosts、Ambiguous）的声明全文和相对顺序不变，新分片无字段初始化。
- Ambiguous原L1437保留主文件，仍在RouteNameResolver和EndpointHit之间。本批不处理路线查询夹心。
- DeptAnswer public struct整段全文一致，仍在主文件，所有字段名、类型、说明原样。
- Build原L69表达式体与L75块体全文/签名/字节完全相同，不拆方法内部。不新增转发层。topic顺序CloseRoute→Capacity→Rotation→OpenRoute→FleetBuy→HrRecruit→Finance→RouteQuery→Feasibility→Company；switch顺序FIN→HR→DeptFleet→MKT→MNT→STG→default Operations(gs)，包含早退与默认分支的位置不变。
- 原主文件只做上述原始字节片段移除，以及类声明增加partial。独立重建主文件expected并逐字节匹配；所有移除片段在目标文件逐字节匹配。原有注释/字符串/浮点运算/拼接顺序没有删除或重排，换行格式不变。
- 使用本机.NET SDK 8.0.423自带Roslyn解析，无新第三方依赖：原62方法，新三文件仍62方法，按完整签名一一对应，无遗漏、无重复。3个表达式体方法也计入：Build、ProfitText、Fail。
- 方法全文定义为签名、参数、完整块体或表达式体；前置注释由原始整段SHA覆盖。逐方法先做原始UTF8全文SHA与字符串严格比较，再用Roslyn token.Text序列忽略token间空白；字符串/插值文本内部的全角空格、标点、转义与UTF16内容不被忽略。两种对照均IDENTICAL。

## 36迁移方法完整清单

| 方法 | 原行 | 新文件 | 新行 | 全文SHA256（前后相同） | IDENTICAL | 去空白token归一化一致 |
|---|---:|---|---:|---|---|---|
| CompanyOverview | 115 | DeptReportLogic.Departments.cs | 24 | aec83c93546004277a395bc3054a591b54bc19b8b0ab162ca9efed9390317594 | true | true |
| CompanyVerdict | 195 | DeptReportLogic.Departments.cs | 104 | 879ef87ce99d6a4df79d41b2532369a6d77b3300d314e07d84b4297e90f98f7f | true | true |
| CompanyAdvice | 208 | DeptReportLogic.Departments.cs | 117 | 828db02548891c6bc50a03b1d1bd170a8e6c1f445c4277b180f0039f3586c24b | true | true |
| CrewGapTotal | 226 | DeptReportLogic.Departments.cs | 135 | 1b3a7199c32ad081089de332e8da3a6ba282083652bd25f793e988bec9e56029 | true | true |
| Finance | 240 | DeptReportLogic.Departments.cs | 149 | 5d0dae7634a8f7cec3795c81b26b0eec6202f2889f59d30082693551a62e9856 | true | true |
| HumanResources | 274 | DeptReportLogic.Departments.cs | 179 | 56799038f612bafbb5fd49dd1407b070040c11cca1e17d133fb0d619ce2848a6 | true | true |
| Fleet | 311 | DeptReportLogic.Departments.cs | 216 | 63e676e26029e66ec22ff0f0b0ca0bb04b0ecb380ac56d25a6000e00e2923364 | true | true |
| Operations | 355 | DeptReportLogic.Departments.cs | 260 | 80cfc755851babe2c1e0f545739ed2e0efa003df76e6cabd77fcb6bf1efa05f9 | true | true |
| Market | 405 | DeptReportLogic.Departments.cs | 310 | 0a07edcfb4a210a359a843506652789e9927845230b40edb047122fe442e4383 | true | true |
| Maintenance | 426 | DeptReportLogic.Departments.cs | 331 | 868d973888bd309dee0f1b028ece5ff449026ef0ab8c95b95b66db86bff5ca87 | true | true |
| Strategy | 448 | DeptReportLogic.Departments.cs | 353 | df7c474e0cc0dfd24226aaaee618dc2128367abccef49583496ca2a5867682db | true | true |
| Feasibility | 469 | DeptReportLogic.Actions.cs | 14 | 1538a00cb4095c00e21cf280c5bf7febc83fe6fdf6d78b39c613a31eaffde704 | true | true |
| CountAirworthy | 590 | DeptReportLogic.Actions.cs | 135 | 6ba6b2b31f4a9cdc0b726fe39942a29a4d77670551642d6bbc3650c4a26d13c6 | true | true |
| CloseRouteReview | 610 | DeptReportLogic.Actions.cs | 155 | 44342103c757d3c33382519ab6a43c962dad046ab17d2a463ce9e9e1e422f06e | true | true |
| CloseAmbiguous | 675 | DeptReportLogic.Actions.cs | 220 | e361d469525b653cad4be422923517aa1dda162355d791f9a4de68c83a11eb7c | true | true |
| CountRoutePair | 705 | DeptReportLogic.Actions.cs | 250 | d85032bcc7e8ac7a806a8f8c3d7f8d69683be8aa6fa132d037618fe14ca1a5b6 | true | true |
| SamePair | 714 | DeptReportLogic.Actions.cs | 259 | 3bf7071d19ce21073614f378496450343e5088be61b94a21091be150c6e6a87f | true | true |
| CloseConfirm | 724 | DeptReportLogic.Actions.cs | 269 | d5ae10ac26b11afc0ffd8150e9ba03998ce8a3fb825c331492cc3210c03262b5 | true | true |
| ExecuteClose | 757 | DeptReportLogic.Actions.cs | 302 | 88d9ea2ca4222423429321aa51866a32c04837575ae6eb2ecfb0e5b3c452118f | true | true |
| CloseDoneLetter | 799 | DeptReportLogic.Actions.cs | 344 | 0571c6e627b79df559d8a45c2ef0ea55bc19ea3c0171055cde9f0e7ac14eb0ef | true | true |
| CloseNetworkLetter | 835 | DeptReportLogic.Actions.cs | 380 | 9a11f33e1d44b6e16f45aa2a379b2edd55976eeb0052088de3945dfd6b821d4b | true | true |
| RotationDirective | 876 | DeptReportLogic.Actions.cs | 421 | 69ce594c6757f76a9d1336780e0dd4d854b0553fff8ca35a5f97f510bb57050d | true | true |
| CapacityDirective | 912 | DeptReportLogic.Actions.cs | 457 | 2c9dc5b1185f56f6908a874b86be6709755d621ad62f9e1dab9dca7a844b30a7 | true | true |
| RunCapacityDirective | 959 | DeptReportLogic.Actions.cs | 504 | 4b0e7071f1bb0d1d7588b3679dffb1d5c3346074061294e4cb1b62990eaaa54e | true | true |
| Directive | 1042 | DeptReportLogic.Actions.cs | 587 | b0a37ede946c5a1663073fb047d9c7d1da33a42c1e60095c11430319de3ab821 | true | true |
| OpenRouteDirective | 1052 | DeptReportLogic.Actions.cs | 597 | da62d8c7407904d84c8ea442905fa4218c7e44b95423aec5d2e35cae261927a5 | true | true |
| FleetBuyDirective | 1056 | DeptReportLogic.Actions.cs | 601 | f7437b6688696948e84e717b140d496b3240e5c761854d19eca33d0d4c25b813 | true | true |
| HrRecruitDirective | 1059 | DeptReportLogic.Actions.cs | 604 | eaf0a14eca3d46d788efede11baa8b29190b7e80fc960e4651ced0915fad61da | true | true |
| FinanceDirective | 1062 | DeptReportLogic.Actions.cs | 607 | 5d4a3ddcc1b043fd84eb9b00437bbadd1fb44bca00a7f0c1853538e7b009b1bc | true | true |
| FinanceKindName | 1065 | DeptReportLogic.Actions.cs | 610 | dcd126ee7aab2fe76a1a52162ac1f8093982912ac0c3d2ad92a2fdf75763fdc4 | true | true |
| ChannelName | 1068 | DeptReportLogic.Actions.cs | 613 | 1e000aeb1e2711536e641bd2b9b17cb510b11214fe4a6d8d2073835e5630e331 | true | true |
| RunPending | 1077 | DeptReportLogic.Actions.cs | 622 | 2132536e12d4536a293a047409fea71f07c3d1509649e5228a9843639c6a1096 | true | true |
| RunOpenRoute | 1097 | DeptReportLogic.Actions.cs | 642 | dfb426ce2f061a44ebdb2ac5efc2c54e021ece0c386b1d0745d05ccb2e05452e | true | true |
| RunFleetBuy | 1115 | DeptReportLogic.Actions.cs | 660 | 192ea4b51cbb7ee75936102f113371feb6a8f6de89fe3fef96dbc1fe8560b108 | true | true |
| RunHrRecruit | 1122 | DeptReportLogic.Actions.cs | 667 | 0918c3f18976bbd678894dbb60bbcef5a287d490091cca09e558a0529cf59364 | true | true |
| RunFinanceRequest | 1129 | DeptReportLogic.Actions.cs | 674 | 35f2f7abd7bbb36d0c3ce11eaf377478723ce1d5b286d6f589699caf002a794f | true | true |

## 26保留方法核对

- Build(GameState gs,CeoCommandRouter.CeoAsk ask)：原L69→主文件L69，SHA256 33afd5a0fe776f52cfd1d3528e3f13475939acf59f97772e0b7353a0fac129bf，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- Build(GameState gs,CeoCommandRouter.CeoAsk ask,string raw)：原L75→主文件L75，SHA256 4e29dedb81733f89289f62f9028276d02db67b79df11c9d8609ded28abf48f61，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- TryResolveRoute(GameState gs,CeoCommandRouter.CeoAsk ask,string raw,out RouteData route)：原L1136→主文件L110，SHA256 afc1728b5d8e6dd7accb7baf29954c7c03882bba4c48f190be7a3f18f1f5fc90，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- FindRouteByFlightNumber(GameState gs,string flightNumber)：原L1160→主文件L134，SHA256 d0b809401a2f0055bae17f7131e1fcebc97513b370b1dbcf765565369b25c60b，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- FlightNumberIn(GameState gs,string raw)：原L1192→主文件L166，SHA256 a54df8c0602d0fe25bd6aa3cfa0ab92d52f23150340933edb85d1bb55972f45a，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- IndexIgnoreCase(string haystack,string needle)：原L1219→主文件L193，SHA256 363cbf231ef9b8990e17496f0cf64b29b8f3d96df654e01a779c8218cfc4cbbb，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- RouteLabelOf(CeoCommandRouter.CeoAsk ask)：原L1224→主文件L198，SHA256 064f7bd8a38426a56a2c8ea53e17b43b9515c9ca81a033a490373034bcd6ff99，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- RouteQuery(GameState gs,CeoCommandRouter.CeoAsk ask,string raw)：原L1242→主文件L216，SHA256 cb7fcf7172ea2b5c6a32e538484055c9464db59e5f78aafe3239fa93080e7bcb，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- RunRouteQuery(GameState gs,string routeKey,string raw,out string receipt)：原L1272→主文件L246，SHA256 c2b3d591001b82b9d073a7bcc633e6b2df1f8681e0a826f20060dbaf7d11c424，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- DescribeMissingRoute(GameState gs,CeoCommandRouter.CeoAsk ask,bool asking)：原L1294→主文件L268，SHA256 315bac985ee8aa1e3898abddb148461622884304f8142c622dd189ab068604ed，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- ExistingPairList(GameState gs,int max)：原L1330→主文件L304，SHA256 7c4ba06744d273a4a74201f3e854f53039587c2b18b4e14f3a4ca1a385873a73，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- FlightNumbersOf(GameState gs,string a,string b)：原L1345→主文件L319，SHA256 2311d42c5be313a5d9c0bdcb55c20f10dccc408eac3853cc97b322e85235d0cf，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- RouteNumberLabel(RouteData r)：原L1366→主文件L340，SHA256 0be8392276ab9c4dc3a27ade8ff2328ca6b604c31fc6ad5d1bb2d1eab3b2b36e，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- FirstFlightNumberOf(GameState gs,string a,string b)：原L1378→主文件L352，SHA256 73bfd118357dc3d10416a19b652ddad223b43ca9e407d3fd9fa94a4744c86f46，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- ExistingFlightList(GameState gs,int max)：原L1397→主文件L371，SHA256 6e18501e5ec770432f2dc30290d2f35d028b375b1abc6d66182e5e256cbcff90，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- RouteNameResolver(GameState gs)：原L1418→主文件L392，SHA256 2e88741d8d5664052bd26409a9286c32eeefd57adde96609427b4ad07009de0a，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- EndpointHit(string code,string piece,string hit)：原L1439→主文件L413，SHA256 3d55309e33c420e0bf142390757d949407feef3f0a079795e3316092330efd5d，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- FindRoute(GameState gs,string a,string b)：原L1455→主文件L429，SHA256 c5dc8140bce7b62b3c1e6924521e711c8e1f7c223364d9fba2749401ad565583，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- FindRouteByName(GameState gs,string originText,string destText)：原L1472→主文件L446，SHA256 71293be11f854952c2872f118cf33838dc6117f214ce973134f73e7ba15741a4，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- NameMatches(string code,string text)：原L1487→主文件L461，SHA256 d87f0d2ec28818cf81807e10dd19099dad88d3756d8672165d6a0045a6541051，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- ProfitText(float dailyProfitWan)：原L1504→主文件L478，SHA256 caf9a1e15fc819f031f71b178a36eeabd476d8109603229a0266de06a489782a，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- Answer(string dept,string subject,string body,string oneLine)：原L1506→主文件L480，SHA256 a51b5a8d6ab1107b9952d7c36fd8c9747b26b497f288e47225a1f2f413369c31，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- Fail(string note)：原L1522→主文件L496，SHA256 c7138a5e4b4336502e71ddb28cfead78d38aeaa951738e42fcabb606b982af62，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- SenderOf(string dept)：原L1524→主文件L498，SHA256 1d831f0c18555b4e932ee24d3e50005af457063b9db93055f77d8a8ddcfcf4ed，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- RoleOf(string dept)：原L1540→主文件L514，SHA256 dfa5e5b1b0fb97b349c09c16e37360eee041b472ac2b0884b8f17fa02832ba26，IDENTICAL: true，NORMALIZED_IDENTICAL: true。
- GlyphOf(string dept)：原L1556→主文件L530，SHA256 b1bc56eb67487f3d5d7ec63d186fef34b860f00dc1ef7f6f1c65f1c929d5073f，IDENTICAL: true，NORMALIZED_IDENTICAL: true。

## meta配对

- Unity Project/Assets/Scripts/Logic/DeptReportLogic.Departments.cs ↔ Unity Project/Assets/Scripts/Logic/DeptReportLogic.Departments.cs.meta；GUID 824aa04ec1ab887b595953486906f4ab，本次crypto.randomBytes(16)产生；写入前git grep查已入库meta无命中，Assets全部现存meta（含未跟踪）无碰撞；写入后GUID在Assets恰好出现一次。
- Unity Project/Assets/Scripts/Logic/DeptReportLogic.Actions.cs ↔ Unity Project/Assets/Scripts/Logic/DeptReportLogic.Actions.cs.meta；GUID fc42ba5380f3759849c8a3ccc9275b63，本次crypto.randomBytes(16)产生；写入前git grep查已入库meta无命中，Assets全部现存meta（含未跟踪）无碰撞；写入后GUID在Assets恰好出现一次。
- 模板遵照现有QuarterlyOneOffLogic.State.cs.meta：fileFormatVersion: 2 + 32位小写十六进制guid；未让Unity事后补meta。新增cs=2，新增meta=2；主meta的原GUID 25478bbba76b3c14ba3cd8980bf0348b与原字节保持不变。

## 数值前后对照与开发自测

取改动前/后各StressTestRunner.Run(verbose:false)的60组合结果，逐项比较组合身份、分类、期末现金、净利及所有返回属性。现金与净利允许差≤0.01万，实测最大差均0；不是用bridgecheck自身0.5万容差代替本批前后0.01万要求。

```json
{
  "rows": 60,
  "allPropertiesIdentical": true,
  "maxCashDeltaWan": 0,
  "maxNetProfitDeltaWan": 0,
  "distribution": {
    "GREEN": 45,
    "BANKRUPT": 13,
    "YELLOW": 1,
    "RED": 1
  }
}
```

- Roslyn：62/62全文IDENTICAL，36迁移+26保留；Build×2、DeptAnswer、3字段不变。
- semcheck：errors=0；tests-asm: errors=0；enc: errors=0（不修改编码基线）。
- slotcheck：SLOTCHECK OK。
- bridgecheck：70/70（60组合现金桥+10不变量），0失败、0跳过；前后60组合全部属性一致。
- 同一DEL隔离快照：EditMode 59/59，0失败、0跳过；默认PlayMode 171通过，2个Explicit未选中；显式补跑 2/2，0失败、0跳过；按用例fullname合并173个PlayMode全部完成，无剩余未执行项。原始默认XML中的2个Explicit标记保留，不重写。所有结果均为开发自测，不是Cursor独立验收。

## 本机证据与复验方法

本机忽略目录.collab-local/s47-deptreport保存原DeptReportLogic.before.cs/meta、segments.json、method-proof.json、metrics.json、numeric-proof.json、前后60组合JSON、检查脚本和日志。这些是本机取证辅助，不属于新增交付源码。

```powershell
node .collab-local/s47-deptreport/verify-layout.mjs
dotnet run --project .collab-local/s47-deptreport/harness/Verifier.csproj -c Release -- syntax
node .collab-local/s47-deptreport/check-data.mjs
node tools/semcheck/run.js
dotnet run --project tools/slotcheck/slotcheck.csproj -c Release
dotnet run --project tools/balance/AirlineCEO.Balance.csproj -c Release -- --bridgecheck
```

## 交付与角色边界

只生成一个DEL；源码与任务快照身份绑定。待用户提供匹配该DEL/三类指纹/deciding profile的authorization后，由真实actor=cursor反向复验。Codex不自签review、不冒用Cursor/user、不自动git add/commit/push、不杀Unity、不删工程锁、不跑clean-checkout-verify、不执行完整LogicSelfTests.Run()。下一批S48不得在S47独立复验和用户裁决前开始。

## 唯一DEL绑定及原始Unity证据

**已实现，待复验。收益仅为单文件体积下降，行数与导入时间均为零收益。**

```json
{
  "projectId": "Airline-CEO-Simulator",
  "taskId": "S47-DeptReportPartial",
  "deliveryId": "DEL-20261031141433-af88d9",
  "developerActor": "codex",
  "reviewerActor": "cursor",
  "decidingProfile": "unity-editmode",
  "subjectFingerprints": {
    "implementation": "a11051d43c1af4edebae0bb5967b3f33e4e1ac7e2b9c20429a31e24430e6db06",
    "verification": "05137ecfe4fc8ce1d87f59b1a3c30217a9bf948e883366de2bd49eb399a32d0a",
    "contract": "383ea8d4fc5988443ef40d0e4e1727d6424ec04bc19a5e8acf4710900d977b0a"
  }
}
```

- edit：Passed，total=59 / passed=59 / failed=0 / skipped=0。
  - XML：out\collab\DEL-20261031141433-af88d9\Unity Project\Logs\Tasks20261017_s47_edit.xml
  - XML SHA256：91cda22589c2febbf0d80a519c83e510efa81c12ad5d679745af57c440817081
  - 日志：out\collab\DEL-20261031141433-af88d9\Unity Project\Logs\Tasks20261017_s47_edit.log
  - 日志 SHA256：5e3432cfb5decdbe0efd7d7bbe5f02bb6ea7c2445a9c963b6a3c2aea0eacd932
- play：Passed，total=173 / passed=171 / failed=0 / skipped=2。
  - XML：out\collab\DEL-20261031141433-af88d9\Unity Project\Logs\Tasks20261017_s47_play.xml
  - XML SHA256：c3ef47a2953141d0496d0396d8f71bbda4893cb913c1559e380b5bba1ab48876
  - 日志：out\collab\DEL-20261031141433-af88d9\Unity Project\Logs\Tasks20261017_s47_play.log
  - 日志 SHA256：7cb3a0e6697b4e66f65f5c7e0aa947c55f626cea97d8b8914896dea02bec56a6
- explicit：Passed，total=2 / passed=2 / failed=0 / skipped=0。
  - XML：out\collab\DEL-20261031141433-af88d9\Unity Project\Logs\Tasks20261017_s47_explicit.xml
  - XML SHA256：4a971cbfb63e4f76081f5714f9d7b79de5cb8d64e28b6b20d5d65d07d38ed782
  - 日志：out\collab\DEL-20261031141433-af88d9\Unity Project\Logs\Tasks20261017_s47_explicit.log
  - 日志 SHA256：404c2583f028c4b41a59066bde02b548fbcb07ad13cda173d3ac5bf81cf19e19

合并状态只核对测试身份，不改原始XML：

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

## 用户授权后的Cursor反向复验

上方绑定数据不是授权文件。用户需提供schemaVersion=1、grantedBy=user、actor=cursor、developerActor=codex、对应projectId/taskId/deliveryId/三类subjectFingerprints、profiles覆盖unity-editmode、actions覆盖review-start/review-run/review-evidence/review-finish和非空statement的授权文件。Codex不替用户签署该授权，不复用S46授权。

由真实Cursor在获得文件后执行（本轮不执行）：

```powershell
node tools/collab/cli.mjs stage-review --delivery DEL-20261031141433-af88d9 --actor cursor --profile unity-editmode --authorization <用户提供的授权文件.json>
```

独立复验与用户裁决前，不将24个工作区计数表述为已验收进度，不进入S48。

## 最终完整性与范围收尾

- Unity退出后verifySnapshotIntegrity通过，原始manifest输入无漂移、无未列源文件。Test Runner临时场景及meta由引擎自行移除，没有修改/删除项目meta掩盖问题。
- 调用本批之前已存在的pruneSnapshotArtifacts，只清理本DEL测试生成缓存；事前验证绝对路径恰为out/collab/DEL-20261031141433-af88d9，保留全部manifest输入与6份XML/日志。清理1966780829 bytes，清理后完整性再次通过。这不是本批新开发的prune功能，不计Phase 3收益，不动任何历史快照或docs资源。
- 最终源码范围恰好：1个原主文件修改+2个新partial+2份新meta。已有主meta、其他源码、测试、asmdef、经济参数均未修改；没有git add/commit/push。
- 本机另有其他协作方新增的程序集拆分可行性指令文档，未纳入本批、未据此启动其他任务。
- S47只有1个DEL，没有独立REV；仍为已实现，待复验。下一步由用户提供新DEL匹配的Cursor授权文件，本批不沿用S46授权或自签。
