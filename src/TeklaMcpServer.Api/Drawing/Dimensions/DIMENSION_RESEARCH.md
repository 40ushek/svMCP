# Dimension Research Notes

This document holds research notes moved out of the main roadmap. The roadmap
keeps only the decisions that affect implementation.

## Main conclusion

Do not port one automatic-dimensioning paper wholesale. Separate semantic
dimension selection, completeness/redundancy checks, geometric placement, and
Tekla apply/read-back verification.

## Prior art

- Feature-based dimensioning: [Chen, Feng and Lu](https://doi.org/10.1016/S0010-4485(00)00132-9).
- Graph consistency: [Todd](https://doi.org/10.1137/0402022).
- Completeness testing: [Sui et al.](https://doi.org/10.4028/www.scientific.net/AMM.319.351).
- Planar rigidity reference: [Laman](https://doi.org/10.1007/BF01534980).
- Curve-chain dimensioning: [Li and Yang](https://doi.org/10.32604/cmc.2020.011398).
- Deterministic placement: [Kakoulis et al.](https://doi.org/10.54684/ijmmt.2023.15.3.67).
- Global DAG/genetic search: [IAGA](https://doi.org/10.17559/TV-20240127001297).
- BIM/fabrication generation: [BIM framework](https://doi.org/10.1016/j.compind.2021.103395).

Laman's condition is only a theoretical reference for selected planar
bar-constraint subproblems. It is not a Tekla completeness criterion.

## How Tekla's own automatic dimensioning is set up

Reference only, read on 2026-11-12 from the Tekla help. It records what Tekla
offers, not what this project should do. The pages are option tables, not an
algorithm description; `support.tekla.com` now redirects to `help.trimble.com`.

Integrated dimensioning (single-part, assembly and cast unit drawings):

- [General tab](https://help.trimble.com/doc/tekla-structures/2022/dra_general_dimensioning_properties):
  combine levels (a larger number combines more; the levels are not defined),
  close dimensions (no / in X / all) with a separate rule for short dimensions,
  forward offset (how far a corner base point is searched before an edge point
  is used), recognizable distance (the asymmetry limit for internal dimensions
  of secondary parts), main dimension position (auto / above / below; skew
  dimensions of the main part go on the opposite side). No defaults are given.
- [Position dimensions tab](https://help.trimble.com/doc/tekla-structures/2021/dra_position_dimension_properties_for_singlepart_assembly_and_castunit_drawings):
  parts and bolts are positioned to the main part reference line or between
  work points; a secondary part is located by bolt, by part edges or by both
  (which edge is not stated); reversed direction for running dimensions puts the
  zero at the other end; centered parts and bolts can be dimensioned to the main
  part center lines; equal dimensions combine as `3*60` or `3*60=180`, accuracy 0.1.
- [Part dimensions tab](https://help.trimble.com/doc/tekla-structures/2021/dra_part_dimensioning_properties_for_singlepart_assembly_and_castunit_drawings):
  internal dimensions of secondary parts none / necessary / all ("necessary" is
  only described as what assembling needs); main part overall once / all views /
  none; assembly overall length only / all / off; preferred dimension side
  (front or side view); shape, radius, bevel, knock-off and work-point check
  dimensions.
- [Bolt dimensions tab](https://help.trimble.com/doc/tekla-structures/2022/dra_bolt_dimensioning_properties):
  internal bolt dimensions none / internal / all (all adds the distance from the
  outermost bolt to the part edge); skewed groups in part or bolt-group
  direction; check dimension between extreme bolts of the main part or the
  assembly; extreme bolts to work points; combining as above.
- [Dimension grouping tab](https://help.trimble.com/doc/tekla-structures/2022/dra_dimensioning_dimension_grouping_properties):
  identical parts, bolts, components and cuts can share one dimension line with tags.
- [Sub-assemblies tab](https://help.trimble.com/doc/tekla-structures/2022/dra_subassembly_dimensioning_properties):
  a sub-assembly is positioned from bolts, extrema points or its reference point.

View-level dimensioning rules:

- [Dimensioning rule properties](https://help.trimble.com/doc/tekla-structures/2025/dra_dimensioning_rule_properties):
  types are overall, edge shape, secondary parts, holes, recesses, distance to
  grid, filter, neighbor parts and center of gravity; holes do not cover bolts.
  Measure from: assembly, main part, part name, filter, current part. Dimension
  to both ends or the center point. With both opposite sides selected, each
  object is dimensioned on the side closest to it. Origin defaults: left for
  horizontal, bottom for vertical, or the nearest edge per dimension. Combine on
  one line with a tolerance of 50 mm (the largest distance at which objects
  count as lying on one line). Do not create dimensions shorter than: 0.
  Minimum length for a skew section: 300 mm; shorter ones are dimensioned
  horizontally and vertically. Close lines: on.
- [Add automatic view-level dimensions](https://help.trimble.com/doc/tekla-structures/2025/dra_add_automatice_view_level_dimensions):
  the order of rules is the order of lines, the first rule closest to the part,
  but Tekla takes the first suitable location under the placement and protection
  settings, so the final order can differ. Line spacing is not stated. Integrated
  is the way to get bolt dimensions.
- [Define dimensioning](https://help.trimble.com/doc/tekla-structures/2026/dra_dimensions):
  in single-part, assembly and cast unit drawings automatic dimensions are set
  view by view; no rule for avoiding the same dimension on two views is stated.

Not found or not read: the dimension planes table (`dim_planes_table`), the
meaning of combine levels 1-5, which edge of a secondary part is taken, and the
defaults of most Integrated options.

## Constraints on a future placement layer

A typed graph model (semantic relations, placement-candidate conflicts,
chain ordering) was drafted on 2026-08-08 and deliberately dropped: there is no
planner creating new dimensions yet, so there is nothing to order and nothing
to conflict. Reviewing it did produce two constraints worth keeping, whatever
structure the placement layer eventually uses.

**Identity must be semantic.** A dimension's identity cannot be its Tekla ID or
its point index: 'recreate_dimension' renumbers the set, and read-back
normalizes point order. Anything comparing a before and after state — verifying
that a reflow moved the intended dimension, or that source associations
survived an edit — needs identity derived from geometry and source anchors
instead.

**Several sources at one point is not an error.** Where a stud meets its plate,
one dimension point legitimately lies on two parts. Coverage already keeps all
matches and does not pick a winner. Which part the dimension is actually
locating is a subject/policy decision, not a nearest-point calculation.

## Confidence vocabulary

Do not introduce a second confidence taxonomy beside source evidence:

- source evidence: 'DrawingPartCandidateConfidence' =
  'ExactGeometry', 'ReferenceGeometry', 'DerivedGeometry',
  'BoundingBoxFallback';
- unresolved association is a status such as 'NeedsReview', not a confidence.

Tekla-native associativity must not be claimed until observable on the validated
Open API path.
