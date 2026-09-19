# Roadmap: View Stitching by Model-to-Sheet Transform

## Goal

A standalone tool that stitches two drawing views into one continuous picture.
It must move the target view **only when the two views already render every
model-space point identically on the sheet apart from a paper-space
translation**.

Typical case: a house-plan view and a porch view have independent extents but
the same projection and scale. Their pictures belong at different places on
the sheet today, yet should continue across their common edge.

This is deliberately **not** part of `fit_views_to_sheet` / projection
alignment (`DrawingProjectionAlignmentService*`). Those paths validate every
candidate move against other views and reserved areas. Here overlap is often
the intended result, because the two views become one visual composition.

## Non-goals

- Not a replacement for GA-axis or main-part projection alignment.
- Does not resolve collisions, change extents, or participate in
  `fit_views_to_sheet`'s automatic pipeline.
- Does not use a selected point, `modelId`, or a drawing part as an anchor.
  A point can prove only one coincidence; it cannot prove equal scale or
  projection direction.
- Does not automatically change target scale or recreate/reorient a view.
  Such operations are separate features, not a safe fallback for stitching.

## Coordinate systems

The algorithm uses three coordinate systems:

```text
M = global model coordinate system
V = local coordinate system of one drawing view (`View.ViewCoordinateSystem`)
P = paper/sheet coordinate system
```

For each view, construct the complete affine mapping:

```text
F_view : M -> V -> P
```

The first stage is pure 3D geometry:

```csharp
var modelCs = new CoordinateSystem(
    new Point(0, 0, 0),
    new Vector(1, 0, 0),
    new Vector(0, 1, 0));

var modelToView = MatrixFactory.ByCoordinateSystems(
    modelCs,
    view.ViewCoordinateSystem);
```

`MatrixFactory.ByCoordinateSystems` transforms coordinates from its first
coordinate system to its second one. It must be used only with coordinate
systems expressed in the same work-plane context. The algorithm must not
change the model work plane.

Transforming `view.ViewCoordinateSystem.Origin` into its own coordinate
system is **not** a context check: it is zero by definition. The Host probe
must therefore log both `ViewCoordinateSystem` and `DisplayCoordinateSystem`,
and explicitly report when they differ. The project currently uses
`ViewCoordinateSystem` for the validated view-local geometry path, but that
choice remains a live-drawing assumption for stitching until visual testing
confirms the paper result. Until a case where the two published coordinate
systems differ has been verified, an apply command must refuse that pair (and
also refuse a view whose `DisplayCoordinateSystem` cannot be read).

The second stage is the established view-local -> sheet projection:

```text
local = modelToView.Transform(modelPoint)
paper.X = view.Origin.X + local.X / view.Attributes.Scale
paper.Y = view.Origin.Y + local.Y / view.Attributes.Scale
```

`view.Origin` and every resulting move vector are in **paper coordinates**.

## Stitching algorithm

Given base view `A` and target view `B`:

1. Build `F_A` and `F_B`, the complete `M -> P` mappings above.
2. Evaluate both mappings on the global model basis. Use a non-zero fixed
   length `L` for readable diagnostics, then divide vectors by `L` if a
   per-model-unit value is needed:

   ```text
   O  = (0, 0, 0)
   X  = (L, 0, 0)
   Y  = (0, L, 0)
   Z  = (0, 0, L)

   pO = F(O)
   vX = F(X) - F(O)
   vY = F(Y) - F(O)
   vZ = F(Z) - F(O)
   ```

   `pO` is the translation part of the model-to-paper mapping. `vX`, `vY`
   and `vZ` are its linear part: they encode the view's scale and projection
   direction on paper.

3. Compare the complete linear parts in paper units. For each basis vector,
   report both the relative length difference and the angle between its
   normalized paper vectors:

   ```text
   vX_A ~= vX_B
   vY_A ~= vY_B
   vZ_A ~= vZ_B
   ```

   Do not use an absolute residual that silently depends on the diagnostic
   basis length `L`. The decision tolerances are relative scale difference and
   direction angle; the raw residual vector remains diagnostic output.

   A vector whose paper length is at most `1e-6 mm` is treated as zero. This
   keeps the projected depth (`vZ`) stable in the presence of Tekla floating
   point noise. If only one of a pair is zero, report a deterministic mismatch
   rather than deriving an angle by division by zero.

   - If any vector differs, the views are **not stitchable by translation**.
     A changed length means scale mismatch; a changed direction means
     projection/orientation mismatch. Do not move either view.
   - If all vectors match, the two model-to-paper mappings differ only in
     their translation part and can be stitched safely.

4. Compute the target move in paper coordinates:

   ```text
   deltaPaper = pO_A - pO_B
   ```

5. Apply only this paper-space translation to the target view:

   ```csharp
   var origin = targetView.Origin;
   origin.X += deltaPaper.X;
   origin.Y += deltaPaper.Y;
   targetView.Origin = origin;
   targetView.Modify();
   drawing.CommitChanges();
   ```

   There is no `ViewPlacementValidator` call: visual overlap is allowed by
   this explicitly invoked operation.

6. Re-read the target view and rebuild `F_B`. Verify residuals for `O/X/Y/Z`:

   ```text
   F_A(O) ~= F_B_after(O)
   F_A(X) ~= F_B_after(X)
   F_A(Y) ~= F_B_after(Y)
   F_A(Z) ~= F_B_after(Z)
   ```

   Only a successful read-back is a successful stitch. Invalidate the target
   view's geometry cache after commit.

When step 3 passes, the calculated translation makes the mappings equal for
every model-space point, not merely for one user-selected anchor:

```text
F_A(P) = F_B_after(P) for every model point P.
```

This equality is about the projected XY image only. Two views with the same
projection direction and scale but different depth windows or elevations can
pass the transform test. That is allowed for stitching only when the intended
visual result makes such a depth difference acceptable; the command must
return the depth-window facts as diagnostics rather than silently claiming
semantic equivalence.

## Folded-path development around a hinge line (planned)

This is a separate operation from translation stitching. It represents two
view planes as a sheet folded through an arbitrary dihedral angle, then opened
along their common hinge. In drawing terms, it is a continuous walking path
that turns through any angle: the two views must meet at the turn line, but
their other in-plane directions intentionally differ.

Given view planes `A` and `B`, derive their model-space normals from their
coordinate systems:

```text
nA = normalize(A.AxisX × A.AxisY)
nB = normalize(B.AxisX × B.AxisY)
h  = normalize(nA × nB)     // hinge direction
```

The mode is a candidate only when `nA` and `nB` are not parallel or
anti-parallel within an angular tolerance (0.1°, i.e. `|nA × nB| > sin 0.1°`;
a smaller angle makes `H0` ill-conditioned), both views use the same paper
scale, and both published view coordinate systems match their respective
`DisplayCoordinateSystem`. The fold angle is diagnostic information:

```text
foldAngle = acos(clamp(dot(nA, nB), -1, 1))
```

It does not affect the translation itself. Solve the two plane equations for
a point `H0` on their intersection line:

```text
nA · (H0 - A.Origin) = 0
nB · (H0 - B.Origin) = 0
H0 = (dA * (nB × h) + dB * (h × nA)) / |nA × nB|,   dX = nX · X.Origin
```

The division by `|nA × nB| = sin(foldAngle)` is required because `h` is
normalized; without it `H0` lies on both planes only at 90° (at 60° it was
134 mm off a plane in a numeric check).

Use `H0` and a non-zero point on the hinge to validate the only mapping that
must be equal in this mode:

```text
hA = F_A(H0)
hB = F_B(H0)
eA = F_A(H0 + L*h) - hA
eB = F_B(H0 + L*h) - hB
```

`eA` and `eB` must have equal paper length and direction. Opposite direction
is not sufficient: a translation would join only `H0` and reverse the rest of
the hinge line. If they match, the fold move is:

```text
deltaPaper = hA - hB
```

This makes the entire image of the hinge line coincide. It deliberately does
not require the other model basis directions to match: they are the two
branches of the developed path after the turn. Parallel or anti-parallel
planes have no unique hinge line and remain the domain of ordinary
translation stitching.

Before applying the move, the side of the hinge is validated for each view
from where the view's frame actually lies: the sign of
`hingeEdge × (frameCenter - hinge point)` on paper, with the target frame
center taken after the proposed move. A developed path needs the two frames
on opposite sides of the hinge; same side, or a center lying on the hinge
line (undetermined), is rejected. The frame center is a proxy for the view's
content, not a proof of it. The rule never uses selection order, except that
selected mode takes the larger frame as base.

The Host probe `--view-fold-probe-selected` logs normals, `H0`, hinge
direction, paper-edge residuals, the frame sides and `deltaPaper`. The
explicitly named `--view-fold-apply-selected` applies only a candidate that
passes both the hinge mapping and the opposite-side check, then performs a
hinge read-back. It is experimental, for a user-observed live test, not
general automatic layout. Overlap of the two frames beyond that is allowed by
this explicitly invoked operation. Changing a view's `Origin` can translate an already suitable
projection into its unfolded position; it cannot rotate or mirror a projection
that has the wrong orientation.

## Host diagnostic and controlled apply

The Host supports the following commands:

```text
TeklaMcpServer.Host.exe --view-stitch-probe <baseViewId> <targetViewId>
TeklaMcpServer.Host.exe --view-stitch-probe-selected
TeklaMcpServer.Host.exe --view-stitch-apply-selected
```

The explicit-ID and `--view-stitch-probe-selected` modes are read-only. In
selected mode, exactly two views must be selected; the view with the larger
`Width × Height` frame area becomes base and the smaller one becomes target.
Equal areas are rejected as ambiguous. This is a convenience policy for the
house/main-view case, not a geometric requirement of the transform algorithm.

`--view-stitch-apply-selected` performs the same calculation and only then
calls `TeklaDrawingViewApi.MoveView` for the selected target, followed by a
read-back calculation. It refuses the write when the linear parts differ, or
when `ViewCoordinateSystem` differs from `DisplayCoordinateSystem` (including
an unavailable display system). No command changes scale, extent, projection,
or the current work plane.

All modes log both model-to-paper mappings, both published view coordinate
systems, relative scale and direction residuals, the translation-only
candidate, and `deltaPaper`. The calculation and its immediate read-back are
still not proof of Tekla's final rendering; visual inspection remains the
acceptance check.

Validate at least these live cases before exposing an MCP write command:

1. Equal scale and equal projection: apply `deltaPaper` manually and inspect
   that the views join correctly on the drawing.
2. Different scale: probe rejects the pair; a point-only alignment would have
   been a false positive.
3. Different projection direction or mirrored axes: probe rejects the pair.
4. Already stitched pair: `deltaPaper` and all post-check residuals are near
   zero.
5. After a successful visual stitch, update the drawing and read it again to
   verify Tekla did not reflow the views back to another layout.

### Recorded live result: case 1 (plan views)

`Sills plan`: base `1510` and targets `4734`, `5349`, and `7386` were
successfully stitched with `--view-stitch-apply-selected`; each read-back
reported target-origin delta `(0, 0)`, and the drawing was visually confirmed
correct. Both views had scale `1:40`, unit `ViewCoordinateSystem`, and
`ViewCoordinateSystem == DisplayCoordinateSystem`. This validates the
view-local-to-paper formula for this plan case, but it does **not** validate
the matrix branch for non-zero/rotated view coordinate systems or a case
where the published view and display coordinate systems differ.

### Recorded diagnostic: perpendicular candidate

On `Sills plan`, views `E` (`12736`) and `F` (`12846`) both had scale `1:20`
and matching view/display coordinate systems. Their planes were
perpendicular: `E` showed model X/Z and `F` model Y/Z. The standard stitch
correctly rejected them because their complete linear parts differ. They are
a live folded-path probe candidate: the probe found the hinge
`(1290.664174, 9046.642411, 0)` in the model, fold angle `90°`, matching
paper hinge direction, and target delta `(-51.659875, -0.343951) mm`. That
run used the earlier side diagnostic (`h × n`, not the frame position), so its
`same-side` result is void. It also predates the `H0` fix, which does not
change a 90° result. Re-run to get the frame-based side. No folded move has
been applied or visually validated; only a 90° fold has been probed, so the
non-90° `H0` path is covered by a numeric check only.

## Status

Host probing and an explicitly requested, guarded Host apply mode are
implemented. The plan-view case is visually validated; rotated/non-zero view
coordinate systems and a `ViewCoordinateSystem != DisplayCoordinateSystem`
case remain required live tests before expanding write support. The read-only
folded-path probe is implemented. The fold apply mode is experimental and
requires visual confirmation after every use.
