/**
 * Importer: ~/.omp/agent/sessions/[project]/[session].jsonl  →  data/observations.jsonl
 *
 * Consumes the harness's own session logs; no live instrumentation.
 * Idempotent: sessions already present in observations.jsonl are skipped.
 * Only sessions with at least one skill read or explicit skill invocation are kept.
 *
 * Usage:
 *   bun run src/import.ts             # import sessions whose cwd mentions SlopArena
 *   bun run src/import.ts --all       # import every project's sessions
 *   bun run src/import.ts --force     # re-import even if session id already recorded
 */
import { readdirSync, readFileSync, existsSync, mkdirSync, writeFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { basename, join, dirname } from "node:path";
import { homedir } from "node:os";
import { fileURLToPath } from "node:url";
import type { SkillObservation } from "./schema";

const here = dirname(fileURLToPath(import.meta.url));
const OUT = join(here, "..", "data", "observations.jsonl");
const SESSIONS_ROOT = join(homedir(), ".omp", "agent", "sessions");
const PROMPT_CAP = 2000;

type AnyRec = Record<string, any>;

/** `skill://name/…` → canonical skill name `name`. */
function canonicalSkillName(path: string): string | null {
  if (!path.startsWith("skill://")) return null;
  return path.slice("skill://".length).split("/")[0] || null;
}

/** Known skill names across all install locations — used to detect explicit `/name` invocations. */
function knownSkillNames(): Set<string> {
  const names = new Set<string>();
  const roots = [
    join(homedir(), ".agents", "skills"),
    join(homedir(), ".omp", "agent", "skills"),
    "/home/binoui/Documents/projects/SlopArena/.omp/skills",
    "/home/binoui/Documents/projects/SlopArena/.agents/skills/unity-skills",
    "/home/binoui/Documents/projects/SlopArena/.claude/skills",
  ];
  for (const root of roots) {
    if (!existsSync(root)) continue;
    for (const entry of readdirSync(root, { withFileTypes: true })) {
      if (entry.name === "skills") continue; // meta-doc directory, not a routable skill
      if (entry.isDirectory() && existsSync(join(root, entry.name, "SKILL.md")))
        names.add(entry.name);
    }
  }
  return names;
}

/** Repo HEAD as of the session start, resolved from git history of the session cwd. */
function gitHeadAt(cwd: string, isoTimestamp: string): string | undefined {
  try {
    return (
      execFileSync(
        "git",
        ["-C", cwd, "log", "-1", "--format=%H", `--before=${isoTimestamp}`],
        { encoding: "utf8", timeout: 5000 },
      ).trim() || undefined
    );
  } catch {
    return undefined;
  }
}

function parseSessionFile(file: string, skills: Set<string>): SkillObservation | null {
  const lines = readFileSync(file, "utf8").split("\n").filter(Boolean);
  let session: AnyRec | null = null;
  let prompt: string | undefined;
  let explicit: string[] = [];
  const loaded = new Set<string>();
  const tools = new Set<string>();
  const modelCounts = new Map<string, number>();

  for (const line of lines) {
    let ev: AnyRec;
    try {
      ev = JSON.parse(line);
    } catch {
      continue;
    }
    if (ev.type === "session") {
      session = ev;
    } else if (ev.type === "custom" && ev.customType === "tool_execution_start") {
      const d = ev.data ?? {};
      if (d.toolName) tools.add(d.toolName);
      const skill = d.args?.path ? canonicalSkillName(d.args.path) : null;
      if (skill) loaded.add(skill);
    } else if (ev.type === "message" && ev.message?.role === "user") {
      const texts: string[] = (ev.message.content ?? [])
        .filter((c: AnyRec) => c.type === "text" && typeof c.text === "string")
        .map((c: AnyRec) => c.text);
      for (const text of texts) {
        if (prompt === undefined && !text.startsWith("[Image")) {
          prompt = text.slice(0, PROMPT_CAP);
        }
        for (const name of skills) {
          if (new RegExp(`(?<![\\w/-])/${name}\\b`).test(text) && !explicit.includes(name))
            explicit.push(name);
        }
      }
    } else if (ev.type === "message" && ev.message?.role === "assistant") {
      const m: string | undefined = ev.message.model;
      if (m) modelCounts.set(m, (modelCounts.get(m) ?? 0) + 1);
    }
  }
  if (!session || prompt === undefined) return null;
  const model = [...modelCounts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0];
  return {
    id: session.id,
    timestamp: session.timestamp,
    prompt,
    title: session.title,
    repo: session.cwd ? basename(session.cwd) : undefined,
    commit: session.cwd ? gitHeadAt(session.cwd, session.timestamp) : undefined,
    model,
    explicitlyInvokedSkills: explicit.length ? explicit : undefined,
    loadedSkills: [...loaded],
    toolsUsed: [...tools],
  };
}

const args = process.argv.slice(2);
const all = args.includes("--all");
const force = args.includes("--force");

mkdirSync(dirname(OUT), { recursive: true });
const existing: SkillObservation[] = existsSync(OUT)
  ? readFileSync(OUT, "utf8")
      .split("\n")
      .filter(Boolean)
      .map((l) => JSON.parse(l))
  : [];
const byId = new Map(existing.map((o) => [o.id, o]));

const skills = knownSkillNames();

for (const projectDir of readdirSync(SESSIONS_ROOT, { withFileTypes: true })) {
  if (!projectDir.isDirectory()) continue;
  if (!all && !projectDir.name.includes("SlopArena")) continue;
  const dir = join(SESSIONS_ROOT, projectDir.name);
  for (const f of readdirSync(dir)) {
    if (!f.endsWith(".jsonl")) continue;
    const obs = parseSessionFile(join(dir, f), skills);
    if (!obs) continue;
    const had = byId.has(obs.id);
    if (had && !force) continue;
    if (obs.loadedSkills.length === 0 && !obs.explicitlyInvokedSkills?.length) {
      if (force) byId.delete(obs.id); // stale entry no longer qualifies
      continue;
    }
    byId.set(obs.id, obs);
  }
}

writeFileSync(OUT, [...byId.values()].map((o) => JSON.stringify(o)).join("\n") + "\n");
console.log(`wrote ${byId.size} observation(s)`);
