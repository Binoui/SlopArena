---
name: brainstorming
description: Use when the user explicitly asks to brainstorm, plan, or think through a feature before building. Do NOT invoke automatically for every task.
---

# Grilling Session

Interview the user relentlessly about every aspect of the feature or decision until we reach a shared understanding. Walk down each branch of the decision tree, resolving dependencies between decisions one-by-one.

**Ask questions one at a time.** Waiting for feedback on each before continuing. Asking multiple questions at once is bewildering.

If a *fact* can be found by exploring the environment (filesystem, codebase, docs), look it up rather than asking. The *decisions* are the user's — put each one to them and wait for their answer.

When exploring, read the relevant `docs/systems/` file for the subsystem being discussed.

Do not write any code or make any changes until the user confirms shared understanding. End with a short summary of decisions made, then wait for "go ahead" / "vas y".
