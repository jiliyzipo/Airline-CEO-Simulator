#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;
using UnityEngine;

namespace AirlineCEO.EditorTools
{
    /// <summary>
    /// 询价函**办结后**的正文口径回归（2026-11-04 用户手测发现）。
    ///
    /// 报障：信里写着「当前可签闲置机　B-2027、B-2043、B-2044」，
    /// 而同一时刻「机队 › 过渡业务」的在租表里 B-2027 正在执行 —— 两个页面互相打脸。
    ///
    /// 成因：`RefreshInquiryMailBodies` 只在「这封函还留着报价」时重建正文
    /// （`offers.Count > 0`）；全部签约后报价清零，正文再也不更新，
    /// 而「签约」按钮已经消失，玩家只能相信一段旧名单。
    ///
    /// 现在的口径：办结即把名单换成冻结说明，并把底部那句「请在底部选择…」改成办结提示；
    /// `Ensure` 每天都会跑到，所以整段必须幂等。
    /// </summary>
    public static class TransitionInquiryBodyFreezeRegression
    {
        const string Advice = "请在本函底部选择「签约闲置机」或「本次不接」。签约将自动分配当前闲置飞机。";
        const string FrozenMark = "本函已办结";
        const string IdleHead = "当前可签闲置机";

        public static bool Run()
        {
            var go = new GameObject("__transition_inquiry_freeze__");
            try
            {
                var gs = go.AddComponent<GameState>();
                gs.EnsureDefaults();
                gs.inbox = new List<InboxMail>();
                gs.pendingInbox = new List<PendingInboxMail>();
                gs.transitionContracts = new List<TransitionContract>();
                gs.transitionOffers = new List<TransitionOffer>();
                gs.transitionIntroSent = true;   // 跳过开局引导信，只看本函

                bool all = true;

                // canary：同一批判据喂给「还没冻结」的正文必须被拒绝，
                // 否则这三条断言只是把实现抄了一遍，绿了也证明不了抓得住回归。
                all &= Check("canary 失效：判定放过了「未冻结」的正文",
                    !LooksFrozen(BodyWithIdle("B-2027"), "B-2027"));

                // ① 已办结的函：Ensure 一遍就该改口径
                var closed = NewInquiry("closed", "approved", BodyWithIdle("B-2027", "B-2043"));
                gs.inbox.Add(closed);
                TransitionBusinessLogic.Ensure(gs);

                all &= Check("已办结的函仍把机号列在「当前可签闲置机」", LooksFrozen(closed.body, "B-2027"));
                all &= Check("办结后没有写上冻结说明",
                    closed.body.IndexOf(FrozenMark, StringComparison.Ordinal) >= 0);
                all &= Check("按钮已消失，正文却还在让人「在本函底部选择」",
                    closed.body.IndexOf(Advice, StringComparison.Ordinal) < 0);

                // ② 幂等：Ensure 每天都会跑到，冻结说明不能越堆越多
                TransitionBusinessLogic.Ensure(gs);
                int first = closed.body.IndexOf(IdleHead, StringComparison.Ordinal);
                int second = first < 0 ? -1 : closed.body.IndexOf(IdleHead, first + 1, StringComparison.Ordinal);
                all &= Check("冻结说明只应出现一次（Ensure 反复跑会把正文套娃）", first >= 0 && second < 0);

                // ③ 「本次不接」是当场关函，不等每日 Ensure
                var declined = NewInquiry("declined", "pending", BodyWithIdle("B-2031"));
                gs.inbox.Add(declined);
                TransitionBusinessLogic.DeclineInquiry(gs, declined, out _);
                all &= Check("「本次不接」后仍列着可签机号",
                    declined.body.IndexOf(IdleHead + "　B-2031", StringComparison.Ordinal) < 0);
                all &= Check("「本次不接」后仍写着「请在底部选择」",
                    declined.body.IndexOf(Advice, StringComparison.Ordinal) < 0);

                if (all)
                    Debug.Log("询价函办结口径：名单冻结 + 底部提示替换 + 幂等，3 组口径全部通过");
                return all;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static bool Check(string what, bool ok)
        {
            if (!ok) Debug.LogError("询价函办结口径-✗ " + what);
            return ok;
        }

        /// <summary>三条判据合成一个「这封函已经改过口径」的判断，canary 与正例共用同一口径。</summary>
        static bool LooksFrozen(string body, string reg)
        {
            if (string.IsNullOrEmpty(body)) return false;
            return body.IndexOf(IdleHead + "　" + reg, StringComparison.Ordinal) < 0
                   && body.IndexOf(FrozenMark, StringComparison.Ordinal) >= 0
                   && body.IndexOf(Advice, StringComparison.Ordinal) < 0;
        }

        /// <summary>复刻现场那封询价函：一段报价、一段在租、一行「当前可签闲置机」。</summary>
        static string BodyWithIdle(params string[] idleRegs)
        {
            return "运营部刚接到客户询价，现将意向整理如下。\n"
                   + "\n干租合同询价 1 份：\n"
                   + "- 客户 A（亚太远力租赁）期限 2026年10月06日—2027年01月03日 0.55 万/天\n"
                   + "\n在租一览：\n"
                   + "- B-2047 干租 · 亚太远力租赁 · 0.55 万/天\n"
                   + "\n" + IdleHead + "　" + string.Join("、", idleRegs) + "\n"
                   + "\n付款安排　签约先收定金 20%，执行期逐日入账 60%，交机结算尾款 20%。\n"
                   + "\n" + Advice;
        }

        static InboxMail NewInquiry(string id, string status, string body) => new InboxMail
        {
            id = "ti_freeze_" + id,
            sender = "运营部",
            role = "过渡经营请示",
            glyph = "运",
            subject = "关于开航前闲置运力过渡经营的请示",
            body = body,
            action = TransitionBusinessLogic.InquiryAction,
            kind = "alert",
            status = status,
            deadlineQuarters = 1
        };
    }
}
#endif
