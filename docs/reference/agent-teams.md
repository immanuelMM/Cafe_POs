# Claude Code — Agent Teams (reference)

Source: https://code.claude.com/docs/en/agent-teams

Saved as a local reference for configuring and using agent teams in this
project (the `research-planner`, `frontend-react-dev`, `backend-dotnet-dev`,
`qa-automation`, and `docs-writer` subagents in `.claude/agents/` can be
spawned as teammates once agent teams are enabled).

---

## What agent teams are

Agent teams coordinate multiple Claude Code instances working together. One
session acts as the **team lead**, coordinating work, assigning tasks, and
synthesizing results. **Teammates** work independently, each in its own
context window, and communicate directly with each other. You can also talk
to any teammate directly without going through the lead.

Agent teams are **experimental and disabled by default**. Enable them with:

```json
// settings.json
{
  "env": {
    "CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS": "1"
  }
}
```

This project already has that set in `.claude/settings.json`.

Known limitations exist around session resumption, task coordination, and
shutdown behavior (see [Limitations](#limitations) below).

## When to use agent teams vs. subagents

Before setting up a team, check if a lighter option works:
- **Subagents** work within a single session, report a result back to the
  caller, and are cheaper (summarized context, lower token cost).
- **Agent teams** are for complex work requiring discussion, parallel
  exploration, and self-coordination between teammates.

Strongest use cases for teams:
- **Research and review** — teammates investigate different aspects, then
  share/challenge findings.
- **New modules or features** — each teammate owns a separate piece.
- **Debugging with competing hypotheses** — teammates test different
  theories in parallel and converge faster.
- **Cross-layer coordination** — e.g. frontend + backend + tests changes,
  each owned by a different teammate (maps directly to this project's
  `frontend-react-dev` / `backend-dotnet-dev` / `qa-automation` agents).

| | Subagents | Agent teams |
|---|---|---|
| Context | Own window; results return to caller | Own window; fully independent |
| Communication | Returns a result; named subagents can message each other | Teammates message each other directly |
| Coordination | Main agent manages all work | Self-coordination via messages + shared task list |
| Best for | Focused tasks where only the result matters | Complex work requiring discussion/collaboration |
| Token cost | Lower (summarized back) | Higher (each teammate is a full session) |

Teams add coordination overhead and use significantly more tokens. They work
best when teammates can operate **independently**. For sequential work,
same-file edits, or heavily-dependent steps, prefer a single session or
subagents.

## Starting a team

Enable the env var, then just describe the task and teammates you want in
natural language, e.g.:

```
Spawn a frontend-react-dev teammate and a backend-dotnet-dev teammate to
build the menu browsing feature in parallel — frontend owns the React
components, backend owns the /api/menu endpoint and EF Core model. Have
qa-automation join once both are far enough along to write tests against it.
```

Claude populates a shared task list, spawns teammates, has them work, and
synthesizes findings when done. The agent panel (below the prompt input)
lists teammates — arrow keys to select, Enter to open a teammate's
transcript and message it directly, Escape to clear/interrupt.

### Using this project's subagent definitions as teammates

Reference a subagent type by name when asking Claude to spawn a teammate —
this reuses the role/system-prompt/tools already defined for it:

```
Spawn a teammate using the research-planner agent type to investigate
current POS UI patterns while a frontend-react-dev teammate starts
scaffolding the component shell.
```

What's applied from the definition: `tools` (plus `SendMessage` and the
Task tools), `model` (if your prompt doesn't name one), `disallowedTools`,
`effort`, `autoCompactWindow`, and the body (appended to the default system
prompt for in-process teammates, used in place of it for split-pane
teammates). `skills` and `mcpServers` are NOT applied the same way — the
teammate loads skills/MCP servers from project/user settings instead (with
some split-pane-specific exceptions for `mcpServers`).

## Display modes

- **In-process** (default): all teammates run inside the main terminal;
  arrow keys + Enter to view/message a teammate. Works everywhere.
- **Split panes**: each teammate gets its own pane (requires tmux or
  iTerm2 + the `it2` CLI). Set via `teammateMode` in `~/.claude/settings.json`
  (`"auto"`, `"tmux"`, `"iterm2"`, or `"in-process"`) or per-session with
  `claude --teammate-mode auto`.

## Specifying teammates and models

You can tell Claude exactly how many teammates and which model to use:

```
Spawn 4 teammates to refactor these modules in parallel. Use Sonnet for
each teammate.
```

Model resolution order: prompt-named model → subagent definition's `model`
→ `CLAUDE_CODE_SUBAGENT_MODEL` env var → lead's current model (subject to
org `availableModels` allowlist substitution rules).

## Planning before implementing

If the lead is in plan mode and you ask for a teammate, that teammate works
in read-only plan mode until its plan is ready, then sends a plan approval
request — Claude Code auto-approves it in the lead's session (no separate
prompt), and the teammate proceeds to implementation.

## Tasks

The shared task list coordinates work: pending → in progress → completed,
with optional dependencies (a task can't be claimed until its dependencies
complete). The lead can assign tasks explicitly, or teammates self-claim the
next unblocked task after finishing their own. Agents without the Task tools
coordinate via messages instead.

## Shutting down a teammate

```
Ask the <name> teammate to shut down
```
The lead sends a shutdown request; the teammate can approve (exits
gracefully) or reject with an explanation. Team directories clean up
automatically when the session ends.

## Quality gates via hooks

- `TeammateIdle` — runs when a teammate is about to go idle; exit code 2 to
  send feedback and keep it working.
- `TaskCreated` — runs on task creation; exit code 2 to block + feedback.
- `TaskCompleted` — runs on task completion; exit code 2 to block + feedback.

Useful for this project, e.g. a `TaskCompleted` hook on the
`backend-dotnet-dev` teammate's tasks that runs `dotnet build`/`dotnet test`
before allowing a task to be marked done.

## Architecture (for reference)

- **Team lead**: main session, spawns teammates, coordinates.
- **Teammates**: separate Claude Code instances per assigned task(s).
- **Task list**: shared work items teammates claim/complete.
- **Mailbox**: per-agent JSON file at
  `~/.claude/teams/{team-name}/inboxes/{agent-name}.json` for messaging.

Team name = `session-` + first 8 chars of the session ID.
- Team config: `~/.claude/teams/{team-name}/config.json` (removed when
  session ends — don't hand-edit, it's overwritten on every state update).
- Task list: `~/.claude/tasks/{team-name}/` (persists locally, governed by
  `cleanupPeriodDays`).

There is **no project-level team config** — a file like
`.claude/teams/teams.json` is not recognized as configuration.

## Permissions

Teammates start with the lead's permission mode (except `dontAsk`, which
isn't inherited). If the lead runs with `--dangerously-skip-permissions`,
so do all teammates. Permission prompts from teammates bubble up to the
lead — pre-approve common operations in permission settings to reduce
interruptions when running a team for this project (e.g. `dotnet build`,
`npm run build`, `npm test`).

A teammate can't approve its own permission prompt via another teammate's
relayed message, and the auto-mode classifier treats relayed "approval"
claims as untrusted input, not consent from you.

## Context and communication

- Each teammate loads CLAUDE.md, MCP servers, and skills like a normal
  session, but does **not** inherit the lead's conversation history — give
  it full task-specific context in the spawn prompt.
- Messages are delivered automatically; the lead doesn't poll.
- A teammate notifies the lead automatically when it finishes (or fails).

## Token usage

Agent teams cost significantly more tokens than a single session — each
teammate is a full independent context window. Worth it for research/review/
parallel feature work; not for routine single-threaded tasks.

## Best practices

- **Give full context in the spawn prompt** — teammates don't see prior
  conversation.
- **Team size**: start with 3-5 teammates (this project's 5 agents map well
  onto this). More teammates ⇒ more coordination overhead and diminishing
  returns.
- **Size tasks right**: self-contained units with a clear deliverable (a
  component, an endpoint, a test file) — not too small (overhead dominates)
  or too large (teammates drift too long between check-ins).
- **Wait for teammates to finish** rather than letting the lead jump in and
  implement itself.
- **Start with research/review tasks** before parallel implementation, to
  learn the coordination model with lower risk.
- **Avoid file conflicts** — give each teammate its own set of files/area
  (this is naturally satisfied by the frontend/backend/QA/docs split).
- **Monitor and steer** — check in, redirect, and synthesize as teammates
  report back; don't let a team run unattended too long.

## Troubleshooting (common cases)

- **Teammates not appearing**: check the agent panel; confirm the task was
  complex enough that Claude chose to spawn a team (ask explicitly for an
  "agent team" if it spawns plain subagents instead).
- **Claude spawns teammates instead of subagents**: expected once agent
  teams are enabled — any named subagent becomes a teammate. To stop this,
  set `CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS` to `"0"` (project/local
  settings or managed settings can still override back to enabled).
- **Too many permission prompts**: pre-approve common Bash/tool patterns.
- **Agents stopping early**: open the teammate's transcript, give it more
  instructions, or spawn a replacement.
- **Orphaned tmux sessions**: `tmux ls` then `tmux kill-session -t <name>`.

## Limitations

- No session resumption with in-process teammates (`/resume`/`/rewind`
  don't restore them — re-spawn after resuming).
- Task status can lag (teammates sometimes fail to mark tasks complete).
- Shutdown can be slow (teammates finish their current tool call first).
- One team per session; no nested teams (teammates can't spawn teammates).
- No background subagents from in-process teammates.
- Lead role is fixed for the session's lifetime.
- Per-teammate permission mode can only be changed after spawn, not at
  spawn time.
- Split panes require tmux or iTerm2 (not supported in VS Code's integrated
  terminal, Windows Terminal, or Ghostty — relevant since this project is
  being developed on Windows; in-process mode is the practical default
  here unless tmux is installed, e.g. via WSL or Git Bash).

## How this applies to this project's agents

This project defines five subagents in `.claude/agents/`:
`research-planner`, `frontend-react-dev`, `backend-dotnet-dev`,
`qa-automation`, `docs-writer`. With agent teams enabled, any of these can
be spawned as a **teammate** (not just a one-shot subagent) by naming the
agent type in a spawn request, e.g.:

```
Spawn teammates using the frontend-react-dev and backend-dotnet-dev agent
types to build the order-checkout feature in parallel, each owning their
layer. Spawn a qa-automation teammate once both report their first working
version, to write tests against the real endpoints/components.
```

Good team shape for this project given the "cross-layer coordination" use
case above:
- `research-planner` teammate (or run it first as a one-shot subagent,
  before a team even starts, since planning is inherently sequential/
  upstream of everything else).
- `frontend-react-dev` teammate — owns `src/` (or wherever the React app
  lives).
- `backend-dotnet-dev` teammate — owns the .NET 8 API project directory.
- `qa-automation` teammate — owns test directories/files only, starts once
  there's something real to test against.
- `docs-writer` — best run after the above settle, since docs should
  describe verified, implemented behavior rather than work in flight.
