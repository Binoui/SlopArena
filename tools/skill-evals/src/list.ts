/**
 * List observations: id, outcome, explicit/loaded skills. Cheap triage view.
 *
 * Usage: bun run src/list.ts [grep-substring]
 */
import { readFileSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import type { SkillObservation } from "./schema";

const here = dirname(fileURLToPath(import.meta.url));
const OBS = join(here, "..", "data", "observations.jsonl");
if (!existsSync(OBS)) {
  console.error("no observations yet — run `bun run import` first");
  process.exit(1);
}
const filter = process.argv[2];

const observations: SkillObservation[] = readFileSync(OBS, "utf8")
  .split("\n")
  .filter(Boolean)
  .map((l) => JSON.parse(l));

for (const o of observations) {
  const hay = `${o.prompt} ${o.title ?? ""}`;
  if (filter && !hay.toLowerCase().includes(filter.toLowerCase())) continue;
  console.log(
    [
      o.id.slice(0, 8),
      o.timestamp.slice(0, 10),
      o.model ?? "?",
      (o.outcome ?? "-").padEnd(12),
      `x:${o.explicitlyInvokedSkills?.join(",") ?? "-"}`,
      `loaded:[${o.loadedSkills.join(",")}]`,
      (o.title ?? o.prompt.slice(0, 60)).replace(/\n/g, " ").slice(0, 70),
    ].join("  "),
  );
}
console.log(`\n${observations.length} observation(s) — annotate: bun run annotate <id> good|questionable|bad`);
