# 0006. AGPL-3.0 and a contributor license agreement

**Status:** accepted. The text of the CLA is a starting point that a lawyer must review before any commercial use.

## Context

The maintainer wants the project open source and wants to keep the possibility of a commercial edition later. Someone who runs a
modified copy as a service should have to share their changes.

## Decision

- The code is under the **AGPL-3.0**, with the copyright in the name of the maintainer (`AntoineYREM`), not "contributors".
- External contributions need a signed **CLA** ([CLA.md](../../CLA.md)) that lets the maintainer relicense. Contributors keep the
  ownership of their work. A bot asks for the signature in the first pull request.
- The files of `examples/` (analyses of public-domain books) are under **CC0**, so that anyone can reuse them.
- Dependencies must have licenses that can be combined with the AGPL; the continuous integration checks it
  ([THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md)).

## Consequences

- A contributor has one more step, which is the price of the freedom to relicense. It is explained in `CONTRIBUTING.md`.
- Every contribution must be the contributor's own work, or carry a license compatible with all of the above.
- Before any commercial use: have the CLA reviewed, and check the name "LivingPaper" (trademark, domain).
