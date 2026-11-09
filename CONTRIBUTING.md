# Contributing to LockscreenGif

Thank you for your interest in contributing to **LockscreenGif**!

## Code Style & Formatting

The repository enforces C# code style using [CSharpier](https://csharpier.com/) and `.editorconfig` rules.
Before submitting changes, format your code by running:

```powershell
dotnet tool restore
./scripts/Format-CSharp.ps1
```

To verify code formatting without modifying files:
```powershell
./scripts/Format-CSharp.ps1 -Check
```

## Running Tests

Ensure all automated regression tests pass before creating a pull request:

```powershell
Get-ChildItem Tests -Recurse -Filter *.csproj | ForEach-Object {
    dotnet run --project $_.FullName -c Release
}
```

## Pull Request Guidelines

1. **One Concern Per Pull Request**: Keep changes focused, concise, and reviewable.
2. **Preserve Contracts**: Do not modify privileged elevation contracts or installer packaging layouts without prior discussion.
3. **Attribution & Licensing**: Respect the project's [MIT License](LICENSE.txt) and [Third-Party Notices](THIRD-PARTY-NOTICES.md).

