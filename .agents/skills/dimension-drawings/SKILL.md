---
name: dimension-drawings
description: Place, repair, clean up, or review dimensions on a Tekla AssemblyDrawing in this project, from one requested chain to a complete drawing. Do not treat code-only work as permission to change the drawing.
---

# Tekla assembly dimensions

Code proposes prepared chains; select their keys. This skill does not authorize
changing a drawing during code work or review. Keep the user's scope and choices.
Do not change code, settings or roadmaps during placement without a separate request.

## Route: read only the file the conditions select

Call get_drawing_context first. Then take the first matching line:

| Condition | Read and follow |
|---|---|
| task is repair, review, debugging, a narrow edit, custom placement or a detailed report | [references/workflows.md](references/workflows.md) |
| `drawing.type` is not `AssemblyDrawing` | nothing: stop and report the type |
| `drawing.mainPartMaterial` = `steel` | [references/steel-assembly.md](references/steel-assembly.md) |
| `drawing.mainPartMaterial` = `timber` | [references/workflows.md](references/workflows.md), timber route (`ruleSet=panel`) |
| anything else (`concrete`, `misc`, `unknown`) | nothing: stop and report the material |

Read no other file for that run. The selected file names any further reference it needs.

## Details of a placed selection

For full fields of the same selection, call get_view_dimension_context with viewId
and previewPlanId. Do not request chainDetails merely to see more: on sections it
builds a different split-chain variant. Keep the original plan ID, ruleSet and
chainView when writing a reviewed selection.
Review is read-only; overlays are drawing mutations. Do not expand a narrow request
to other parts, sides or views, or ask again for choices already authorized.
