# Contributing to DTDE

Thanks for taking the time to contribute to **DTDE (Distributed Temporal Data Engine)**.

## Quick start

### Prerequisites

- .NET SDK 9.x
- A GitHub account

### Build

```bash
# From the repository root
dotnet build ./Dtde.sln -c Release
```

### Test

```bash
# From the repository root
dotnet test ./Dtde.sln -c Release
```

## Ways to contribute

- Bug reports and repro cases
- Documentation improvements (`/docs`)
- Fixes and features
- Performance improvements and benchmarks (`/benchmarks`)

## Development workflow

1. Create an issue (or comment on an existing one) describing the change.
2. Create a branch from `main`.
3. Make changes.
4. Ensure `dotnet build` and `dotnet test` succeed.
5. Open a Pull Request.

## Branch naming

Use lowercase with hyphens:

- `feature/<issue-number>-<brief-description>`
- `bugfix/<issue-number>-<brief-description>`
- `docs/<brief-description>`

Examples:

- `feature/123-temporal-predicate-optimization`
- `bugfix/456-null-validto-handling`

## Commit messages

Follow this format:

```
<type>(<scope>): <subject>
```

Common `type` values: `feat`, `fix`, `docs`, `refactor`, `test`, `build`, `ci`, `chore`.

Examples:

- `fix(core): handle open-ended validity ranges`
- `docs(guides): clarify sharding setup`

## Coding standards

- Follow the repository `.editorconfig`.
- Treat warnings as errors (the repo is configured this way).
- Prefer clear, maintainable code and tests.

## Documentation

- Project docs live under `/docs`.
- Prefer updating existing documents over creating new ones.

## Pull request checklist

- Builds and tests pass (`dotnet build`, `dotnet test`).
- Public API changes are documented (and ideally include tests).
- Documentation is updated where relevant (`/docs`).
- `CHANGELOG.md` is updated for user-visible changes.

## Security issues

Please do **not** open public issues for security vulnerabilities. See `SECURITY.md`.

## License

By contributing, you agree that your contributions are licensed under the MIT license (see `LICENSE`).
