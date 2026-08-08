# Dimension Research and Graph Contracts

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

## Three graph roles

### RelationGraph

A typed graph with 'AnchorNode' (dimension point), 'SourceFeatureNode' (face,
edge, vertex or assembly feature), and 'DimensionRelation' edges. Relations
retain axis, row/type, source role and evidence. Control diagonals may
intentionally form cycles and are protected by project policy.

### CandidateConflictGraph

A placement candidate is a side/offset option for one chain. Edges represent
geometric collisions. Candidates from the same chain are mutually exclusive.
Fixed obstacles (view boundary, part geometry and reserved annotation areas)
are rejected before solving.

### OrderingDAG

A policy-generated partial order scoped by view, layer and side. Typical
precedence is base, internal, inter-element, then overall/control chains, but
plant policy may alter it. It is not a measurement or collision graph.

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

