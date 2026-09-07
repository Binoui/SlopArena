/**
 * Observation browser — phase 1 corpus inspection.
 *
 * Loads every recorded real operation and surfaces prompt / explicit skill /
 * loaded skills / model / repo-commit / outcome / notes in the Evalite UI.
 * The task is a trivial deterministic echo: NO model call, NO fake score.
 * Run with: bun run eval:dev
 */
import { evalite } from "evalite";
import { readFileSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import type { SkillObservation } from "../src/schema";

const here = dirname(fileURLToPath(import.meta.url));
const OBS = join(here, "..", "data", "observations.jsonl");

evalite("Observation Browser", {
  data: () => {
    if (!existsSync(OBS)) return [];
    const observations: SkillObservation[] = readFileSync(OBS, "utf8")
      .split("\n")
      .filter(Boolean)
      .map((l) => JSON.parse(l));
    return observations.map((o) => ({ input: o.id, expected: undefined as string | undefined }));
  },
  task: async (id) => {
    const observations: SkillObservation[] = readFileSync(OBS, "utf8")
      .split("\n")
      .filter(Boolean)
      .map((l) => JSON.parse(l));
    const o = observations.find((x) => x.id === id)!;
    return {
      prompt: o.prompt,
      explicitSkill: o.explicitlyInvokedSkills ?? [],
      loadedSkills: o.loadedSkills,
      model: o.model,
      repo: o.repo,
      commit: o.commit,
      humanOutcome: o.outcome,
      notes: o.notes,
    };
  },
});
