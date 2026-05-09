# `.claude/` — project-scoped Claude Code configuration

Everything in this directory is committed and shared with the team. It tells
[Claude Code](https://claude.com/claude-code) how to behave inside this
repository.

## Contents

| Path | Purpose |
|---|---|
| `settings.json` | Permissions allowlist + denylist. Shared with the team. |
| `agents/` | Project-scoped subagents (`dotnet-library-reviewer`, `nuget-packager`). |
| `commands/` | Project-scoped slash commands (`/dtde-build`, `/dtde-pack`, `/dtde-bench`, `/dtde-verify-build`). |
| `skills/` | Project-scoped skills (e.g. `add-sharding-strategy`). |

The root [`CLAUDE.md`](../CLAUDE.md) carries the project's primary
documentation for Claude. Keep it in sync with reality — it loads into every
session.

The repo's [`.mcp.json`](../.mcp.json) declares project-scoped MCP servers.
Today that's the **Microsoft Learn** docs server only; team members can opt
into more via `/mcp` or `~/.claude/settings.json`.

## What's NOT committed

The repository's [`.gitignore`](../.gitignore) excludes:

- `.claude/settings.local.json` — per-developer overrides.
- `.claude/.credentials.json` — never share.
- `.claude/worktrees/`, `.claude/cache/`, `.claude/sessions/`,
  `.claude/logs/` — transient runtime data.

If you want a personal CLAUDE override (preferences that aren't
project-scoped), use `~/.claude/CLAUDE.md` (user scope) or
`./CLAUDE.local.md` (project scope, gitignored — but **not** here, it's
gitignored at the user level only).

## Adding things

- **A new slash command:** drop `commands/<name>.md` with frontmatter
  (`description`, `allowed-tools`, optional `argument-hint`). Slash command
  bodies are normal markdown; `$ARGUMENTS` interpolates the user input.
- **A new subagent:** drop `agents/<name>.md` with frontmatter (`name`,
  `description`, `tools`, `model`). Use the existing two as templates.
- **A new skill:** create `skills/<name>/SKILL.md`. Skills auto-load when
  their description matches the user's intent.

## Permission updates

Permissions live in `settings.json`. Keep `allow:` tight — the goal is
"agent never has to ask twice for routine read-only tooling, but cannot do
anything destructive without confirmation". Run-of-the-mill local commands
(`dotnet build`, `git status`, `gh pr view`) are pre-approved; anything that
mutates remote state (`git push --force`, `dotnet nuget push`,
`gh release create`) is denied or unconfigured (so it always prompts).
