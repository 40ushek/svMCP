# AI Agent Instructions

## Initial step (MANDATORY)

Before performing any task:

1. Scan the entire repository.
2. Build a mental map of the project structure.
3. Identify:
   - main modules
   - architectural layers
   - entry points
   - core services
   - domain models
4. Understand dependencies between modules.
5. Detect architectural patterns used in the project.

Do not modify code before the architecture is understood.

## Architecture rules

- Follow existing architecture and coding patterns.
- Prefer modifying existing components instead of creating new ones.
- Avoid code duplication.
- Keep changes minimal and localized.
- Do not introduce new frameworks or patterns unless explicitly requested.

## Code changes

When implementing changes:

1. Identify affected modules.
2. Explain the change briefly.
3. Modify only necessary files.
4. Preserve public APIs unless modification is required.

## Code quality

- Keep functions small and focused.
- Prefer clear naming.
- Avoid unnecessary abstractions.
- Reuse existing utilities and helpers.

## Output format

When proposing changes:
- list affected files
- show minimal code modifications
- explain reasoning briefly