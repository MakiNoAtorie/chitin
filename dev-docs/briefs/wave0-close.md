# Wave 0 — closing phase: review and fix (same session)

This continues the session that implemented Wave 0. The implementation is committed as `d4d64f2..HEAD`.
Do not start a new thread of work: the remaining phase of this wave is the review below, then the fixes,
then the close.

Context you need:

- A separate review run was started by mistake and interrupted. It committed exactly one fix before it
  was stopped: `69e882e` (W0-1 — repaint after an async handler error so the status message is visible).
  Treat that commit as part of this wave, but verify it yourself: check that the repaint happens on the
  right thread, that it cannot throw when the visual is disposed, and that the message is actually
  visible for the intended five seconds.
- Everything after that commit is your own work and has not been reviewed.

## Do this, in this session

1. Read `dev-docs/briefs/review-wave0.md` and follow its Phase 1 (review) exactly as written: numbered
   findings with `file:line`, severity, what is wrong, minimal fix. Highest-risk area: `Prim[]` cache
   invalidation — walk every mutation of document state and prove each one bumps `Revision` or is
   invisible to the cache. Also verify the `FormattedText` cache cannot return a stale colour.
2. Fix every finding you raised, in this session, with the same rules as the implementation:
   Ponytail active, no new dependencies, no restructuring beyond the fix, PNG hashes unchanged unless a
   fix legitimately changes output (say which hash moved and why).
3. Rebuild, run `dev-docs/verify.sh`, and make it print `GATE_OK`. Add a `CHECK` for any bug you fixed
   that can be checked, especially a revision-coverage check that fails if a mutation leaves the cache
   stale.
4. Commit the fixes as `W0 review: <n> findings fixed — <what was verified>`.
5. Close the wave in your report: findings raised, fixed, refused (with reasons), the gate output, the
   commit hashes for the whole wave, and anything left open for Wave 1.
