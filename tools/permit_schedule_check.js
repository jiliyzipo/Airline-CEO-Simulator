// 校验准入节点排期：每个中间季度上报哪些节点、是否覆盖 2/3/4 且不重复、季度单调不减。
// 与 RoutePermitLogic.OffsetOf / NodesAt 保持一致（改公式时请同步）。
const NodeAccept = 1, NodeReview = 2, NodeRights = 3, NodeNotice = 4, NodeGrant = 5, NodeSlot = 6;
const NodeName = { 1: "受理立案", 2: "材料与运力审查", 3: "航权额度核验", 4: "局方公示", 5: "批复与发证", 6: "时刻协调与开航" };

function offsetOf(node, span) {
  if (span < 1) span = 1;
  if (node <= NodeAccept) return 0;
  if (node === NodeGrant) return span;
  if (node > NodeGrant) return span + (node - NodeGrant);
  if (span <= 1) return span;
  if (node === NodeReview) return 1;
  if (node === NodeNotice) return span - 1;
  const mid = 1 + Math.floor((span - 2) / 2 + 0.5);
  return Math.min(Math.max(mid, 1), Math.max(1, span - 1));
}

function nodesAt(doneNode, span, elapsed) {
  const list = [];
  if (elapsed <= 0 || elapsed >= span) return list;
  for (let node = NodeReview; node <= NodeNotice; node++) {
    if (node <= doneNode) continue;
    if (offsetOf(node, span) === elapsed) list.push(node);
  }
  return list;
}

let bad = 0;
for (let span = 1; span <= 6; span++) {
  const seen = [];
  const rows = [];
  for (let elapsed = 1; elapsed < span; elapsed++) {
    const ns = nodesAt(1, span, elapsed);
    rows.push(`Q+${elapsed}: [${ns.map(n => NodeName[n]).join(" / ") || "-"}]`);
    for (const n of ns) see(n);
  }
  function see(n) { seen.push(n); }
  const sorted = seen.slice().sort((a, b) => a - b);
  const dup = new Set(seen).size !== seen.length;
  const covers = sorted.join(",") === "2,3,4";
  // 季单调：同一节点季随 span 增大不减（含发证后的第 6 节点）
  const offs = [1, 2, 3, 4, 5, 6].map(n => offsetOf(n, span));
  const mono = offs.every((v, i) => i === 0 || v >= offs[i - 1]);
  const slotAfterGrant = offsetOf(6, span) > offsetOf(5, span);
  const ok = (span === 1 ? seen.length === 0 : covers && !dup) && mono && slotAfterGrant;
  if (!ok) bad++;
  console.log(`span=${span} 中间季节点 ${rows.join(" | ")} 覆盖=[${sorted.join(",")}] 重复=${dup} 季单调=${mono} 时刻在发证后=${slotAfterGrant} ${ok ? "OK" : "!! 异常"}`);
  console.log(`        节点目标偏移 1..6 = [${offs.join(", ")}]（批复=span=${span}，时刻=span+1=${span + 1}）`);
}
console.log(bad === 0 ? "\n排期校验通过" : `\n${bad} 个跨度异常`);
process.exit(bad === 0 ? 0 : 1);
