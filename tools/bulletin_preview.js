// 预览：外部事件 → 公文式来文（OfficialBulletin + ExternalTimeMapper 的 JS 镜像，用于人工核对措辞）
const OFFSET_MIN = 8 * 60;                       // 本机时区（分钟），与 GameState.LocalUtcOffset 同口径
const IND = '\u3000\u3000';

const pad = n => String(n).padStart(2, '0');
const dt = s => new Date(s + 'Z');
const local = d => new Date(d.getTime() + OFFSET_MIN * 60000);
const dayLabel = d => { const l = local(d); return `${l.getUTCFullYear()}年${l.getUTCMonth() + 1}月${l.getUTCDate()}日`; };
const dateTimeLabel = d => { const l = local(d); return `${l.getUTCFullYear()}年${pad(l.getUTCMonth() + 1)}月${pad(l.getUTCDate())}日 ${pad(l.getUTCHours())}:${pad(l.getUTCMinutes())}`; };
const isLocalMidnight = d => { const l = local(d); return l.getUTCHours() === 0 && l.getUTCMinutes() === 0; };
const hoursUntilNextMidnight = recv => {
  const l = local(recv);
  const next = Date.UTC(l.getUTCFullYear(), l.getUTCMonth(), l.getUTCDate() + 1, 0, 0, 0);
  return (next - l.getTime()) / 3600000 - OFFSET_MIN / 60 + OFFSET_MIN / 60;
};

const RX = {
  ymd: /((?:20|19)\d{2})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日/g,
  md: /(?<!\d)(\d{1,2})\s*月\s*(\d{1,2})\s*日/g,
  days: /(?:预计)?\s*(\d+)\s*天\s*后/,
  hours: /(?:预计)?\s*(\d+)\s*小时\s*后/,
  imm: /即刻|立即|即日起|即刻生效|马上生效/,
  timeAfter: /^\s*(零时|时整|时起|点起|起|开始|生效|施行|实施|执行|当天|当日|上午|下午|凌晨|晚)/,
  timeBefore: /(自|于|从|定于|拟于|在)\s*$/,
  midnightAfter: /^\s*(零时|时整|时起|点起|起|开始|生效|施行|实施|执行)/,
};

const isTimeExpr = (text, m) => {
  const after = m.index + m[0].length;
  const tail = text.slice(after, after + 8);
  if (RX.timeAfter.test(tail)) return true;
  return RX.timeBefore.test(text.slice(Math.max(0, m.index - 3), m.index));
};
const isMidnightExpr = (text, m) => {
  const after = m.index + m[0].length;
  const tail = text.slice(after, after + 8);
  if (RX.midnightAfter.test(tail)) return true;
  return RX.timeBefore.test(text.slice(Math.max(0, m.index - 3), m.index));
};

// 返回 { off, raw }：raw = 驱动了偏移的那段原始日期（供去现实日期时定位）
function extractOffset(text, publishedAt, expired, recv) {
  if (expired) return { off: 0, raw: '' };
  const t = text || '';
  if (RX.imm.test(t)) return { off: 0, raw: '' };
  let m = RX.days.exec(t); if (m) return { off: Number(m[1]) * 24, raw: '' };
  m = RX.hours.exec(t); if (m) return { off: Number(m[1]), raw: '' };
  const anchor = publishedAt ? publishedAt : recv;
  const dayDelta = target => {
    const d = Math.round((target - Date.UTC(anchor.getUTCFullYear(), anchor.getUTCMonth(), anchor.getUTCDate())) / 86400000);
    return d < 0 ? 0 : d;
  };
  RX.ymd.lastIndex = 0;
  const y = RX.ymd.exec(t);
  if (y && isTimeExpr(t, y)) {
    const d = dayDelta(Date.UTC(Number(y[1]), Number(y[2]) - 1, Number(y[3])));
    return { off: hoursUntilNextMidnight(recv) + Math.max(0, d - 1) * 24, raw: y[0] };
  }
  RX.md.lastIndex = 0;
  const md = RX.md.exec(t);
  if (md && isTimeExpr(t, md)) {
    let yy = anchor.getUTCFullYear();
    if (Number(md[1]) < anchor.getUTCMonth() + 1) yy++;
    const d = dayDelta(Date.UTC(yy, Number(md[1]) - 1, Number(md[2])));
    return { off: hoursUntilNextMidnight(recv) + Math.max(0, d - 1) * 24, raw: md[0] };
  }
  return { off: hoursUntilNextMidnight(recv), raw: '' };
}

function adverbial(off, recv, eff) {
  if (off <= 0.01) return '即刻';
  if (isLocalMidnight(eff)) {
    const n = Math.round((local(eff).setUTCHours(0, 0, 0, 0) - local(recv).setUTCHours(0, 0, 0, 0)) / 86400000);
    if (n <= 0) return '今日零时';
    if (n === 1) return '明日零时';
    return `${n}天后零时`;
  }
  if (off >= 20 && Math.abs(off % 24) < 0.2) return `${Math.round(off / 24)}天后`;
  if (off < 24) return `${Math.max(1, Math.round(off))}小时后`;
  return `${Math.round(off / 24)}天后`;
}
const describe = (off, r, e) => { const a = adverbial(off, r, e); return a === '即刻' ? '即刻生效' : a + '起'; };

const norm = s => (s || '').replace(/[\s\u3000]/g, '').replace(/年/g, '-').replace(/月/g, '-').replace(/日/g, '');

// 游离的现实日期 → 相对日词（以原文自身的发布日期为锚，保持内部一致性）
function dayWord(rawStr, anchor, withYear) {
  let d;
  if (withYear) {
    const m = /(\d{2,4})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日/.exec(rawStr);
    if (!m) return '近日';
    let y = Number(m[1]); if (y < 100) y += 2000;
    d = Date.UTC(y, Number(m[2]) - 1, Number(m[3]));
  } else {
    const m = /(\d{1,2})\s*月\s*(\d{1,2})\s*日/.exec(rawStr);
    if (!m) return '近日';
    d = Date.UTC(anchor.getUTCFullYear(), Number(m[1]) - 1, Number(m[2]));
  }
  const base = Date.UTC(anchor.getUTCFullYear(), anchor.getUTCMonth(), anchor.getUTCDate());
  const delta = Math.round((d - base) / 86400000);
  if (delta === 0) return '今日';
  if (delta === 1) return '明日';
  if (delta === -1) return '昨日';
  if (delta === 2) return '后日';
  if (delta === -2) return '前日';
  if (delta > 2 && delta <= 10) return `${delta}天后`;
  if (delta < -2 && delta >= -30) return `${-delta}天前`;
  return delta > 0 ? '近日' : '此前';
}

function replaceDates(text, re, phrase, raw, anchor, withYear) {
  re.lastIndex = 0;
  let out = '', last = 0, m;
  while ((m = re.exec(text)) !== null) {
    out += text.slice(last, m.index);
    const isPhrase = raw && norm(m[0]) === norm(raw);
    out += (isPhrase && isMidnightExpr(text, m)) ? phrase : dayWord(m[0], anchor, withYear);
    last = m.index + m[0].length;
    if (m[0].length === 0) re.lastIndex++;
  }
  return out + text.slice(last);
}

function stripDates(text, phrase, anchor, raw) {
  if (!text) return '';
  let s = replaceDates(text, RX.ymd, phrase, raw || '', anchor, true);
  s = replaceDates(s, RX.md, phrase, raw || '', anchor, false);
  s = s.replace('自即刻零时起', '即刻起').replace('自即刻起', '即刻起').replace('零时零时', '零时');
  s = s.replace(/(?<=[\u4e00-\u9fff，。；：、])\s+(?=\d+(?:天|小时)后)/g, '');
  s = s.replace(/(?<=\d+(?:天|小时)后(?:零时)?)\s+(?=[\u4e00-\u9fff，。；：、])/g, '');
  return s.trim();
}

const GOV = /局|署|部|委员会|发改委|银行/;
const styleOf = src => (/顾问|研究所|研究院|协会|咨询|评级|标普|穆迪|惠誉|中心/.test(src) ? 'adv'
  : /社|通讯社|报|周刊|电视台|网|杂志/.test(src) ? 'news'
    : /公司|集团|制造商|波音|空客|商飞|工业/.test(src) ? 'corp' : 'gov');
const docType = src => (/气象|台/.test(src) ? '预警通报'
  : /银行|央行/.test(src) ? '公告'
    : GOV.test(src) ? '通知' : '通报');

function paragraphs(c) {
  return c.split('\n').map(p => p.trim()).filter(Boolean).map(p => IND + p).join('\n');
}
const right = (t, cols = 30) => {
  const w = [...t].reduce((a, ch) => a + (ch.codePointAt(0) < 0x2e80 ? 1 : 2), 0);
  let pad2 = Math.max(0, cols - w);
  return '\u3000'.repeat(Math.floor(pad2 / 2)) + (pad2 % 2 ? ' ' : '') + t;
};

function topicOf(source, title) {
  let t = (title || '').trim();
  if (!t) return '有关事项';
  for (let len = source.length; len >= 2; len--) {
    if (t.length <= len) continue;
    if (!t.startsWith(source.slice(0, len))) continue;
    t = t.slice(len).replace(/^[：:,，\s]+/, '');
    break;
  }
  return t || '有关事项';
}
const shortTopic = t => {
  const m = /^([^，,；;。]+)[，,；;。]/.exec(t);
  return (m ? m[1] : t).trim() || '有关事项';
};
const timeAdverbial = d => {  let s = (d || '').trim();
  if (s.endsWith('生效')) s = s.slice(0, -2);
  if (s.endsWith('起')) s = s.slice(0, -1);
  return s.trim() || '发布之时';
};

// 正文段落后留一空行；无正文（订阅源常只给标题）则不留空行
const body = c => (c ? paragraphs(c) + '\n' : '');

function compose(ev, gameDate, triggered, signed = true) {
  const style = styleOf(ev.source);
  const topic = topicOf(ev.source, ev.title);
  const when = timeAdverbial(ev.desc);
  const now = when === '即刻';
  // 转呈件（signed=false）不写机关落款块：机关名与文日在转呈抬头里点明一次，末尾只署新闻官
  const agencySign = signed ? [right(ev.source), right(gameDate)] : [];
  if (style === 'gov') {
    const doc = docType(ev.source);
    const closing = doc === '通知' ? '通知' : doc === '公告' ? '公告' : '通报';
    return [
      `关于${shortTopic(topic)}的${doc}`, '',
      body(ev.content),
      triggered ? `${IND}本${doc}已自${gameDate}起施行。`
        : now ? `${IND}本${doc}自发布之时起施行。`
          : `${IND}本${doc}自${when}起施行，届时将另行通报。`,
      '', `${IND}特此${closing}。`, '',
      ...agencySign,
    ].join('\n');
  }
  if (style === 'news') {
    return [
      `【${ev.source}】${topic}`, '',
      body(ev.content),
      triggered ? `${IND}（${ev.source} ${gameDate} 电）本条已生效。`
        : now ? `${IND}（${ev.source} ${gameDate} 电）即日起生效。`
          : `${IND}（${ev.source} ${gameDate} 电）据报将于${when}生效。`,
    ].join('\n');
  }
  if (style === 'corp') {
    return [
      `${ev.source}关于${topic}的公告`, '',
      body(ev.content),
      triggered ? `${IND}本公告自${gameDate}起生效。`
        : now ? `${IND}本公告自发布之日起生效。`
          : `${IND}本公告自${when}起生效。`,
      '', ...agencySign,
    ].join('\n');
  }
  return [
    `${ev.source}：${topic}`, '',
    body(ev.content),
    triggered ? `${IND}上述情况自${gameDate}起显现。`
      : now ? `${IND}上述情况即时显现。`
        : `${IND}上述情况预计于${when}显现。`,
    '', `${IND}以上情况，请予关注。`, '',
    ...agencySign,
  ].join('\n');
}

// —— 演示载荷 ——
const recv = dt('2030-05-10T15:00:00');   // 游戏接收瞬间（UTC）
const demos = [
  {
    id: 'ndrc_fuel', source: '发改委', category: '市场', title: '发改委调整成品油价格',
    content: '国家发展改革委决定，自2026年9月12日零时起，国内汽、柴油价格每吨分别上调 260 元和 250 元。',
    publishedAt: '2026-09-11T15:00:00',
  },
  {
    id: 'typhoon_swa', source: '中央气象台', category: '季节', title: '台风将影响粤东机场',
    content: '今年第14号台风预计3天后登陆粤东，汕头机场可能短时关闭。',
    publishedAt: '2026-09-11T10:00:00',
  },
  {
    id: 'reuters_opec', source: '路透社', category: '市场', title: 'OPEC+ 就减产配额产生分歧',
    content: '据路透社报道，OPEC+ 成员国就下阶段减产配额分歧加大，油价短期波动加剧。',
    publishedAt: '2026-09-11T08:00:00',
  },
  {
    id: 'boeing_ad', source: '波音公司', category: '市场', title: '波音发布新机型适航通告',
    content: '波音公司通告，737 MAX 系列新批次需在 2026年10月1日 前完成方向舵作动器检查。',
    publishedAt: '2026-09-10T09:00:00',
  },
  {
    id: 'pbc_fx', source: '中国人民银行', category: '市场', title: '人民币中间价下调，进口成本上升',
    content: '中国人民银行今日下调人民币兑美元中间价，进口航材与飞机备件成本相应抬升。',
    publishedAt: '2026-09-11T02:00:00',
  },
  {
    id: 'sp_rating', source: '标普全球', category: '市场', title: '评级机构下调信用展望',
    content: '标普全球将相关航司信用展望由「稳定」下调为「负面」，融资成本可能抬升。',
    publishedAt: '2026-09-11T02:00:00',
  },
  {
    id: 'hr_advisory', source: '人力资源顾问', category: '竞对', title: '行业人才争夺加剧，核心岗位流失风险',
    content: '人力资源顾问提示：行业人才争夺白热化，核心技术岗位面临挖角风险。',
    publishedAt: '2026-09-11T02:00:00',
  },
  {
    id: 'rss_weather', source: '天气网', category: '季节', title: '9月12日全国航空天气预报',
    content: '', publishedAt: '2026-09-12T00:00:00',
  },
  {
    id: 'rss_caac', source: '中国民航网', category: '监管', title: '9月11日新航季时刻协调会召开 - 中国民航网',
    content: '', publishedAt: '2026-09-11T06:00:00',
  },
  {
    id: 'caac_ad', source: '民航局', category: '监管', title: '民航局发布新机型适航指令，检修工时增加',
    content: '民航局发布适航指令，要求部分机型加装检查项目，机务排班工时相应上升，短期准点率承压。',
    publishedAt: '2026-09-11T02:00:00',
  },
];

// —— 转呈件外壳（C# PressDesk.ForwardHead / ForwardSign 的镜像）——
// 外部来文一律由新闻官转呈：抬头交代整理人，原文照登（原机关落款仍在原文里），末尾另署转呈人落款。
// 新闻官姓名按存档派生（C# PressDesk.Name），本预览只核对措辞，故用固定示例名。
const RIGHT_COLUMNS = 30;
const dispWidth = s => [...s].reduce((w, ch) => w + (ch.codePointAt(0) < 0x2E80 ? 1 : 2), 0);
const rightAligned = text => {
  const t = (text || '').trim();
  const pad = Math.max(0, RIGHT_COLUMNS - dispWidth(t));
  return '\u3000'.repeat(Math.floor(pad / 2)) + (pad % 2 ? ' ' : '') + t;
};
const OFFICER = '沈其昌';
const forward = (ev, label, triggered = false) => [
  `以下为${ev.source}来文原文（${label}），由新闻官${OFFICER}整理转呈。`,
  '',
  compose(ev, label, triggered, false).replace(/\s+$/, ''),
  '',
  rightAligned('新闻官 ' + OFFICER),
  rightAligned(label),
].join('\n');

let fail = 0;
const check = (name, ok, extra = '') => { if (!ok) fail++; console.log(`  ${ok ? '✓' : '✗'} ${name}${extra ? ' ' + extra : ''}`); };

for (const p of demos) {
  const publishedAt = dt(p.publishedAt);
  const ex = extractOffset(p.content + p.title, publishedAt, false, recv);
  const off = ex.off;
  const eff = new Date(recv.getTime() + off * 3600000);
  const adv = adverbial(off, recv, eff);
  const desc = describe(off, recv, eff);
  const ev = {
    ...p, desc,
    content: stripDates(p.content, adv, publishedAt, ex.raw),
    title: stripDates(p.title, adv, publishedAt, ex.raw),
  };
  const recvLabel = dayLabel(recv);
  console.log(`\n================ 【预告】${recvLabel} · ${ev.title}（${ev.source} / ${desc} / ${dateTimeLabel(eff)}） ================`);
  console.log(forward(ev, recvLabel));
  console.log(`\n---------------- 施行通报（生效时再发一封） ----------------`);
  console.log(forward(ev, dayLabel(eff), true));
  check('无公司信笺称谓', ev.content.indexOf('尊敬的') < 0);
  check('正文无现实日期', !/20\d{2}年/.test(ev.content + ev.title) && !/\d{1,2}月\d{1,2}日/.test(ev.content + ev.title), ev.title);
  check('无叠字', !ev.content.includes('零时零时'));
  if (p.id === 'rss_weather') {
    check('名称型日期→今日（不硬塞即刻）', ev.title === '今日全国航空天气预报', ev.title);
  }
  if (p.id === 'rss_caac') {
    check('名称型日期→今日（同日发布）', ev.title === '今日新航季时刻协调会召开 - 中国民航网', ev.title);
  }
}

console.log(`\n结果：${fail === 0 ? '全部通过' : fail + ' 项不符'}`);
