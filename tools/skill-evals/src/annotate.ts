/**
 * Annotate / promote an observation. Edits data/observations.jsonl in place;
 * writing `expectedSkills` marks it as a labelled routing-regression candidate.
 *
 * Usage:
 *   bun run src/annotate.ts <id-or-prefix> good|questionable|bad \
 *     [--note "why"] [--expected skillA,skillB] [--forbidden skillC] [--promote]
 *
 * --promote additionally appends the annotated observation to the committed
 * labelled fixture set at data/fixtures.jsonl.
 */
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import type { SkillObservation } from "./schema";

const here = dirname(fileURLToPath(import.meta.url));
const OBS = join(here, "..", "data", "observations.jsonl");
const FIXTURES = join(here, "..", "data", "fixtures.jsonl");

const [idPrefix, outcome, ...rest] = process.argv.slice(2);
if (!idPrefix || !["good", "questionable", "bad"].includes(outcome)) {
  console.error(
    "usage: bun run src/annotate.ts <id-or-prefix> good|questionable|bad [--note text] [--expected a,b] [--forbidden c] [--promote]",
  );
  process.exit(1);
}

const flag = (name: string) => {
  const i = rest.indexOf(name);
  return i === -1 ? undefined : rest[i + 1];
};
const note = flag("--note");
const expected = flag("--expected")?.split(",").map((s) => s.trim()).filter(Boolean);
const forbidden = flag("--forbidden")?.split(",").map((s) => s.trim()).filter(Boolean);
const promote = rest.includes("--promote");

const observations: SkillObservation[] = readFileSync(OBS, "utf8")
  .split("\n")
  .filter(Boolean)
  .map((l) => JSON.parse(l));

const matches = observations.filter((o) => o.id.startsWith(idPrefix));
if (matches.length !== 1) {
  console.error(
    matches.length === 0 ? `no observation matches "${idPrefix}"` : `"${idPrefix}" is ambiguous (${matches.length} matches)`,
  );
  process.exit(1);
}
const obs = matches[0];
obs.outcome = outcome as SkillObservation["outcome"];
if (note !== undefined) obs.notes = note;
if (expected) obs.expectedSkills = expected;
if (forbidden) obs.forbiddenSkills = forbidden;

writeFileSync(OBS, observations.map((o) => JSON.stringify(o)).join("\n") + "\n");
console.log(`annotated ${obs.id} -> ${outcome}`);

if (promote) {
  const fixtures: SkillObservation[] = existsSync(FIXTURES)
    ? readFileSync(FIXTURES, "utf8").split("\n").filter(Boolean).map((l) => JSON.parse(l))
    : [];
  const without = fixtures.filter((f) => f.id !== obs.id);
  writeFileSync(
    FIXTURES,
    [...without, obs].map((o) => JSON.stringify(o)).join("\n") + "\n",
  );
  console.log(`promoted to data/fixtures.jsonl (committed labelled case)`);
}
