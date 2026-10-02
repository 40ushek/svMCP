# Captured part-layer regression

`timber-batten-mixed-prefixes.v1.json` is a small capture from the read-only
EW.1 - 6 review on 2026-10-02 (AssemblyDrawing, Front and Top, scale 1:25).
It retains only classification inputs and point-owner links. It is not a geometry,
support-resolution or full composition golden.

Each part declares `expectedSource`: `observed` for Front plan results and
`evaluated` for calculations over Top attributes. The observed regression theory
checks only Front expectations and ownership links. A separate pure-rule test uses
Top attributes to check C24 selection and unmatched sheathing; it does not replay
the evaluated JSON expectations as live regression goldens.

- `timber-batten` and `frame-beam` attributes and expected classifications came from
  the Front panel plan. The four live TIMBER battens classified successfully; the
  deliberately contradictory name/prefix rules produced four conflicts. This
  fixture retains one representative from each relevant attribute family.
- `c24-batten` and `sheathing` attributes came from the Top context. Their expected
  classes are evaluations of those frozen attributes under the recorded rules,
  not observed classifications in a live Top panel plan. Panel preview supports
  only Front/Back; the C24 battens were absent from the Front snapshot.
- The five point links came from Front's diagnostic bindings. Only links whose
  owners belong to the retained representative parts are included. Source kinds
  and distinct owner relationships are retained, without coordinates, feature
  geometry or original source multiplicity. The replay tests use placeholder
  coordinates solely because the binding projection does not consume geometry.
- Model IDs are fixture-local integers; point IDs and part positions are local
  semantic aliases. No drawing GUID, live model/view/point IDs or file paths are
  required by the test. Generic Name/Prefix/Profile/Material values are retained
  because they are the classification inputs. The capture's reverse identity map
  and complete live responses remain in ignored local review artifacts.

Live preview, composition decisions and chain diagnostics were identical with and
without rules; an identical repeated request reproduced the same response. All
15 live point bindings resolved to captured part records. These observations are
recorded in the roadmap; this reduced fixture does not replay the full planner.

The existing-dimension comparison was not entirely stable: one Bottom overall
line was read at -422.5 and subsequently at -575.885 view units. Its ID, span and
segment length remained the same; the later repeat was stable. No write/arrange
commands were called. The cause is unestablished, so drawing-position invariance
is not an acceptance claim of this capture.

The follow-up read-only audit placed the differing reads at 21:40:39 and 21:41:44
local time (UTC+2). Logged layout commands at 19:49–19:50 preceded that interval;
the arrange command failed, and the three fit commands succeeded. All five sets
currently report `Fixed` placement, as do the loaded standard/overall attributes.
Historical placement was not captured. These facts do not establish the cause;
controlled update/layout reproduction remains a separate task.
