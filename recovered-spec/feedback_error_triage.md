---
name: feedback-error-triage
description: How to present error fixes during RimWorld mod triage sessions
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 0809dcb9-327c-454d-8394-c7da9151144d
---

Before suggesting a patch for any error, always lead with:
1. **Severity** — what breaks if the error is left unfixed (crash, log spam, missed event, no gameplay impact, etc.)
2. **Likelihood of reoccurrence** — one-off vs. every session, what triggers it

Then offer the fix. This lets the user decide whether the fix is worth applying.

**Why:** User wants to make an informed call on whether to patch vs. wait and see. Some errors are benign one-offs that don't need patches.

**How to apply:** Every time an error is pasted for review, open with a 1-2 sentence severity + recurrence assessment before proposing any code change.
