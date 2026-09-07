/**
 * Scored routing regression — DETERMINISTIC, no LLM judge.
 *
 * STRICT SEPARATION from observations.eval.ts: these suites score ONLY
 * observations promoted to data/fixtures.jsonl with an explicit routing
 * expectation. Raw unlabelled observations never enter this file, and each
 * suite is filtered to cases that actually carry its expectation kind, so no
 * case can pass vacuously and inflate the headline score.
 * observations.eval.ts carries no scorers and never contributes here.
 *
 * Scope: scores the ROUTING THAT WAS RECORDED for labelled historical
 * observations. The omp agent has no independently invocable skill router —
 * skill selection happens inside the model's context (system prompt +
 * SKILL.md reads), so there is no clean way to re-run "the router" offline
 * yet. Once such a hook exists, swap the `task`s below for a live routing
 * call and keep the scorers unchanged.
 *
 * Scoring per suite: 1 when every required skill was loaded / every forbidden
 * skill stayed unloaded, else 0. Extra skills are reported in the output, not
 * penalised. (Evalite beta16 has no "no score" state, hence two suites.)
 *
 * Run a single file: bunx evalite evals/routing.eval.ts
 */
import { evalite } from "evalite";
import { readFileSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import type { SkillObservation } from "../src/schema";

const here = dirname(fileURLToPath(import.meta.url));
const FIXTURES = join(here, "..", "data", "fixtures.jsonl");

type Labelled = SkillObservation & {
  expectedSkills: string[];
  forbiddenSkills?: string[];
};

const loadLabelled = (): Labelled[] => {
  if (!existsSync(FIXTURES)) return [];
  return readFileSync(FIXTURES, "utf8")
    .split("\n")
    .filter(Boolean)
    .map((l) => JSON.parse(l));
};

const labelledWithExpected = (): Labelled[] =>
  loadLabelled().filter((o) => o.expectedSkills?.length);

const labelledWithForbidden = (): Labelled[] =>
  loadLabelled().filter((o) => o.forbiddenSkills?.length);

evalite("Routing Regression — required skills (recorded)", {
  data: () =>
    labelledWithExpected().map((o) => ({
      input: o.id,
      expected: `required: ${o.expectedSkills.join(", ")}`,
    })),
  task: async (id) => {
    const o = labelledWithExpected().find((x) => x.id === id)!;
    return {
      prompt: o.prompt,
      loadedSkills: o.loadedSkills,
      requiredSkills: o.expectedSkills,
      extraSkills: o.loadedSkills.filter((s) => !o.expectedSkills.includes(s)),
    };
  },
  scorers: [
    {
      name: "required skills loaded",
      scorer: ({ output }) =>
        output.requiredSkills.every((s: string) => output.loadedSkills.includes(s))
          ? 1
          : 0,
    },
  ],
});

evalite("Routing Regression — forbidden skills (recorded)", {
  data: () =>
    labelledWithForbidden().map((o) => ({
      input: o.id,
      expected: `forbidden: ${o.forbiddenSkills!.join(", ")}`,
    })),
  task: async (id) => {
    const o = labelledWithForbidden().find((x) => x.id === id)!;
    return {
      prompt: o.prompt,
      loadedSkills: o.loadedSkills,
      forbiddenSkills: o.forbiddenSkills!,
    };
  },
  scorers: [
    {
      name: "forbidden skills not loaded",
      scorer: ({ output }) =>
        output.forbiddenSkills.every((s: string) => !output.loadedSkills.includes(s))
          ? 1
          : 0,
    },
  ],
});
