# Dimension Chain Composition and Architecture Roadmap

Updated 2026-10-02. Agreed design; minimal architecture boundary implemented below.
The full first delivery and later integrations remain pending. This is the canonical work
order for proposal contracts, composition, plan identity and extensible placement.
[Dimensions delivery](ROADMAP_DIMENSIONS.md#current-delivery-and-next-gate) owns
runtime/deployment status; [targeted refactoring](ROADMAP_DIMENSIONS.md#targeted-refactoring-before-chain-composition-2026-10-02)
owns independent correctness/performance work. Do not duplicate those statuses.

## Minimal architectural step (2026-10-02)

`DimensionRuleResult` now accepts optional typed proposal identity, measurement
purpose and reference metadata. Existing rule callers and legacy preview rows are
unchanged. `DimensionChainComposer` is a detached pure boundary with the explicit
`shared-datum-union-v2` diagnostic policy: retain every original proposal, combine
only explicitly compatible supported proposals, and record concrete causes and
affected model/point IDs. It does not assert duplicate coverage or readiness for writing.

`TimberPanelChainPreview` is the first adapter. Panel `chainDetails` returns an
additive `compositionPlan` over the entire view's evaluation (independent of the
legacy response's side filter), bound to an explicitly supplied context and positive
view ID. The plan is lazy: ordinary `chain` and its batch-reference resolution do
not compute it. Detailed plan construction/projection failures return
`compositionPlan: {error}` without changing the legacy preview. Rule evaluation and
legacy preview failures still propagate normally.
Side detection is non-throwing for diagnostic adaptation. Proposals with unsupported
placement/direction do not enter the four legacy rows but remain in the plan with
`previewKey: null` and an explicit `unsupported-legacy-preview` refusal. Supported
rows keep their existing values when such proposals are present.

Content-derived proposal IDs include purpose, direction, reference,
placement normal and ordered supported points with source evidence. They are local
to the snapshot: catalog point IDs and exact double coordinates participate in the
hash; IDs are not preserved across catalog rebuilds or coordinate changes and must
not be stored as persistent references. IDs use a rule-family prefix and 16 hex
digits of SHA-256; duplicate IDs, including shortened-hash collisions, are refused.
Unsupported placement contributes its type and publicly serialized state to identity
and remains explicitly blocked; this does not enable local/interior placement.
This unsupported-placement identity is provisional: it depends on the CLR type
name and reflection serialization, so renaming types or changing their serialized
state changes IDs; unserializable state produces a diagnostic plan error.
Result enumeration
and source evidence enumeration do not determine identity. `previewKey` links each
proposal to the existing preview independently of its ID; it is not a unique
proposal address. Unknown kinds map to `Unknown` purpose, and unsupported reference
semantics remain `Unknown`/`Unspecified` rather than inferred from panel geometry.
The current panel adapter does not resolve reference or reference-support semantics:
all its proposals carry these unknown values. Multiple proposals may share a
`previewKey` in the diagnostic plan. Legacy batch references still require one
chain per requested key; missing/ambiguous requested keys are rejected explicitly before preparation
or drawing writes, instead of selecting an arbitrary proposal.
An ambiguous unrequested key does not block resolution of a unique requested key.
The compact projection contains semantic metadata, preview links and decisions;
points, segments and evidence remain in the retained internal proposals and existing
preview, not duplicated in the plan response. Only genuine unions add a `chains`
block with source proposal IDs, ordered point IDs, datum ID and recomputed segments.
No row hint, stored plan state or batch references are added.
The existing rule's omitted mirror proposals remain visible as original refusals;
their pre-suppression candidates are not reconstructed.

Synthetic tests cover content identity under reordered proposals/sources, distinct
datum/source/semantic inputs, unknown vocabulary, explicit context validation,
order/source preservation, duplicate and cross-context rejection, missing evidence,
too few points and incomplete/refused inputs. Part causes include missing support,
unlocated members and unsupported inclined members. View-level causes are reported
separately: they do not prove that every proposal is incomplete. Panel and query
tests cover legacy point parity, concrete part diagnostics and compact projection.
Additional tests cover lazy diagnostic failure isolation, ambiguous legacy preview
keys, shortened-ID rejection and unsupported-placement type/state discrimination.
Mixed-evaluation adapter tests cover unsupported placement and skew direction
alongside unchanged legacy rows; reference tests distinguish requested ambiguity
from unrequested ambiguity. Diagnostic scope validation remains lazy: invalid
context/view arguments are reported only when the detailed plan is requested.

This is the requested minimal architecture stage, not completion of the first
delivery below. Per-candidate inclusion/exclusion
and requested/resolved-support diagnostics, compatibility/combination policies,
explicit complete settings and fixture/live acceptance remain pending. Runtime
deployment and live drawing changes are outside this step.

## Diagnostic part classification (2026-10-02)

`PartRoleClassifier.ClassifyLayer` is static and extends the existing classifier independently of
its structural inclusion/exclusion policy. The frozen `PartRoleInView` now retains
the original part name alongside prefix, profile and material. Classification reads
these captured values only; it does not read Tekla or change geometry selection.

The typed `ViewDimensionContext.Query` accepts optional `layerRules`. The MCP/bridge
JSON transport is implemented below; runtime activation is separate. No built-in prefix table or default
class is inferred. A `PartLayerRule` contains an explicit ID, class name, integer
priority, AND conditions on `Name`, `Prefix`, `Profile`, `Material`, and arbitrary JSON `Data`.
Each typed `PartLayerCondition` carries `Property`, `MatchKind` and `Value`.
`Equals` (the default), `Contains` and `StartsWith` all ignore case.
`Contains` is a literal substring search without word boundaries: `Name Contains "B"`
matches both `BEAM` and `BATTEN`. Use a sufficiently specific value or additional
AND conditions when that coverage is unintended. No arbitrary minimum length is imposed.
The original
property/value dictionary constructor remains shorthand for `Equals` conditions.
Multiple conditions may target the same property; all must match. Class names and rule IDs are exact
identifiers. Larger priority wins. Equal-priority matching rules assigning different
classes produce `Conflict`; agreeing rules retain all winning rule IDs. Missing
properties produce `Unknown` if they could affect the winning class; a known mismatch
disproves an AND rule even if another condition's value is absent. Reasons name the
unavailable properties and potentially deciding rules. Invalid/duplicate rule IDs
are refused in diagnostic projection. Rule conditions and JSON data are copied;
additional data is preserved without interpretation. No interface is introduced yet.

Example for direct typed callers:

```csharp
var rule = new PartLayerRule("batten-by-name", "batten", new[] {
    new PartLayerCondition(PartLayerProperty.Name, "BATTEN", PartLayerMatchKind.StartsWith)
});
var answer = context.Query("chainDetails", ruleSet: "panel", layerRules: new[] { rule });
```

Only panel `chainDetails` with a non-empty `layerRules` list adds this diagnostic projection.
Absent or empty rules skip classification and omit all three additional blocks,
preserving the compact plan. The plan returns one
`partClassification` table with policy `part-layer-diagnostics-v2`, explicit rules,
raw part attributes, class/status/reason, matched/winning/unresolved rule IDs and the
existing structural inclusion reason. `proposalPointIds` links each proposal's ordered
point IDs to `pointPartBindings`; bindings reference all source-part model IDs,
report absent/unidentified part owners and distinguish non-part source kinds.
Coordinates, segments and full source evidence are not repeated. These IDs remain
snapshot-local. Classification failures use the existing `compositionPlan.error`
isolation; compact `chain`, steel diagnostics, legacy preview and composition
decisions retain their behavior. Classification is recalculated from explicit Query
settings; it is not stored as mutable context state or incorporated in proposal IDs.

Synthetic tests cover comparison kinds, suffixes, captured material, conditions, explicit precedence, conflicts, unavailable values,
immutable JSON data, source ownership and deterministic projection. Integration tests
verify exact legacy preview/decision parity, omitted tables without rules and diagnostic failure isolation.
This completes diagnostic classification only: candidate selection by class,
per-candidate inclusion/exclusion explanations, support resolution, compatibility and
combination remain pending. No live drawing acceptance or deployment is claimed.

## Classification rules through MCP/bridge (2026-10-02)

`get_view_dimension_context` now accepts optional `layerRules`, a string containing
a JSON array of rules. It is appended to the existing bridge arguments; older callers
without that argument still work. Empty/whitespace or `[]` produces no classification
tables. Classification is returned only for `ruleSet="panel"` with `chainDetails`;
other questions retain their output even when valid rules are supplied.

Example: request `viewId=<current view>`, `questions="chainDetails"`, `ruleSet="panel"`
and pass this array serialized as the `layerRules` string:

```json
[
  { "id": "timber-batten", "className": "batten", "priority": 10,
    "conditions": [
      { "property": "Name", "matchKind": "StartsWith", "value": "BATTEN" },
      { "property": "Material", "value": "TIMBER" }
    ], "data": { "label": "Timber battens" } },
  { "id": "c24-batten", "className": "batten", "priority": 10,
    "conditions": [
      { "property": "Name", "matchKind": "StartsWith", "value": "BATTEN" },
      { "property": "Material", "value": "C24" }
    ] }
]
```

Both rules deliberately assign the same class, without assuming a plant-specific
meaning for B/T prefixes. Different class names can be configured explicitly.
Property names in JSON are the shown camelCase names. Property/match vocabulary
values ignore case; omitted `matchKind` is `Equals`, omitted priority is zero.
`data` is an arbitrary JSON object copied without interpretation.

One shared wire validator runs in MCP before contacting the bridge and in the bridge
before fetching the view context. Invalid JSON/schema, missing fields, unsupported
property/match values, duplicate IDs/fields and invalid priority/data are rejected
explicitly. This is request validation; failures do not read geometry and are not
reported as `compositionPlan.error`. Typed classification/projection failures retain
the existing isolated plan error behavior. Rules are not stored in the geometry cache,
used for structural inclusion, or passed to the writer.

Synthetic transport tests verify exact argument/JSON preservation, defaults and older
arguments, domain vocabulary coverage, malformed requests at both boundaries and
parsed-rule-to-Query preview parity. Live use requires updated MCP and bridge binaries
and refreshing the MCP tool schema. Runtime status is recorded in
[Dimensions delivery](ROADMAP_DIMENSIONS.md#current-delivery-and-next-gate).

## Read-only classification review (2026-10-02)

Reviewed EW.1 - 6, Front, scale 1:25 through a fresh MCP process with the new
`layerRules` schema. No structural exclusions were supplied. Name-based BATTEN rules
with explicit TIMBER/C24 refinements classified four visible TIMBER battens; ten
BEAM parts stayed `Unknown` because no rule matched them. Deliberately contradictory
name/prefix rules at equal priority produced four `Conflict` results. In all runs,
legacy preview, composition decisions and chain diagnostics were identical; the
repeated normal request reproduced the same response and context identity. All 15
point bindings resolved to the plan's part table. Existing missing-support, unlocated
and inclined-part issues remained reported; classification did not repair them.

The two C24 battens are present in the drawing/Top attributes but not in the Front
snapshot. Top panel preview is explicitly refused (only Front/Back supported).
Their classification is therefore covered by replay of captured attributes, not
claimed as a live Top composition-plan result. The small versioned
[classification fixture](../../../TeklaMcpServer.Tests/TestAssets/PartLayers/timber-batten-mixed-prefixes.v1.json)
retains four representative part records and five owner links with local IDs;
its [capture notes](../../../TeklaMcpServer.Tests/TestAssets/PartLayers/README.md)
separate observed Front results from pure evaluation of Top attributes. It does not
contain geometry or prove full planner/support correctness.
Each record declares `expectedSource: observed|evaluated`. Observed Front results
are regression expectations; Top attributes are exercised by a separate pure-rule
test and are not counted as observed composition-plan acceptance.

Five existing dimension IDs were retained in successive reads. One Bottom overall
line's position differed between the first and second read (-422.5 to -575.885 view
units); its measured span/length stayed 1168, and a third read matched the second.
No dimension write/arrange commands were called. The cause is unestablished;
drawing-position invariance is not proven by this run. Full first-delivery
classification-driven selection, compatibility/composition and live placement gates
remain open.

The read-only follow-up audit placed the differing reads at 21:40:39 and 21:41:44
local time (UTC+2); logged layout commands at 19:49–19:50 were earlier. The arrange
command failed and three fit commands succeeded. All five current dimension sets
and loaded standard/overall attributes report `Fixed` placement. Historical placement
was not captured; neither layout-command timing nor current placement establishes
the cause. Controlled update/layout reproduction is a separate pending investigation.

## First conservative union policy (2026-10-02)

`DimensionRuleResult.CompositionIntent` is optional explicit rule evidence: subject
category (Part/Bolt), units, reference identity, local/layer scope, measurement type
(Relative/Absolute/RelativeAndAbsolute) and open/closed closure. Intent participates
in content identity only when supplied; proposals without it retain the previous ID
formula. Known purpose/reference/support are required independently of intent.
Classification, display keys and coordinate proximity never fill these fields.

The first union policy requires equal category, kind/purpose, units, signed direction,
reference semantics and identity, scope, measurement type and outside-outline normal.
It supports open Location/Internal/Edge chains with an identical supported first
point, matching complete source evidence and strictly monotonic endpoint-datum order
extending on the same side. Overall, Check and closed chains remain separate.
`DimensionCoordinateSettings` supplies a positive finite tolerance in model/view mm,
default 0.1 mm. Typed `Query` passes the same per-request settings to bolt preview and
composition; bolt pattern, clustering and edge checks receive it, and cached bolt
previews include it in their key. MCP transport of custom settings remains a later
step; existing MCP requests use the default. This explicitly changes the previous
bolt default of 0.01 mm to 0.1 mm; it is not a change to steel/panel point-selection
thresholds. The plan reports the actual `coordinateToleranceViewUnits`.
Exact supported-point identity/coordinates remain required when deduplicating;
near-equal projected coordinates within tolerance or reused point IDs with different
positions/supports prevent union. Adjacent projected points must exceed the tolerance. Sources
are compared independent of enumeration order. No witness point is relocated or
substituted; concrete legacy rows do not participate in placement intent.

Eligible proposals form a pairwise compatibility graph. Only connected components
whose every pair is compatible are combined. Non-transitive components (A compatible
with B and C, but B conflicting with C) retain all proposals separately and report
`ambiguous-composition` with the affected proposal IDs; no hash ordering chooses a
winner. Independent compatible components can still combine. The union retains each unique supported
point in datum order and recomputes projected segments; original proposals and their
evidence remain intact. A snapshot-local `composed:` ID hashes the policy, tolerance and ordered
source proposal IDs, with shortened-ID collision rejection. `Combined` decisions
reference that ID. Refused inputs retain `Blocked` issues; unresolved/incompatible
inputs retain `KeepSeparate` with reasons. This is not duplicate suppression.

The panel diagnostic adapter preserves supplied intent/reference metadata, so a
synthetic compatible pair exercises its actual detailed-preview path. Current panel
rules still do not supply resolved bases or intent, so their real proposals remain
separate. Legacy rows, compact responses and batch writing use the original proposals.
Production rule enrichment and read-only live union acceptance remain pending; no
new composition policy has been deployed during this code stage.

Tests cover compatible pairs/triples, reordered inputs/sources, arbitrary vectors,
backward datum order, category/purpose/units/reference/scope/type/normal mismatches,
conflicting point identity/support, opposite spans, unresolved metadata, closure,
nonmonotonic order, blocked inputs, tolerance boundaries/configuration, ambiguous
components under permuted addresses, independent unions and diagnostic-adapter
legacy parity. Bolt tests verify the new default and request-specific pattern checks;
Query tests cover settings propagation and cache separation.

The dimension-position investigation is deferred: the user reported a possible model
change and consequent drawing update during the earlier capture. This is a plausible
explanation, not a verified cause, and the position difference is not treated as an
established dimension-writer defect.

## Foundation and boundaries

Keep the frozen view context, detached geometry, pure rules and verified writer.
Extend existing `DimensionRuleResult`, `DimensionRulePoint` and source models only
where the first delivery needs it; no parallel geometry library or full rewrite.

`frozen geometry + part attributes -> part classification -> rules/proposals -> composition plan -> placement/support resolution -> verified writer`

- The context keeps geometry and the original part attributes. Part classification
  (for example frame, batten, sheathing, unknown) belongs to the context/rules side
  as an extension of `PartRoleClassifier`; composition receives the class and never
  classifies parts itself.
- Rules decide what must be measured and which supports are admissible.
- Composition decides what can share a chain or be suppressed as equivalent, with
  a reason for every input. It does not read Tekla or invent geometric supports.
- Placement selects an admissible line location, row and offset. Resolve
  side-dependent supports before writing and revalidate measurement coverage.
- The writer consumes the exact resolved points and placement shown in the plan.
  A wrong layer, missing support or unsupported skew geometry is a planner/source
  capability issue; composition must expose it rather than claim to fix it.

A rule can emit several proposals; one proposal can measure several objects.
Neither one part = one chain nor one external side = one chain is a domain rule.

**Minimal contract for the first delivery.** Proposal identity, purpose, reference,
measurement direction as a unit vector in the view plane (`Top/Bottom/Left/Right` is
an adapter and a side hint, not part of the contract), ordered supports with source
evidence and diagnostics. Inclined and interior placement, local node scope, angular
proposals and the other rows below are extension directions: they are described so
the contract does not block them, not required to deliver the first plan.

| Independent proposal information (extension directions marked later) | Required meaning |
|---|---|
| Identity and scope | Snapshot/context identity, original proposal identity, measured subjects and optional local node/sub-assembly scope. |
| Purpose and reference | Location/internal/edge/overall/check purpose; reference part or supported edge/axis/work point; distinguish measured subject, datum and witness-support owner. |
| Measurement direction | Unit vector in the view plane and its provenance: view axis, main/neighbor part, bolt group or supported sloped edge. |
| Supported points | Full coordinates, ordered identities, all source evidence, true starting datum, closure endpoints, tolerances and eligibility/refusal diagnostics. |
| Placement intent | Admissible normals/regions and placement reference; outside assembly, local to selected geometry, inside, or explicit fixed line. Concrete row/offset is resolved later. |
| Composition evidence | Input proposal identities, policy/version, preserved measurement coverage and reason for combine, keep, suppress or block. |

**Closed vocabularies, aligned with the Tekla dimensioning rule model.** Tekla
separates what is dimensioned from what it is measured from; the proposal carries both
as small closed sets instead of free text.

- Purpose: `overall`, `location`, `internal`, `edge`, `check`. Today's `kind` values map
  onto it. Tekla's types (Overall, Edge shape, Secondary parts, Neighbor parts, Holes,
  Filter) are the reference for later additions.
- Reference (`Measure from`): `assembly`, `mainPart`, `namedPart`, `filter`, `currentPart`,
  `none`; modifier for the support kind: `boundingBox`, `nearestEdge`, `midpoint`.
  Adding `grid` references is an independent option, not a geometric support kind.
- A chain is not tied to one part or to an external side of the assembly: its measured
  subjects and its reference are separate fields.
- Only the subset the current rules can produce is supported at first; the rest is
  reserved vocabulary, unsupported values stay explicit.

The contract must represent arbitrary direction and local placement without forcing
`Top/Bottom/Left/Right`. Existing four-side responses remain adapters. Representation
alone does not enable skew/internal automatic placement: unsupported policies must
remain explicit. Angular dimensions have different geometric inputs and are a later
planner family, not a disguised straight-chain direction.

## Single first delivery: read-only composition plan

The first useful result is an inspectable before/after plan over the existing
proposals: original proposals, part classes, selected policies, composed chains and
the reason for every decision. It creates no drawing objects. The first adapter is
`TimberPanelChainPreview` and its existing rules, because the reported active
problems (layers, incomplete chains, skew parts) pass through that path and extracting
section-only `Consolidate` would not exercise it. The delivery is defined by the
generic plan, not by the panel: other preview families are later adapters. Preserve
and expose unsupported and missing-support cases instead of fabricating a valid chain.

**Layer is an explicit classification, not a stored property.** It is produced by
part classification (see the pipeline), not by composition. The model carries no
layer field. Classify each part from project-configured signals: mark prefix, part
name and profile; the drawing's `Zone` only after its source and meaning are
established. Record for every part the original values, the classification rule that
fired and the reason. Unknown and contradictory cases are reported as such and never
resolved silently. No built-in `B`/`T` table: prefixes and names are each plant's
convention, as for `excludePrefixes`. Observed on EW.1 - 6: battens appear as
`B/1, B/2, B/4, B/5` (prefix `B`, TIMBER, 28X70) and as `T/89, T/77` (prefix `T`,
C24, 45X70), all named `BATTEN`. Different prefixes alone do not prove a classification
conflict. A configured name-based rule may classify both sets as battens. Report a
conflict only when configured rules produce incompatible classes without an explicit
precedence rule; preserve the matched rules and evidence.

**The first plan must answer, for every point:** which part supplies it, its class,
the classification rule and reason, and whether it is included or excluded and why.
For every incomplete or unsuitable chain it must give the cause (missing support,
unlocated part, unsupported inclined case, excluded or contradictory layer).
The result explains why a chain is incomplete; it is not another preview.

**Done when (small pure tests first; captured drawings and live review later):** the plan explains,
for each case below, the origin, class, rule and inclusion reason of every point and
the cause of every incomplete or unsuitable chain.

- Incompatible classification rules without explicit precedence produce a conflict;
  a configured name-based rule may classify equal names across different prefixes.
- Parts without a position and chains with missing supports are listed with causes.
- An inclined part and the chains it makes incomplete are listed; unsupported inclined
  cases are refused, not fabricated.
- A clean control case keeps its existing chains unchanged.

**Initial fixtures, to capture later** (examples, not part of the definition above):
EW.1 - 6 has battens marked `B` and `T` with the same name `BATTEN` and several parts
without a position; EW.1 - 1 has an inclined part and incomplete chains; CE.4 is the
clean control. Fixtures normalise runtime model and view IDs; raw IDs are not asserted.

The policy version is part of the plan result so that a result can be reproduced from
the context, the version and the explicit settings.

1. Define the minimal typed proposal/plan contract and deterministic addressing
   together, cover the boundaries with small synthetic tests, then adapt the panel
   proposals. Capturing complete drawing fixtures is not a prerequisite.
2. Show original proposals, selected policies, composed chains, requested/resolved
   supports and all refusal/coverage decisions. Use the same-category, same-purpose
   compatibility case below where it applies; a reasoned keep-separate result is
   valid. Add a small synthetic compatible pair if the panel offers no such pair.
3. Verify pure composition on the small tests and inspect the read-only plan.
   No drawing objects are created in this delivery.
4. Subsequently connect the reviewed plan to existing batch preparation/writing.
   Migrate steel-section decisions through this same boundary using their golden
   fixtures, then bolt proposals. These are later integrations, not alternative
   definitions of the first delivery.

Do not make full preview migration, batch phase extraction or broad optimization
prerequisites. Planner correctness changes remain separately reviewable.

## Composition policies

Keep identical-object grouping, chain combination, duplicate suppression, line
alignment and repeated-spacing notation (`3*60=180`) independent.
Initial combination eligibility: same category and purpose, compatible view/context,
units, direction, reference, datum, dimension type, closure and placement intent,
plus a shared supported point. Check the resulting union and preserve all sources.
Proximity, equal values or numeric coordinate coverage alone prove no equivalence.
Different layer/local scope needs explicit compatible policy evidence.

**Suppression uses one named policy with explicit stage responsibilities.** Thresholds are explicit settings, as in Tekla
(`Do not create dimensions shorter than`, minimum hole size, minimum length for skew
sections), with a reason for every decision. Candidate eligibility belongs to rules,
equivalent-coverage suppression to composition, and readability to placement.
Composition must not infer coverage merely because a short candidate was omitted.
Existing scattered thresholds
(`minimumSegmentViewUnits`, short-part dropping, the readability gap in sections) migrate
into explicit settings/diagnostics without changing their current values or stage
behavior during extraction. Any later transfer that changes which measurements
remain is a separate policy change, not a mechanical refactor.

**Overall versus chain total.** A `RelativeAndAbsolute` chain may also report an
overall span. Equal numeric totals alone prove no duplicate: require compatible
direction, reference, closure endpoints and supported measurement coverage.
The new read-only plan retains original overall proposals and records `duplicateOf`
only with that evidence; it does not silently remove them. This diagnostic decision
does not restore overall chains omitted by legacy planners or force duplicate writes.
Changing the legacy written output is a separate explicitly selected policy.

Return keep-separate when compatibility is unresolved; blocked inputs cannot lose
their refusal diagnostics. Mixed part/bolt and mixed location/internal/edge
combination need later explicit policies and fixtures. Alignment may move dimension
lines, not silently move witness points off supporting geometry. Keep original
proposals available even after composition.

Existing section behavior must migrate rather than gain a competing implementation:
`SectionPartLocationRule` suppresses nearby candidates by a scale-dependent
readability gap; `SectionDimensionChainPreview` merges profile/location and
`Consolidate` suppresses sides by coordinate coverage. Preserve legacy output during
extraction and expose the underlying candidates/reasons. Corrections to coverage
claims or readability suppression are separate behavior changes.
`DimensionPointCatalog.Resolve/Outermost` intentionally substitutes the outermost
support at the same along-coordinate, possibly from another part. Preserve that
policy during extraction and show requested/resolved identities and coordinates;
do not silently apply a global-outermost policy to a new local/interior family.

## Placement stage

Row assignment currently lives in the batch (`ViewDimensionContextProvider.Batch.cs`):
rows follow submission order per side, with the first row at the default paper gap and
later rows at multiples of it. Under this architecture row assignment is a placement
decision. Move it out of the batch into
placement while preserving submission-order row assignment, present values and
results during extraction. Sorting small chains inward and overall outward is a
later explicit placement policy; direction/spacing settings alone do not establish
that sorting. Extend to free/fixed and local placement only with explicit policies.

## Addressing the reviewed plan and batch integration

**Decision: no stored plan state yet.** A pure, deterministic composition is
reproducible from the frozen context, the policy version and explicit settings, so
`planId` is not required for the first delivery. Separate state is justified when a
persistent manual choice or an edited plan appears. The contract below is deferred
until then; it is not a currently supported MCP parameter.

Deferred contract: `planId + chainId`.
A plan binds drawing/view/context identity, `ruleSet`, `chainView`, policy version,
settings and tolerances, source proposal identities and supported-point evidence.
Return them with the read-only plan; IDs alone are not cross-context global IDs.

- Original and composed chains have distinct identities and explicit plan-stage
  metadata. Original proposals remain addressable for an explicit supported write;
  they are not silently replaced by a composed chain with the same display key.
- A later `create_dimensions_batch` extension resolves plan references from the
  cached reviewed plan, never by rebuilding a different rule set under the same
  `Top-location` name. Any placement/style/type overrides are validated and
  reflected in preparation; overrides changing measurement/composition assumptions
  need a new previewed plan.
- Keep current `preview` + required `ruleSet` + `chainView` callers compatible.
  Adapt them to the common preparation path; do not shadow their existing keys.
  For plan references, use the plan's bound settings and reject contradictory
  caller settings instead of falling back to steel or another preview family.
- Reject expired plans, wrong contexts/views, ambiguous/duplicate references,
  incompatible source/composed selections measuring the same coverage, and
  conflicting reference forms before any write. Context refresh/change invalidates
  its plans. Do not retain live Tekla handles.
- Original proposals and composed plans converge on one prepared-write contract,
  placement/support validation and existing verified batch writer. Preserve
  preflight, retention, partial-success reporting, compensation and final read-back.

Acceptance includes identical display keys in steel/panel with different points,
missing or contradictory settings, stale plan references and exact preview-to-write
point equality using fake providers. Live acceptance follows separately.

## Frozen fixtures and regression gates

Start with small synthetic tests for the architecture. When a planner or placement
policy is actually migrated, capture only the necessary regressions. Full drawing
examples do not block the first contract and read-only plan.

Later store versioned JSON fixtures in the test project with detached source geometry,
part roles/layers, contacts needed by rules, filters, scale/units, tolerances and
rule settings, original proposals, resolved supports and expected final decisions.
Capture is pending; this roadmap edit does not create or claim those fixtures.

Give fixtures stable semantic names and normalize runtime drawing/view/model IDs
through a recorded local identity map while preserving distinct sources and
relationships. Do not depend on a live model, mutable Tekla handles or today's IDs.
Record drawing mark/view label/capture metadata separately for provenance.

- Panel fixtures: supported wall/floor, secondary layer/laths, missing support or
  incomplete chain, skew refusal, and the existing partial/exact duplicate cases.
  IW1.1 - 1, RE.1 - 1 and IW1.3 are live acceptance candidates, not stable test IDs.
- Steel regression captures: M.81 Stütze sections G/E, scale 1:5; historical view
  IDs 1908/1409 are capture references only. Preserve legacy profile/location,
  mirrored coverage, readability suppression and outermost substitution.
- Expansion fixture: M.81 front, scale 1:10, historical view 2814, inclined sets
  2961/2989 and interior set 4583. These observations are not yet goldens;
  point-to-bolt association was incomplete and needs independently supported
  evidence before asserting exact source ownership.

Test compatible/incompatible directions, reversed datum, references/layers/scopes,
equal coordinates with different supports, closure, blocked inputs, all four legacy
sides, deterministic/idempotent plans and provenance preservation. Mechanical
extraction must match goldens; semantic corrections require new expected behavior.
Final-readback correctness and measured lookup optimization remain independent gates.

## Reference evidence and future skew/interior policies

References inform boundaries, not an architecture to copy:
[dim](../../../../dim/), [DimensionItem](../../../../dim/ObjectDimensioningTool/DimensionItem.cs),
[DimensionGroup](../../../../dim/ObjectDimensioningTool/DimensionGroup.cs),
[DimensionOperations](../../../../dim/ObjectDimensioningTool/DimensionOperations.cs) and
[ObjectDimensioningCreator](../../../../dim/ObjectDimensioningTool/ObjectDimensioningCreator.cs).
The latter supports local middle/quarter locations and geometry-derived directions.
Do not import mutable chains carrying live Tekla model/drawing objects or silently
copy algorithms that relocate support points.

[x_drawer](../../../../../Refactoring/x_drawer/) and inspected
[x_Drawer_2023.exe](../../../../../Refactoring/x_drawer/applications/x_Drawer_2023.exe)
are outside this repository and require the local checkout layout. Use the versioned
executable in ILSpy, not the root launcher. Intermediate chains carry vector,
normal, offset, Bolt/Part category and Axis/Assm/Node context before creation.
One combination pass checks category equality. Exact interior-selection policy and
unrestricted mixed-category combination were not established in obfuscated code.

Tekla distinguishes direction, measurement purpose, reference and placement:

- [2025 rule properties](https://support.tekla.com/doc/tekla-structures/2025/dra_dimensioning_rule_properties):
  Filter/Edge shape orientation along a sloped edge versus X/Y; Edge shape can do
  both. The skew-section length threshold (default 300 mm) belongs to that rule,
  not all inclined dimensions. Filter dimensions expose `Place dimensions inside`;
  `Measure from` independently selects reference geometry.
- [Integrated dimensioning](https://support.tekla.com/doc/tekla-structures/2025/dra_general_dimensioning_properties):
  skew bolt groups can follow part/group direction; secondary-part skew position
  can use angle, linear dimensions or both. Main-part skew checks use work-point
  X/Y dimensions. `Internal` measurement purpose does not prescribe interior placement.
- [Manual dimensions](https://support.tekla.com/doc/tekla-structures/2025/dra_adding_dimensions),
  [placement](https://support.tekla.com/doc/tekla-structures/2025/dra_placement_properties_for_annotation_objects)
  and [protection](https://support.tekla.com/doc/tekla-structures/2025/dra_protecting_areas_in_drawings)
  separate line direction, Free/Fixed placement and protected geometry/annotations.
- [Combination examples](https://support.tekla.com/doc/tekla-structures/2020/dra_examples_of_combining_dimensions)
  and [identical-object grouping](https://support.tekla.com/doc/tekla-structures/2022/dra_grouping_objects_to_same_dimension_line)
  are policy references, not proof of an accessible Open API rule engine.

Later deliveries add explicit direction-source and local/interior placement policies,
with projected spacing, datum/closure, paper-scale readability and collision gates.
Keep angular planning, depth/occlusion, clipped contours and repeated notation
separate; this foundation does not claim those capabilities are implemented.
