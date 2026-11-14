# Bolt preview compatibility fixtures

These eight fixtures contain synthetic input parameters and 24 expected answers
(`chain`, `chainDetails`, `boltChains`) from the baseline used to validate commit
`4e926bb`. They contain no model paths, private drawing identifiers or local case files.
The test factory reconstructs the same geometry using the existing test helpers.

`BoltChainCompatibilityTests` compares the entire response, normalizing volatile
context IDs and excluding the newly introduced previewPlanId transport metadata. The cases cover front/back views, reversed direction, a tall main part,
an edgeless plate, frame refusal, a section and an end view. Shared/merged groups,
notches, incomplete reads, source precision and orphan prevention have focused
regressions in the bolt component/rule tests. Carry/overall restoration and cached
repeat behavior are additionally covered by `BoltLocationChainComposerTests`.

`full-length-fin-carry.json` is a separate reviewed contract for the existing
`FullLengthFinPlate` scenario, not a full response captured from `4e926bb`. It checks
carry segments, source references and restored overall through the real context,
then checks cached repeat reads and preview-to-batch points/key/offset consistency.
It does not pin an independently captured absolute offset. None of these fixtures
replays a production M.298 drawing.

Expected answers are checked-in evidence, not generated during a test. Update one
only after an explicitly reviewed behavior change, recording its reason in the
roadmap. A replay match does not establish correct live placement in Tekla.

The final-refactor tests and fixtures have been added in source; their compilation
and execution require the next explicit build/test command.
