# Contributing

Thanks for taking the time. This project is small on purpose: a transport adapter, nothing more.

## Commits

Releases are cut by [versionize](https://github.com/versionize/versionize) from the commit history,
so commit messages follow [Conventional Commits](https://www.conventionalcommits.org/):

- `feat: ...` for a new capability (minor bump)
- `fix: ...` for a bug fix (patch bump)
- `feat!: ...` or a `BREAKING CHANGE:` footer for anything that breaks callers (major bump)
- `docs:`, `test:`, `chore:`, `refactor:` for everything that does not change the package's behaviour

Scope is optional (`feat(sessions): ...`). The subject is what ends up in `CHANGELOG.md`, so write it
for someone reading the release notes.

## Pull requests

1. Fork and branch from `main`.
2. Keep the change focused; a transport should not grow opinions about applications.
3. Add or update tests in `tests/`. The tests drive the server through the official
   `ModelContextProtocol` client over a real Watson listener on a loopback port, so a change that
   passes them works against a real MCP client.
4. `dotnet build` with zero warnings (`TreatWarningsAsErrors` is on) and `dotnet test` green.
5. Open the pull request against `main`. The CI builds and tests on every pull request.

## Releasing (maintainers)

Actions → **Bump Version** → Run workflow. versionize computes the next version from the commits,
updates `CHANGELOG.md` and the `.csproj`, commits and pushes a `vX.Y.Z` tag; the tag triggers
**Publish to NuGet**, which packs, pushes to nuget.org and creates the GitHub release with the
changelog section as notes.
