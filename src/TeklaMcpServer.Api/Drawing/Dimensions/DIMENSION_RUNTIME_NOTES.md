# Dimension Runtime Notes

These are validated implementation constraints moved out of the strategic
roadmap.

## Coordinate space

A live drawing reproduced a mismatch where part geometry was in model
coordinates while dimensions were in view coordinates. Point-to-part matching
under that condition produces confident nonsense.

The validated part-geometry path uses
'TransformationPlane(view.ViewCoordinateSystem)'. Do not use
'DisplayCoordinateSystem' for associativity. Persist coordinate-space
provenance and stop association on mismatch.

## Read-back

Tekla normalizes dimension point order on read. For a simple connected chain,
the original start can be reconstructed from segment connectivity. Branching,
disconnected or ambiguous chains must be 'NeedsReview'.

After recreate/apply, reread the edited dimension and its neighbours because
Tekla reflows them. Match by geometry/semantic identity, not dimension ID.

## Association evidence is inferred, not native

'get_dimension_contexts' records that its per-point selection is inferred.
Until the coordinate-space audit is complete, 'DistanceToGeometry' and
'MatchedModelId' must not be read as proof of the native Tekla associativity
rule — a small distance means the point is near that geometry, not that Tekla
associates the two.

'GetDimensionSet()' on a segment returns a live 'DimensionSetBase', not an
identifier. A live handle must never enter a persisted observation; record the
id only. Segment-to-chain membership is known anyway, because traversal goes
from the set downwards. 'GetDrawing()' per object is likewise redundant: the
drawing is constant for an observation and already sits in its header.

## Native text

Native dimension value text can be moved manually in Tekla, but the moved
position is not observable through the validated Open API surface. Text
geometry is supporting/debug data only: text polygon debug may use runtime text
geometry where Tekla exposes it, and otherwise stays a synthetic fallback. This
constraint must not distort the core domain redesign.

Sources already checked, so they are not re-checked:

- 'StraightDimension.GetRelatedObjects()';
- 'StraightDimensionSet.GetRelatedObjects()';
- recursive 'GetObjects()' traversal where available;
- drawing presentation model text primitives;
- reflected public/nonpublic members on 'StraightDimension',
  'StraightDimensionSet' and related attributes.

## LengthList — known gap, do not fix in passing

Recorded 2026-08-01. Left deliberately as it is.

A dimension prints each snap point projected onto the reference line along the
normal, valued from the START of that line. Confirmed against a PDF export: all
26 printed values across four chains matched, including two horizontal chains
counting from the left edge and two vertical ones from the bottom.

'DimensionItem.ReplacePointList' does project onto the reference line, which
fixed an older straight-line measurement that invented fractional values
(2555.25 against a printed 2546, 456.91 against 448) and produced negative
segments. But it still measures from 'PointList[0]', not from the near end of
the line, so a chain whose points arrive opposite to the line reads correct in
magnitude and reversed in order: printed '60 · 1764 · 4309' is reported as
'2545.5 · 4249.1 · 4309.1'.

It was not carried through because both length lists must stay index-aligned
with 'PointList'. 'DimensionOperations' converts a length index to a point
index by adding one ('GetLengthMatchedPointIndices') and treats 'LengthList[0]'
as the first span; ordering values by position along the line breaks that
pairing and matches a length to the wrong point during packet reduction — a
real defect introduced once and caught in review. Sorting 'PointList' instead
swaps 'StartX'/'StartY' with 'EndX'/'EndY' on vertical chains, which grouping,
dedup and arrangement all read, and
'BuildGroups_MergesNearbySegmentsOnSameLineBandWithinSameDimension' fails on
exactly that.

Closing it needs either a separate printed-value list with its own
'length -> point index' mapping, leaving 'LengthList' alone, or a downstream
contract where a length and its point travel as a pair rather than by parallel
index.

Meanwhile 'LengthList' is safe for spans and for comparing chains against each
other. It is **not** the absolute run shown on the sheet: diagnosing a drawing
by reading it as one already cost a full session chasing snap drift that the
drawings did not have. Test coverage is partial —
'ReferenceLineSuppliesTheAxisWhenPresent' proves the reference line wins over
the direction field, but nothing covers the near end or a line running the
other way.

## AngleAtVertex

Tekla support confirmed that 'AngleDimension' objects with
'AngleTypes.AngleAtVertex' cannot be moved visually by changing 'Distance'
through the Open API. Observed: 'Distance' changes and persists, 'Modify()'
returns true, and the arc/text does not move. Dead ends already checked —
changing 'Origin' is not a workaround because it changes the measured angle
geometry, and 'Placing' ('Free' / 'Fixed') does not affect the behaviour.

Current policy: 'MoveAngleDimension' returns 'Moved=false' with a clear reason
for both 'AngleTypes.AngleAtVertex' and 'AngleTypes.AngleAtVertexGradian', and
never falls back to moving 'Origin'. A delete/recreate workaround, if ever
added, must be a separate explicit feature.

## Transport

Recorded 2026-08-02. The persistent bridge is a line-delimited JSON protocol
over local stdin/stdout, so compression is not automatically the bottleneck.
Measure before changing the protocol, correlating for the same command: Tekla
execution inside the bridge ('executeMs'), bridge serialization and write
('writeMs'), server-side request write / response read / JSON parse, and
request/response byte counts.

The first acceptance result is a report over several large geometry calls
showing the median and worst-case share of time attributable to Tekla execution
versus serialization and pipe transfer. Do not introduce gzip or change line
framing until that report shows a material transfer cost; if compression is
justified, keep it inside the existing bridge contract and preserve the plain
JSON result seen by MCP tools.

Coordinate reduction is a separate, low-risk optimization: a compact wire
projection rounded to 0.01 mm, while internal calculations and the
observation/hash representation keep their existing precision. Coverage, anchor
matching and maximum coordinate error must be tested before it becomes the
default MCP output.

## IW.10 coordinate mismatch — 2026-08-01

On drawing IW.10, view 1218, after nine drawings where the contract held, part
geometry was returned in model coordinates while dimensions were in view
coordinates. The mismatch reproduced on repeated reads; reading dimensions in
between did not change it. The top view returned a third orientation.

Across thirteen parts, the extents were X=1363, Y=100, Z=3738.2, while the
chains measured 1363 wide and 3738.2 tall. The wall height was therefore in
part Z and dimension Y; part Y was wall thickness. Every part had
axisY=[0,0,1000] (world Z), although a correct view read points it up the wall.

This invalidates any association planner that joins points without an explicit
coordinate-space contract. The mismatch evidence is preserved under
'cases/dimension_cases/assembly/965a95fe-.../before/'.

## Raked-top evidence

On IW.1, view 1214, chains 1622 and 1686 both used (1833.5, 1691.1), where the
only candidate was a bounding-box corner of the raked top plate. The real
vertices at that end were y=1284.6 and y=1223.1; y=1691.1 existed only at the
opposite end, where a genuine vertex existed. This was an imagined assembly
box, not a valid part anchor.

'fallbackOnly' means matched only by hull or box evidence; it is a finding for
adjudication, not proof of an error. SolidGeometryComplete must be checked
before treating it as defective because failed solid traversal can leave only
axis and box candidates.

The drawing rule confirmed on EW.4-6 is: on a raked top the chain ends at the
panel's real corner; an overall may span two different x-coordinates; the
anchor is the raked member's real corner, not the top of the nearest batten.
The overall height is the projection between the lowest and highest real
points, even when no single part has that height.
