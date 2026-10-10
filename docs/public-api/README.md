# Public API

One file per package: every public type and member, one per line, in the format of the .NET public API analyzer
(`!` marks a non-nullable reference, `?` a nullable one). It is a reference, not a gate: the build doesn't check it.

Regenerate after changing a package's public API, and review the diff:

```powershell
pwsh tools/Update-PublicApiDocs.ps1
```
