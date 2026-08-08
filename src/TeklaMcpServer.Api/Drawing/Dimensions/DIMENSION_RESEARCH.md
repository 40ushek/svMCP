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

Do not introduce a third confidence taxonomy:

- actionability: 'DimensionDefectConfidence' =
  'Mechanical' / 'Provisional';
- source evidence: 'DrawingPartCandidateConfidence' =
  'ExactGeometry', 'ReferenceGeometry', 'DerivedGeometry',
  'BoundingBoxFallback';
- unresolved association is a status such as 'NeedsReview', not a confidence.

Tekla-native associativity must not be claimed until observable on the validated
Open API path.

