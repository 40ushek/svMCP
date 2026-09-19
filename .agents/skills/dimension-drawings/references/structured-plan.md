# Experimental structured location plan

Read only when using `preview_structural_dimension_plan` /
`apply_structural_dimension_plan`. These were added to source on 2026-09-19;
unit tests and builds passed, but live Tekla validation was not performed then.
Do not infer deployment from source or make ordinary placement depend on a
live test of these commands. Use only on an explicitly requested test or after
live validation for the relevant runtime has been established.

## Supported scope

One steel `PartLocation` chain, horizontal/vertical, `Relative`, **Create only**.
Not timber, running rows, retain/replace, all-side completion or automatic
drafting judgement. Do not change the user's row type/policy to fit this scope.
Outside it, use the existing placement route and its explicit internal plan.
The accepted full-solid projection limitation on sections is unchanged; the
steel visual gate still applies.

## Read, preview, apply

1. From the current `get_structural_chain_positions` response, retain
   `sourceFingerprint`, `positionIndex` and each support's `supportIndex` with the
   coordinates. In the default compact answer the `supportIndex` values are the
   `refs[].supportIndex` of a support entry (one entry may list several, all naming
   the same point); name any one of them. Use `verbose=true` for the full form.
   Also read existing dimensions to avoid duplicating a matching chain.
2. Build one plan JSON with these fields:
   - `viewId`, `sourceFingerprint`, `side` (Top/Bottom/Left/Right);
   - `referenceModelId` (the confirmed main part), `subjectModelId`;
   - `ruleSet="steel"`, `purpose="PartLocation"`;
   - `internalPolicy` (None/Necessary/All), `recognizableDistance` for Necessary;
   - `datumReason`, `closureReason`, `rowType="Relative"`, `attributesFile`;
   - `distance` (non-negative), `reverseStart`, and the same
     `excludePrefixes` / `excludeMaterials` as the source read;
   - `positions`: exactly one decision per candidate on the selected side,
     each with `positionIndex`, `disposition` (Kept/Removed), `reason`;
     Kept also selects `supportIndex`, Removed omits it.
3. Preview once. Inspect resolved points, side and settings against the intended
   measurement. Both reference and subject supports must be represented; a bare
   plate size is not a location chain. Each support keeps its own transverse
   coordinate. Reference and closure can differ.
4. Apply the unchanged plan with the returned `approvalToken`. The bridge
   re-reads source and rejects stale fingerprints or changed plans. Re-preview
   only after an actual change/rejection; do not run repeated previews for an
   unchanged plan. Named attributes and their row type are checked at apply,
   not by the geometry-only preview.
5. Inspect returned ID/write state and read dimensions once for neighbour and
   plan verification. Other sides remain unreviewed.

The token checks consistency, not user authorization, duplicate suppression or
atomicity. Do not blindly resend apply after a timeout/error. Inspect the actual
view first: the chain might already exist. A stale-token error requires a fresh
source and reviewed plan, not bypassing the gate with raw create.
