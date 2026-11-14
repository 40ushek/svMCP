# Standard steel assembly run

Selected by [../SKILL.md](../SKILL.md) for a steel assembly drawing; the dimension
agent reads it directly. These steps are complete for ordinary placement. Do not
load other references, source code or roadmaps for this run.

1. get_drawing_context is already read (it chose this file). Read get_drawing_views,
   identify the target views and skip _3DView.
2. Read get_drawing_dimensions with compact=true. If dimensions exist and the
   user has not said to keep, replace or add, ask once. Reuse an already stated
   choice. Never delete without authorization.
3. Finish one view before reading another: switching views expires its context.
   Order: front, sections/end views, top, bottom, back. Front precedes back and
   top precedes bottom so repeat detection sees the earlier dimensions.
4. Call get_view_dimension_context(viewId, questions="chain,scale,repeats",
   ruleSet="steel"). Keep contextId and previewPlanId from that answer.
   If isComplete=false or sectionPreviewStatus says refused, write nothing on
   that view; report the reason and continue. A provisional section/end preview
   may be used, with that limitation reported.
5. Each row has `chains`, the ones the code allows to place, and `refused`, the
   ones it does not, as key and reason (`repeat`, `incomplete`, `boltClass`,
   `boltReadIncomplete`). Select every key in `chains` of every row, including
   `bolt-position-*` at the end of the Bolt row. Never select a refused key, never
   drop a key from `chains` by your own judgement, and do not compare segments
   yourself. A non-empty Bolt.readIncomplete means no bolt is placed on the view.
   If the user explicitly wants repeats, read chain,scale without repeats and
   select from that answer instead.
6. Send one create_dimensions_batch for the view with the same viewId,
   contextId, previewPlanId, ruleSet="steel", chainView="chain" and chainsJson,
   e.g. [{"preview":"Bottom-location"}]. Send keys only: no pointIds,
   coordinates, direction, paperGapMm or attributesFile. The batch supplies
   offsets and attributes; unavailable defaults fall back to standard and are
   reported. No eligible keys means no batch call.
7. Check that every selected key has a result. created and retained are fine.
   For merged, uncertain, failed or skipped, stop on this view and report the
   key/status/error; do not retry blindly. A finalReadError means the write was
   not verified: report it. Report attributesFileUsed, renderedLineStatus and
   boltGeometryVerification when present. If the plan expired or dimensions
   changed before any write, re-read and select from the new answer. Never put
   a new ID on an old selection without reviewing it. The batch is the read-back;
   do not make another dimension read just to confirm it.
8. Give a short final report: created/retained counts and view count; failed
   keys and refused views with reasons; any Bolt.readIncomplete. Say that
   section/end chains are provisional and line positions were not checked by eye.
   Do not list successful or refused chains, segments or diagnostic fields
   unless requested.

Do not request raw bolts, contacts, source points, part solids, debug overlays or
obsolete boltChains in this run. Do not rebuild or repair a refused chain by hand.
If a proposal looks wrong, write nothing on that view and report it. Dimensions
alone do not require refresh=true; refresh source geometry only after model/view
edits or uncertain source identity.
