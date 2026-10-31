/**
 * The clause templates every stage profile generates.
 *
 * These live in their own module because three places need them and one of them
 * (`task-add`) must not import `stage.mjs`: the stage module already imports the
 * command module, and a cycle there would make the template list load order
 * dependent.
 */
export const AC_TEMPLATES = {
  'unity-editmode': [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    'Unity EditMode 套件在隔离快照内真实执行，本 run 生成的 NUnit XML 全部用例通过',
    '测试计数与机器结果一致：total>0、failed=0、skipped<total',
  ],
  // PlayMode 是独立 profile（config.profiles['unity-playmode']），但一直没有
  // 自己的条款模板，于是 `stage-close --profile unity-playmode` 会退回到 smoke
  // 的「冒烟 profile 输出可解析的机器结果 JSON」——条款描述与真实证据（NUnit
  // XML）不符。补一份与 EditMode 等价的模板，避免每次 PlayMode 收口都留下
  // 一份字面失真的验收条件。
  'unity-playmode': [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    'Unity PlayMode 套件在隔离快照内真实执行，本 run 生成的 NUnit XML 全部用例通过',
    '测试计数与机器结果一致：total>0、failed=0、skipped<total',
  ],
  compile: [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    '语义编译检查通过：缺失类型/重复定义/编码门禁均为 0 失败',
    '检查报告来自本次 run，且计数与退出码一致',
  ],
  slot: [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    '槽位逻辑断言全部通过（[PASS] 与 SLOTCHECK 汇总一致）',
    '断言计数与机器结果一致，无跳过掩盖失败',
  ],
  collab: [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    '协作工具回归套件全部通过（隔离夹具，不启动 Unity）',
    '各子套件退出码为 0 且断言计数与机器结果一致',
  ],
  balance: [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    '现金桥对账在 60 组合上全部通过（期末 = 期初 + 净利 + IRROPS − 利息 + 未偿本金，残差 ≤ 容差）',
    '对照实验不变量成立：各变体成本明细项之和等于合计，且纯减机变体的营收与基准逐位相同',
    '断言计数与机器结果一致：total>0、failed=0、skipped<total',
  ],
  smoke: [
    '本阶段改动全部进入交付快照：导出后未列文件为 0，清单与源指纹一致',
    '冒烟 profile 输出可解析的机器结果 JSON 且断言计数合法',
    '文档与索引引用在本快照内可解析（无断链路径）',
  ],
};

export function acceptanceCriteriaFor(profile, title) {
  const texts = AC_TEMPLATES[profile] || AC_TEMPLATES.smoke;
  return texts.map((text, i) => ({ id: `AC${i + 1}`, text: `${text}（${title}）` }));
}

/**
 * S16/AC7: which clauses did the task book silently downgrade?
 *
 * A clause is machine-provable only when it is byte-identical to the template the
 * profile generates for that task (see `machineCoverageFor` in stage.mjs). Rewriting
 * a template clause is easy to do while "just making it clearer", and the result is
 * that a criterion the profile could have proved now has to be vouched for by a
 * human. That is a real loss of evidence strength, so it is said out loud when the
 * criteria are written instead of surfacing later as a failed review.
 *
 * Two modes, because the two callers know different things:
 * - with `profile` (stage-close picked one): a clause drifts when it differs from
 *   *that* profile's template, which is the exact clause that will be attested.
 * - without (task-add, profile not chosen yet): a clause drifts when it matches no
 *   profile template at its index. A clause copied verbatim from any template is
 *   still machine-provable under that profile, so warning about it would be noise.
 */
export function clauseDrift(cfg, task, profile = null) {
  const profiles = profile
    ? [profile]
    : Object.keys(AC_TEMPLATES).filter((p) => !cfg?.profiles || cfg.profiles[p]);
  const targets = profiles.length ? profiles : Object.keys(AC_TEMPLATES);
  const byProfile = [];
  for (const p of targets) {
    const expected = new Map(acceptanceCriteriaFor(p, task.taskId).map((ac) => [ac.id, ac.text]));
    const rewritten = (task.acceptanceCriteria || [])
      .filter((ac) => {
        const want = expected.get(String(ac.id));
        return want != null && want !== String(ac.text);
      })
      .map((ac) => String(ac.id));
    if (rewritten.length) byProfile.push({ profile: p, ids: rewritten });
  }
  if (profile) return byProfile;
  // Any mode: keep the ids that were template clauses (some profile has one at that
  // number) but now match no profile at all. A brand-new clause was never
  // machine-mapped, so it is not a downgrade and must not be reported as one.
  const criteria = task.acceptanceCriteria || [];
  const ids = criteria
    .map((ac) => String(ac.id))
    .filter((id) => {
      const wants = targets
        .map((p) => acceptanceCriteriaFor(p, task.taskId).find((ac) => ac.id === id))
        .filter(Boolean);
      if (!wants.length) return false;
      const got = String(criteria.find((ac) => String(ac.id) === id).text);
      return !wants.some((w) => w.text === got);
    });
  return ids.length ? [{ profile: '(any)', ids }] : [];
}

export function clauseDriftWarning(cfg, task, profile = null) {
  const drift = clauseDrift(cfg, task, profile);
  if (!drift.length) return null;
  const ids = [...new Set(drift.flatMap((d) => d.ids))];
  const byProfile = drift.map((d) => `${d.profile}(${d.ids.join(',')})`).join('、');
  return [
    `⚠ ${task.taskId}：${ids.join('、')} 与 profile 模板文本不一致，已由「机器可判定」降级为「需检测者人工补证」（${byProfile}）。`,
    '   如希望这些条款回到自动判定，请逐字沿用模板文本；否则交付时必须用 --coverage 显式补证。',
  ].join('\n');
}
