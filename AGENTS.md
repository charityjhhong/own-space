# Notes for coding agents

Four agents work in this repo at the same time, often while their humans are away. Each agent has one ID, one human, and a fixed set of paths it may write. the team plan is the source of truth for ownership, phases and deadlines.

| Agent ID | Human | May write (everything else is read-only) |
| --- | --- | --- |
| `core` | the human | `app/Assets/_Project/Core/`, `app/Assets/_Project/Contracts/`, `app/Assets/_Project/Tests/`, `proxy/`, root docs (`README.md`, the team plan, `CONCEPT.md`, `AGENTS.md`, `.gitattributes`, `.gitignore`) |
| `xr` | the human | `app/Assets/_Project/Interaction/`, `MR/`, `UI/`, `Scenes/Main.unity`, `app/ProjectSettings/`, `app/Packages/`, `app/Assets/Plugins/Android/`, `app/Assets/TextMesh Pro/`, `app/Assets/Oculus/` |
| `build` | the human | `.github/`, `app/Assets/_Project/Editor/Build/`, `app/Assets/_Project/Diagnostics/`, `docs/release/`, `docs/qa/` |
| `content` | the human | `app/Assets/_Project/Art/`, `Audio/`, `Content/`, `docs/design/`, `docs/demo/`, `submission/` |

**One human, the human, currently runs all four agents.** Ownership by path still applies: each agent writes only its own paths.

Every agent may also write its own sandbox scene, `app/Assets/_Project/Scenes/Sandbox_<id>.unity`.

## Every session

1. **Start:**
   - Read the newest comments on **Agent sync (#1)** and do what is addressed to you or to `all agents`.
   - Read today's **Daily log** issue.
   - Pull `main`.
2. **Work** only in your paths, on a branch named `<id>/p<phase>-<slice>`, for one issue.
3. **Verify** before pushing. Run a batch-mode compile and the EditMode tests, and report the result honestly in the PR. A Fake or the XR Simulator never counts as headset acceptance; label anything that needs a real Quest `needs-headset`.
4. **End:** post your 4-line entry on today's Daily log issue.

## Boundaries

- **Never edit a file outside your paths.** CI rejects it.
  - If you need a change there, post a `request` on #1 addressed to the owner.
  - Until the owner makes it, work around it inside your own paths with a Fake or a feature flag.
- Never push to another agent's branch. Review comments only.
- Never force-push. Never merge to `main`; humans merge.
- Interfaces in `Contracts/` change only in a `core` PR. That PR must include the interface, its Fake, and a `contract-change` comment on #1.
- To get something into `Main.unity`:
  - Make a root prefab in your own folder.
  - Ask `xr` to place it once.
  - After that, edit only your prefab.
- **No Unity Editor clicking.** There is no Unity MCP connection, and the human's time is short. Anything that would normally be done by hand in the Editor (creating objects or prefabs, wiring components, changing settings, placing things in a scene) is done by an **editor script** in an `Editor/` subfolder inside one of your own folders (for example `MR/Editor/`), with an editor-only assembly definition: a static method you run with `-executeMethod` in batch mode in your worktree, and that is safe to run twice. Commit the script and its result together. Ask the human only to open Unity, press Play and check something by eye; say exactly what to look at.
- Commit every Unity asset together with its `.meta`. Never commit build outputs, keystores, passwords, tokens or `Library/`.
- Everything you write is in English. `ko/` holds Korean reading copies for the humans. It is gitignored: don't read it for instructions, and never commit it.

## Quality bar

We build to a ship-ready standard. The full budget is "Quality bar" in the team plan. The rules that matter most while coding:

- **Frame rate:** 72 Hz on Quest 3/3S in the worst scene.
- **No per-frame garbage.** Never allocate in `Update`: no `new`, no LINQ, no string building, no `Find*`.
- **Compiler warnings:** zero. CI treats them as errors.
- **Hands only.** There is no vibration with hand tracking, so feedback is visual and audio.
- **Losing hand tracking** cancels an in-progress action safely.
- **No room scan, no table, pausing, recentering:** each of these has a working path.
- **Core changes come with EditMode tests.** A bug fix starts with a test that reproduces it.
- **Every PR meets the "Definition of done" checklist** in the team plan.

## Size and timing

- One PR = one issue, at most **400 changed lines of code** (assets excluded).
- Stack at most 2 PRs deep.
- Merge small working pieces every 60–90 minutes.
- No new-feature PRs within 24 hours of the feature freeze (see the team plan).
- PR body: what changed, how to run it, expected vs. actual result, known failures.

## Writing on #1 and the Daily log

Keep it short. CalHacks 13's sync issue reached 262 comments and about 440,000 characters, and people stopped reading it. **Ten lines at most per comment.** Put long analysis in the PR or `docs/` and link it.

Agent sync (#1):

```
From: <id> for @<human> → To: <id | all agents | @human>
Type: request | answer | blocked | contract-change | heads-up
- fact (branch / PR / commit)
Need: <one line, or none>
```

Daily log (one issue per day, titled `Daily log MM-DD`):

```
[<id>] <start–end time>
Done: <merged or opened, with links>
Next: <next session>
Blocked: <or none>
```

## Ask your human

Ask only for decisions that are theirs:
- scope cuts
- what lands before the freeze
- merging
- deleting someone's work
- anything needing a login, password or the headset

Tag them with their @handle. Everything else, decide and note it in the PR.
