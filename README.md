# Terragit

Terragit brings Git-inspired version control to Terraria worlds running on tModLoader.
It stores world snapshots as commits, keeps a history for each world, and lets you
restore a previous snapshot from the in-game chat.

## Features

- One repository per Terraria world.
- Commit world snapshots with a message.
- Branch-aware commit history.
- Push and pull commands backed by local save data.
- A simple world diff based on file size and SHA-256 hashes.
- Supports both `.wld` and `.twld` world files.

Terragit stores its data in the tModLoader save directory:

```text
<tModLoader SavePath>/Terragit/
├── database.json
├── commits/
└── remote/
```

The `remote` directory is currently a local branch reference. Network remotes are
not implemented yet.

## Installation

### From the Steam Workshop

1. Subscribe to Terragit on the Steam Workshop.
2. Start tModLoader.
3. Open **Workshop > Manage Mods**.
4. Enable Terragit and reload mods.

### From source

1. Install tModLoader and make sure its mod development tools are available.
2. Copy or clone this project into the tModLoader `ModSources` directory.
3. Open tModLoader and select **Workshop > Develop Mods**.
4. Build Terragit, then enable it from the Mods menu.

The project expects the standard tModLoader layout, with `tModLoader.targets`
available one directory above the project, as in the normal `ModSources` setup.

## Quick start

Load or create a world, then run these commands in chat:

```text
/tg init
/tg add
/tg commit "initial snapshot"
/tg push
```

After making more changes:

```text
/tg add
/tg commit "built the new house"
/tg push
```

To restore the most recently pushed snapshot:

```text
/tg pull
```

The world must be saved before staging it. Pulling replaces the current world file,
so make a backup first if the current changes matter.

## Commands

| Command | Description |
| --- | --- |
| `/tg init` | Creates a repository for the current world. Repairs older repositories that do not have an initial branch. |
| `/tg add` | Reads the current `.wld` or `.twld` file into the staging area. |
| `/tg commit "message"` | Saves the staged world as a commit on the current branch. |
| `/tg push` | Publishes the current branch head to Terragit's local `remote` reference. |
| `/tg pull` | Restores the pushed snapshot for the current branch. |
| `/tg pull <branch>` | Restores the pushed/latest snapshot from a named branch. |
| `/tg diff` | Compares the current world file with the latest local commit. |
| `/tg diff <branch>` | Compares the current world file with a named branch's latest commit. |

## Testing checklist

Use a disposable test world or back up your world before testing:

1. Enable Terragit and load the test world.
2. Run `/tg init`. Confirm that a repository-created message appears.
3. Make a small, visible change in the world.
4. Save the world, then run `/tg add`.
5. Run `/tg commit "test snapshot"`.
6. Run `/tg diff`. The current world should match the commit if nothing changed afterward.
7. Run `/tg push`.
8. Make another change and save it.
9. Run `/tg pull`. Confirm that the world file is restored to the pushed snapshot.
10. Restart tModLoader, reload the same world, and repeat `/tg diff` to verify that the repository persists.

If a command reports that no repository or commit exists, run the commands in order:
`init`, `add`, `commit`, then `push`.

## Building and publishing

For local development, build the project from the tModLoader `ModSources` location:

```bash
dotnet build Terragit.csproj
```

In tModLoader, use **Workshop > Develop Mods** to build and package the mod. To
publish it, use the Workshop publishing flow, provide the mod name, description,
icon, and version metadata, then test the published copy from a clean client.

Before publishing, verify that:

- The mod builds without errors.
- The mod loads with a fresh world.
- Existing worlds are not overwritten unexpectedly.
- Commit, push, pull, and diff work after restarting tModLoader.
- The Workshop description matches the current local-only push/pull behavior.

## Development notes

Terragit is an early project. Commits are complete world-file snapshots rather
than tile-level diffs, and push/pull currently do not communicate with a server
or external Git host. Network synchronization can be added later without changing
the basic in-game workflow.

## License

See [LICENSE](LICENSE).
