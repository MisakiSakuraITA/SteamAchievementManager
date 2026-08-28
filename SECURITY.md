# Security Policy

## Supported Versions

Security fixes are made against the current `7.1.x` line only. Earlier releases are no
longer maintained and will not receive backported fixes; please upgrade before reporting
an issue against them.

| Version | Supported          |
| ------- | ------------------ |
| 7.1.x   | :white_check_mark: |
| 7.0.x   | :x:                |
| < 7.0   | :x:                |

## Reporting a Vulnerability

Please **do not** open a public issue for a suspected security vulnerability. Report it
privately through GitHub's Security Advisories instead:

1. Go to the [Security tab](../../security) of this repository.
2. Select **Report a vulnerability** to open a new private advisory.
3. Describe the issue: affected version, a reproduction case if you have one, and its
   impact (e.g. what a successful exploit would let an attacker do).

You can also start a draft advisory directly at
[github.com/MisakiSakuraITA/SteamAchievementManager/security/advisories/new](https://github.com/MisakiSakuraITA/SteamAchievementManager/security/advisories/new).

A private advisory is visible only to the maintainers and whoever you invite into it,
which gives everyone room to work out a fix before any detail becomes public. There is no
fixed SLA, but reports are triaged as they arrive; expect an initial response acknowledging
the report, followed by either a request for more detail or a plan for a fix.

## Scope

SAM is a local desktop tool that talks to a Steam client already running on the same
machine, over Steam's own local IPC pipe (`steamclient.dll`). Reports concerning that
local interaction — for example, an untrusted schema, cache file, or IPC payload causing
memory corruption or a crash inside `SAM.API` or `SAM.Core` — are in scope. General
feature requests, and issues with Steam itself or with games' own achievement data, are
not; please file those as a regular issue instead.
