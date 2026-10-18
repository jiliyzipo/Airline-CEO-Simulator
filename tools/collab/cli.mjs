#!/usr/bin/env node
import { loadConfig } from './lib/paths.mjs';
import { EXIT } from './lib/exit.mjs';
import { parseArgs } from './lib/commands.mjs';
import * as cmds from './lib/commands.mjs';
import * as stage from './lib/stage.mjs';
import fs from 'node:fs';

const HELP = `
Airline-CEO-Simulator collaboration CLI

Usage:
  node tools/collab/cli.mjs <command> [options] [--json]

Commands:
  doctor
  init
  status
  sync
  ack --actor cursor|codex --revision <n>
  claim --task <id> --actor cursor [--expected-revision <n>]
  deliver --task <id> --actor cursor --summary-file <file> [--model <name>] [--profiles smoke]
  review-start --delivery <id> --actor codex|cursor [--profile smoke] [--authorization <grant.json>]
  review-run --review <id> --actor codex|cursor [--authorization <grant.json>]
  review-finish --review <id> --actor codex|cursor --evidence <file> [--authorization <grant.json>]
  review-evidence --review <id> [--conclusion passed|failed] [--coverage AC1=passed,AC2=failed] [--note text]
  review-cancel --review <id> --actor codex|cursor|user [--authorization <grant.json>] [--reason <text>]
  review-recover --actor codex
  release --task <id> --actor cursor
  watch [--interval 3]
  tasks
  gate [--task <id>]                 (no --task = whole project completion gate)
  task-add --definition <file.json> [--actor cursor|user]
  taskbook-baseline [--task <id>]    (records the clause text so later edits are detected)
  taskbook-check --task <id> [--actor codex|user]
  issue-update --issue <id> --status <fixing|fixed|verified|closed|wontfix|open> --actor cursor|codex|user [--note <text>]
  hook --source cursor|codex --event <name> [--payload <file> | --payload-json <json>]
  hooks-status

Stage automation (one command per side):
  stage-open   [--actor cursor]                       sync + ack + status board
  stage-close  [--task <id>] [--profile <p>] [--roots <a,b>] [--note <text>] [--gaps <text>]
               [--no-preflight] [--dry-run] [--force]
                                                      auto task + summary + DEL + preflight + handoff
  stage-review [--delivery <id>] [--profile <p>] [--coverage AC1=passed,...] [--actor codex]
                                                      start + run + evidence + finish + ISSUEs
  stage-fix                                           actionable defect list for the developer
  stage-board                                         compact status panel

Exit codes: 0 ok | 2 usage | 3 conflict/CAS | 4 locked | 5 check failed | 6 evidence | 7 forbidden
`.trim();

/** Hooks receive their payload on stdin; never block an interactive terminal. */
function readStdinIfPiped() {
  try {
    if (process.stdin.isTTY) return '';
    return fs.readFileSync(0, 'utf8');
  } catch {
    return '';
  }
}

function main() {
  const argv = process.argv.slice(2);
  if (argv.length === 0 || argv.includes('--help') || argv.includes('-h') || argv[0] === 'help') {
    console.log(argv.includes('--json') ? JSON.stringify({ help: HELP }) : HELP);
    process.exit(EXIT.OK);
  }
  const command = argv[0];
  const args = parseArgs(argv.slice(1));
  const json = !!args.json;
  delete args.json;

  let cfg;
  try {
    cfg = loadConfig();
  } catch (e) {
    const code = e.exitCode || EXIT.USAGE;
    if (json) console.log(JSON.stringify({ ok: false, error: e.message, exitCode: code }));
    else console.error(String(e.message || e));
    process.exit(code);
  }

  const map = {
    doctor: () => cmds.cmdDoctor(cfg, args),
    init: () => cmds.cmdInit(cfg, args),
    status: () => cmds.cmdStatus(cfg, args),
    sync: () => cmds.cmdSync(cfg, args),
    ack: () => cmds.cmdAck(cfg, args),
    claim: () => cmds.cmdClaim(cfg, args),
    deliver: () => cmds.cmdDeliver(cfg, args),
    'review-start': () => cmds.cmdReviewStart(cfg, args),
    'review-run': () => cmds.cmdReviewRun(cfg, args),
    'review-finish': () => cmds.cmdReviewFinish(cfg, args),
    'review-evidence': () => cmds.cmdReviewEvidence(cfg, args),
    'review-cancel': () => cmds.cmdReviewCancel(cfg, args),
    'review-recover': () => cmds.cmdReviewRecover(cfg, args),
    release: () => cmds.cmdRelease(cfg, args),
    watch: () => cmds.cmdWatch(cfg, args),
    tasks: () => cmds.cmdTasks(cfg, args),
    gate: () => cmds.cmdGate(cfg, args),
    'task-add': () => cmds.cmdTaskAdd(cfg, args),
    'taskbook-baseline': () => cmds.cmdTaskBookBaseline(cfg, args),
    'taskbook-check': () => cmds.cmdTaskBookCheck(cfg, args),
    'issue-update': () => cmds.cmdIssueUpdate(cfg, args),
    hook: () => cmds.cmdHook(cfg, args, { stdinText: readStdinIfPiped() }),
    'hooks-status': () => cmds.cmdHooksStatus(cfg, args),
    'stage-open': () => stage.cmdStageOpen(cfg, args),
    'stage-close': () => stage.cmdStageClose(cfg, args),
    'stage-review': () => stage.cmdStageReview(cfg, args),
    'stage-fix': () => stage.cmdStageFix(cfg, args),
    'stage-board': () => stage.cmdStageBoard(cfg, args),
    'stage-report': () => stage.cmdStageReport(cfg, args),
  };

  if (!map[command]) {
    console.error(`Unknown command: ${command}`);
    console.error(HELP);
    process.exit(EXIT.USAGE);
  }

  try {
    const result = map[command]();
    if (command === 'watch') return; // keeps process alive
    if (json) console.log(JSON.stringify(result, null, 2));
    else if (typeof result === 'string') console.log(result);
    else if (result?.report) console.log(result.report);
    else console.log(JSON.stringify(result, null, 2));
    const code = result?.exitCode != null ? result.exitCode : EXIT.OK;
    process.exit(code);
  } catch (e) {
    const code = e.exitCode != null ? e.exitCode : EXIT.INTERNAL;
    const payload = {
      ok: false,
      error: e.message,
      exitCode: code,
      ...(e.extra || {}),
    };
    if (json) console.log(JSON.stringify(payload, null, 2));
    else {
      console.error(e.message);
      if (e.extra) console.error(JSON.stringify(e.extra));
    }
    process.exit(code);
  }
}

main();
