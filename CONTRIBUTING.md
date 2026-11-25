# Contributing

Thanks for helping. Small, focused pull requests are easiest to review.

## Getting started

You need the .NET 10 SDK (10.0.400 or later) on Windows.

```powershell
dotnet build
dotnet format --verify-no-changes
```

Both must pass; warnings are treated as errors. CI runs the same commands.
Run `dotnet format` to fix formatting.

## Guidelines

- **Keep secrets secret.** Session IDs and tokens must never reach a log, an
  exception message, the screen, or a file other than the vault and the Jagex
  Launcher's own sign-in file. The one exception is **Copy session details**,
  which puts a session on the clipboard when the user asks for it.
- **Core stays UI-free.** Logic that doesn't need a window belongs in
  `JagexManager.Core`. The WPF project should only show state and forward
  clicks.
- **No comments in the code.** Say it with names and small functions.
- **No new dependencies without a reason.** The app ships as one file and
  handles credentials; every dependency is something users must trust.
- **Keep memory low.** Releases are published uncompressed, because a
  compressed single-file exe unpacks every library into memory at start-up, and
  the window renders in software. Both keep the app near 40 MB. Measure memory
  before changing either.
- **Nothing that helps break Jagex's rules.** Automation of gameplay, ban
  evasion and credential sharing are out of scope.

## Naming

- Short, direct names from the domain: `Vault`, `Login`, `Client`, `Roster`.
- Methods are normally one to three words and say what happens: `Play`, `Stop`,
  `SetAside`, `DropClosed`. Async methods keep the `Async` suffix.
- Avoid vague verbs (`Handle`, `Process`, `Manage`, `Execute`) when a direct one
  exists, and filler suffixes (`Manager`, `Handler`, `Service`, `Helper`) unless
  the type really is one.
- Do not repeat the namespace in a name; clarity beats brevity.
- One word per idea. A `Program` is a client that can be started, and a member
  named `Program` or `Programs` always holds those records; a `Client` is one
  that is running. A client's name is a `Choice` on a character and the
  `Default` in settings. `Custom` holds the clients the user added. Options are
  what the user typed; arguments are the whole command line. A character's
  `Preference` holds its `Choice`, its `Nickname`, its own `Options` and whether
  it is `Hidden` from the list; a window that starts out of sight is
  `Minimised`. `Tokens` is the token endpoint's reply; `Credentials` is the set
  kept for the Jagex Launcher.

## How it works

1. **Login.** An OAuth authorization-code sign-in with PKCE for the launcher
   client. The embedded browser is watched for the redirect that carries the
   code, which is exchanged for tokens.
2. **Consent.** A second request for the game client returns an ID token.
3. **Session.** The ID token is exchanged for a game session ID, and the
   session's characters are listed.
4. **Play.** The client is started with `JX_SESSION_ID`, `JX_CHARACTER_ID` and
   `JX_DISPLAY_NAME` set, as the official launcher does.
5. **Jagex Launcher.** The launcher keeps its sign-in in
   `%LOCALAPPDATA%\Jagex Launcher\auth\creds`, encrypted with AES-GCM under a
   key in Windows Credential Manager ("Jagex Safe Storage"). JagexManager closes
   the launcher, writes the chosen account's tokens, and only then saves the
   tokens the launcher was holding to the account they belong to. The launcher
   replaces its refresh token each time it uses it, so JagexManager removes
   tokens from its vault when it hands them over, and never refreshes tokens
   itself.

## Layout

| Path | Contents |
|---|---|
| `src/JagexManager.Core` | Sign-in flow, Jagex endpoints, encrypted vault, client launch, the Jagex Launcher's sign-in file, the account list the window binds to, the GitHub release check and starting with Windows. No UI framework. |
| `src/JagexManager` | The WPF windows, the embedded sign-in browser, the tray icon, the RuneLite colours and the bundled fonts. |

## Changes to the sign-in flow

Jagex's endpoints are undocumented. When you change `Auth`, `Login` or `Jagex`,
say in the pull request how you confirmed the behaviour (for example, which
reference launcher does the same), and whether you tested with a real account.
Never paste real tokens, session IDs or character IDs into an issue or PR.

## Reporting security issues

Report security problems through
[GitHub's private vulnerability reporting](../../security/advisories/new), not a
public issue, and never include real tokens or account details.
