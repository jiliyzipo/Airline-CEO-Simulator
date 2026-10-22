// CEO 命令台词表探针：只碰 CeoCommandRouter 的纯函数，不启 Unity。
//
// 为什么不写在 Editor 回归里：Unity 里的 CeoCommandRegression 一次只能给一个结论，
// 而调词表时真正想知道的是「这 16 句分别派给了谁」。这里逐句打印，
// 改完词表 2 秒看全貌，再回 Unity 跑一次隔离回归钉死。
//
//   dotnet run --project tools/cmdcheck/cmdcheck.csproj
//
// 注意：GameState 是 MonoBehaviour，`gs == null` 走 UnityEngine.Object 的运算符，
// 在进程外永远为 true —— 所以这里**只验路由**，部门公函正文只能在 Unity 里验。
using System;
using AirlineCEO.Data;
using AirlineCEO.Logic;

static class Program
{
    static int fails;

    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
        if (!ok) fails++;
    }

    static void Main()
    {
        // ① 页面 → 默认部门
        Check("page 财务", CeoCommandRouter.PageDept("n15_财务部") == "FIN");
        Check("page 人力", CeoCommandRouter.PageDept("n18_人力资源部") == "HR");
        Check("page 机队", CeoCommandRouter.PageDept("n9_机队") == CeoCommandRouter.DeptFleet);
        Check("page 航线", CeoCommandRouter.PageDept("n12_航线网络") == "OPS");
        Check("page 愿景", CeoCommandRouter.PageDept("n16_战略发展部") == "STG");

        // ② 可行性：两端机场 + 大兴 ≠ 首都
        var feas = CeoCommandRouter.Route("n12_航线网络", "分析开通北京--纽约航线的可行性");
        Check("可行性 北京-纽约", feas.ok && feas.topic == CeoCommandRouter.TopicFeasibility
            && feas.deptCode == "STG" && feas.origin == "PEK" && feas.dest == "JFK",
            $"{feas.origin}-{feas.dest}");

        var feas2 = CeoCommandRouter.Route("n12_航线网络", "分析开通北京大兴-纽约的可行性");
        Check("可行性 大兴", feas2.ok && feas2.origin == "PKX" && feas2.dest == "JFK",
            $"{feas2.origin}-{feas2.dest}");

        var feas3 = CeoCommandRouter.Route("n12_航线网络", "分析开通上海到伦敦航线的可行性");
        Check("可行性 到字", feas3.ok && feas3.origin == "SHA" && feas3.dest == "LHR",
            $"{feas3.origin}-{feas3.dest}");

        var feasBad = CeoCommandRouter.Route("n12_航线网络", "分析开通某条航线的可行性");
        Check("缺机场拒答", !feasBad.ok, feasBad.note);

        // ②b 新入表机场（批次 A）必须能被命令台解析：否则「入表」只在数据层成立。
        foreach (var code in new[] { "ZUH", "WUX", "NGB", "WNZ" })
        {
            string zh = AirportCatalog.FindByCode(code).nameZh;
            var f = CeoCommandRouter.Route("n12_航线网络", $"分析开通北京—{zh}航线的可行性");
            Check($"新机场可行性 {code}", f.ok && f.topic == CeoCommandRouter.TopicFeasibility
                && f.origin == "PEK" && f.dest == code, $"{f.origin}-{f.dest}");
        }

        // ②c 机场搜索匹配（2026-10-21 截图报障）：英文只按**词首**匹配，不做任意子串。
        //     旧实现 "Chongqing Jia(ngb)ei" 里连着 "ngb"，于是打 NGB 会同时列出重庆江北。
        {
            var ngb = AirportCatalog.Filter("NGB");
            bool ngbFirst = ngb.Count > 0 && ngb[0].code == "NGB";
            bool noCkg = true;
            for (int i = 0; i < ngb.Count; i++) if (ngb[i].code == "CKG") noCkg = false;
            Check("搜索 NGB 不串 CKG", ngbFirst && noCkg,
                string.Join("/", ngb.ConvertAll(a => a.code)));

            var jb = AirportCatalog.Filter("Jiangbei");        // 词首命中仍要有效
            Check("搜索 英文词首 Jiangbei", jb.Count > 0 && jb[0].code == "CKG",
                jb.Count > 0 ? jb[0].code : "空");

            var zh = AirportCatalog.Filter("宁波");
            Check("搜索 中文 宁波", zh.Count > 0 && zh[0].code == "NGB", zh.Count > 0 ? zh[0].code : "空");

            var pre = AirportCatalog.Filter("ZU");
            Check("搜索 代码前缀 ZU", pre.Count > 0 && pre[0].code == "ZUH", pre.Count > 0 ? pre[0].code : "空");

            var tail = AirportCatalog.Filter("GB");                 // 尾码容错仍可用
            Check("搜索 代码包含 GB", tail.Count > 0 && tail[0].code == "NGB", tail.Count > 0 ? tail[0].code : "空");

            Check("搜索 空查询不铺满",
                AirportCatalog.Filter("").Count == 0 && AirportCatalog.Filter("   ").Count == 0);

            var none = AirportCatalog.Filter("QQQQ");
            Check("搜索 无匹配返回空表", none.Count == 0, none.Count.ToString());
        }

        // ③ 句子优先于页面
        var hr = CeoCommandRouter.Route("n12_航线网络", "现在乘务员缺口数量");
        Check("乘务员→人力", hr.ok && hr.deptCode == "HR", hr.deptCode);

        var fin = CeoCommandRouter.Route("n18_人力资源部", "汇报目前的财务状况");
        Check("财务优先于页面", fin.ok && fin.deptCode == "FIN", fin.deptCode);

        var fleet = CeoCommandRouter.Route("n15_财务部", "机队现在几架");
        Check("机队词优先", fleet.ok && fleet.deptCode == CeoCommandRouter.DeptFleet, fleet.deptCode);

        // ④ 关线：认成 Act（调用方回绝），不落进「运营部汇报航线」
        var close = CeoCommandRouter.Route("n12_航线网络", "取消北京大兴--上海虹桥的航线");
        Check("关线识别", close.ok && close.kind == CeoCommandRouter.CeoAskKind.Act
            && close.topic == CeoCommandRouter.TopicCloseRoute
            && close.origin == "PKX" && close.dest == "SHA",
            $"{close.origin}-{close.dest}");

        // 目的地不在机场目录里：仍要认成 Act，并把原话记下来（不能装作没听见）
        // ★ 2026-10-21：原夹具「珠海金湾」已随 ZUH 入目录变成正例，改用仍未收录的佛山沙堤。
        var close2 = CeoCommandRouter.Route("n12_航线网络", "取消北京大兴--佛山沙堤的航线");
        Check("目录外关线", close2.ok && close2.kind == CeoCommandRouter.CeoAskKind.Act
            && close2.origin == "PKX" && close2.dest == null
            && close2.originText == "北京大兴" && close2.destText == "佛山沙堤",
            $"text={close2.originText}-{close2.destText}");

        // ⑤ 看不懂的句子不派件（免得收件箱被「我不明白」刷屏）
        var gib = CeoCommandRouter.Route("n12_航线网络", "asdfgh");
        Check("乱码不派件", !gib.ok, gib.note);

        // ⑤b 运力调令（P2）：加减班 / 改班期 / 换机号 → Act + capacity，交给既有运力管线。
        //     判据复用管线自己的 HasDirectiveSignal，避免命令台与页面各有一套词表。
        var capInc = CeoCommandRouter.Route("n12_航线网络", "把北京--上海加密到每天3班");
        Check("加密→运力调令", capInc.ok && capInc.kind == CeoCommandRouter.CeoAskKind.Act
            && capInc.topic == CeoCommandRouter.TopicCapacity
            && capInc.origin == "PEK" && capInc.dest == "SHA",
            $"{capInc.topic} {capInc.origin}-{capInc.dest}");

        var capRed = CeoCommandRouter.Route("n12_航线网络", "北京到大连削减到每天1班");
        Check("减班→运力调令", capRed.ok && capRed.kind == CeoCommandRouter.CeoAskKind.Act
            && capRed.topic == CeoCommandRouter.TopicCapacity, $"{capRed.topic} {capRed.origin}-{capRed.dest}");

        var capSwap = CeoCommandRouter.Route("n12_航线网络", "给北京--广州换一架飞机执飞");
        Check("换机→运力调令", capSwap.ok && capSwap.kind == CeoCommandRouter.CeoAskKind.Act
            && capSwap.topic == CeoCommandRouter.TopicCapacity
            && capSwap.origin == "PEK" && capSwap.dest == "CAN",
            $"{capSwap.topic} {capSwap.origin}-{capSwap.dest}");

        var capDays = CeoCommandRouter.Route("n12_航线网络", "把北京--上海改成每周一三五执飞");
        Check("改班期→运力调令", capDays.ok && capDays.topic == CeoCommandRouter.TopicCapacity,
            $"{capDays.topic} {capDays.origin}-{capDays.dest}");

        // 但「每天」只是指代，不是命令：问收益的句子不能被当成调令
        var econAsk = CeoCommandRouter.Route("n12_航线网络", "汇报北京--上海每天的日利");
        Check("日利问句不算调令", econAsk.kind != CeoCommandRouter.CeoAskKind.Act, econAsk.topic);

        // ⑤c 「停飞」既是我们按钮上的词，也是 CEO 会用的词：必须走关线，不能落进部门汇报
        var stopFly = CeoCommandRouter.Route("n12_航线网络", "停飞北京首都--西安咸阳航线");
        Check("停飞→关线", stopFly.ok && stopFly.kind == CeoCommandRouter.CeoAskKind.Act
            && stopFly.topic == CeoCommandRouter.TopicCloseRoute,
            $"{stopFly.topic} {stopFly.origin}-{stopFly.dest}");

        // ⑤d 问句与调令必须分得开：「为什么…」是问，不是令；否则一句抱怨会把班次改掉
        var whyCap = CeoCommandRouter.Route("n12_航线网络", "为什么北京--上海只有1班");
        Check("问句不算调令", whyCap.kind != CeoCommandRouter.CeoAskKind.Act, whyCap.topic);

        var whyStop = CeoCommandRouter.Route("n12_航线网络", "为什么停飞北京--上海航线");
        Check("疑问不停飞", whyStop.topic != CeoCommandRouter.TopicCloseRoute, whyStop.topic);

        // ⑤d2 2026-10-22 报障：「取消北京—上海」只回了运营部一封本线答复，没有确认卡。
        //     旧判据要求句中出现「航线 / 航」，而这句最自然的说法根本不带；
        //     `OpsQueryLogic` 又把「取消」当成「停飞」话题，于是整句落进 route_query，
        //     回函写着「未改动本线运力」—— 看起来受理了，实际一条线没动。
        var closeBare = CeoCommandRouter.Route("n12_航线网络", "取消北京--上海");
        Check("裸关线（不带「航线」）", closeBare.ok && closeBare.kind == CeoCommandRouter.CeoAskKind.Act
            && closeBare.topic == CeoCommandRouter.TopicCloseRoute
            && closeBare.origin == "PEK" && closeBare.dest == "SHA",
            $"{closeBare.topic}/{closeBare.kind} {closeBare.origin}-{closeBare.dest}");

        // 反向三条：认错一边的代价不对等 —— 误判成关线是「整条线没了」，误判成问句只是「没动」。
        var closeFreq = CeoCommandRouter.Route("n12_航线网络", "取消北京--上海每天 2 班");
        Check("带班次不关线", closeFreq.topic != CeoCommandRouter.TopicCloseRoute
            && closeFreq.kind != CeoCommandRouter.CeoAskKind.Act, $"{closeFreq.topic}/{closeFreq.kind}");

        var closeAmend = CeoCommandRouter.Route("n12_航线网络", "取消北京--上海的补贴");
        Check("带补贴不裸关线", closeAmend.topic != CeoCommandRouter.TopicCloseRoute,
            $"{closeAmend.topic}/{closeAmend.kind}");

        var stopBareWhy = CeoCommandRouter.Route("n12_航线网络", "为什么北京--上海停飞了");
        Check("裸疑问不停飞", stopBareWhy.topic != CeoCommandRouter.TopicCloseRoute
            && stopBareWhy.kind != CeoCommandRouter.CeoAskKind.Act, $"{stopBareWhy.topic}/{stopBareWhy.kind}");

        // ⑤e 单航线询问（P2）：必须落到 route_query，不能落进「运营部总账」
        var qDays = CeoCommandRouter.Route("n12_航线网络", "为什么北京--上海周三不排班");
        Check("排班问句→本线答复", qDays.ok && qDays.topic == CeoCommandRouter.TopicRouteQuery
            && qDays.origin == "PEK" && qDays.dest == "SHA", $"{qDays.topic} {qDays.origin}-{qDays.dest}");

        var qWaves = CeoCommandRouter.Route("n12_航线网络", "北京--上海现在几班");
        Check("几班问句→本线答复", qWaves.ok && qWaves.topic == CeoCommandRouter.TopicRouteQuery
            && qWaves.origin == "PEK", $"{qWaves.topic} {qWaves.origin}");

        var qNoRoute = CeoCommandRouter.Route("n12_航线网络", "该城对 2 条线");
        Check("无航线无部门→不派件", !qNoRoute.ok, qNoRoute.note);

        var qGeneral = CeoCommandRouter.Route("n12_航线网络", "汇报一下公司整体情况");
        Check("公司整体→顾问", qGeneral.ok && qGeneral.topic == CeoCommandRouter.TopicCompany
            && qGeneral.deptCode == CeoCommandRouter.DeptAdvisor, $"{qGeneral.topic}/{qGeneral.deptCode}");

        // 「公司整体」不看当前页：站在人事页问，仍然由顾问答（不是人力部）
        var qCompanyHrPage = CeoCommandRouter.Route("n18_人力资源部", "汇报一下公司整体情况");
        Check("公司整体不看页面", qCompanyHrPage.deptCode == CeoCommandRouter.DeptAdvisor, qCompanyHrPage.deptCode);

        // 部门自己的「整体情况」仍归该部门，别被顾问抢走
        var qDeptOverall = CeoCommandRouter.Route("n12_航线网络", "财务部整体情况");
        Check("部门整体仍归部门", qDeptOverall.deptCode == "FIN" && qDeptOverall.topic == "", qDeptOverall.deptCode);

        // 弱档：只说「公司 + 怎么样/情况」，也算问公司整体
        var qCompanyHow = CeoCommandRouter.Route("n15_财务部", "公司现在怎么样");
        Check("公司怎么样→顾问", qCompanyHow.deptCode == CeoCommandRouter.DeptAdvisor, qCompanyHow.deptCode);

        // 顾问的活不能被航线问句抢走：点名了城对仍按本线问
        var qCompanyPair = CeoCommandRouter.Route("n12_航线网络", "全公司里北京—上海现在几班");
        Check("公司问句点名城对→按线问", qCompanyPair.topic == CeoCommandRouter.TopicRouteQuery,
            $"{qCompanyPair.topic}/{qCompanyPair.deptCode}");

        // ⑤f 「汇报一下」没有对象：不派件、不发信，只教写法（2026-10-21 实测原话）
        var qBare = CeoCommandRouter.Route("n12_航线网络", "汇报一下");
        Check("只有问法没对象→不派件", !qBare.ok && qBare.note.Contains("不完整"), qBare.note);

        // ⑥ 看不出部门时按当前页兜底
        var fallback = CeoCommandRouter.Route("n15_财务部", "随便说说");
        Check("按页面兜底", fallback.ok && fallback.deptCode == "FIN", fallback.deptCode);

        // ⑦ 国际/地区判定：国家底表优先，坐标包围盒（lon 72–137.8 / lat 0.83–55.83）
        //    会把新加坡、曼谷、东京、首尔全圈进「境内」，于是 PEK—SIN 成了国内航线。
        Check("国际 PEK-SIN", RoutePermitLogic.IsInternational("PEK", "SIN"));
        Check("国际 PEK-NRT", RoutePermitLogic.IsInternational("PEK", "NRT"));
        Check("国际 PEK-ICN", RoutePermitLogic.IsInternational("PEK", "ICN"));
        Check("地区 PEK-HKG", RoutePermitLogic.IsInternational("PEK", "HKG"));
        Check("国际 PEK-CDG", RoutePermitLogic.IsInternational("PEK", "CDG"));
        Check("国内 PEK-SHA", !RoutePermitLogic.IsInternational("PEK", "SHA"));
        Check("国内 PEK-URC", !RoutePermitLogic.IsInternational("PEK", "URC"));
        Check("国内 PVG-CAN", !RoutePermitLogic.IsInternational("PVG", "CAN"));

        // ⑦ 邮件正文装饰（TermNavMap）：机场名与三字码各只出现一次。
        //    旧版 WrapAirportCodes 会把三字码展成完整标签，正文变成「北京首都 北京首都 PEK」。
        string deco = TermNavMap.Decorate("线路　北京首都 PEK → 新加坡樟宜 SIN");
        string decoVisible = System.Text.RegularExpressions.Regex.Replace(deco, "<[^>]+>", "");
        Check("装饰不重复机场名", decoVisible.Contains("北京首都 PEK")
            && !decoVisible.Contains("北京首都 北京首都")
            && decoVisible.Contains("新加坡樟宜 SIN")
            && !decoVisible.Contains("新加坡樟宜 新加坡樟宜"), decoVisible);

        Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
