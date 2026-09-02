# IQOne.Zero.Tool

The `zero` command. Moves what ships inside the Zero packages — the capability catalogue and
the rule files — into your repository, where coding agents read them.

```bash
dotnet tool install --global IQOne.Zero.Tool
zero rules init
```

| | |
| --- | --- |
| `zero rules init` | writes `AGENTS.md`, `.zero/rules/`, `.cursor/rules/` and `CLAUDE.md` |
| `zero rules check` | exits non-zero when those files no longer match the restored packages |
| `zero capabilities` | what is installed, and what Zero offers that is not |

Both the catalogue and the rules travel inside the packages, so they cannot drift from the
code they describe: upgrade to 1.2 and the rules an agent reads are 1.2's. `zero rules check`
is the CI gate for the other half — an upgrade nobody re-ran leaves an agent reading last
release's rules, and the file looks current because somebody committed it on purpose.

`AGENTS.md` holds the catalogue and an index rather than every rule in full: it is loaded at
the start of every agent session, and an agent cannot look up a capability it does not know
exists, but it can read a rule when it reaches that area.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
