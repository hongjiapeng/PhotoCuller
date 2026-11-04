# Contributing to PhotoCuller

Bug reports, documentation improvements, and pull requests are welcome.
You can communicate in English or Chinese.

## Development

See [the README](README.md#getting-started) or
[中文说明](README.zh-CN.md) for Windows, .NET SDK, and Visual Studio requirements
and build instructions. The repository uses public NuGet packages only.

Fork the repository and create a branch from `main`. Keep each pull request
focused on one change and describe the problem, resulting behavior, and how
you verified it. For behavior changes, add or update relevant tests.

Run the existing test suite from the repository root:

```powershell
dotnet test src/PhotoCuller.Tests/PhotoCuller.Tests.csproj --configuration Release
```

WinUI changes also need a Visual Studio MSBuild build and a manual UI check.
Core and Application should remain independent of the Windows UI; WinUI and
the CLI share the Application analysis engine.

## Reporting bugs

Open an [issue](https://github.com/hongjiapeng/PhotoCuller/issues) with the
steps to reproduce, expected and actual behavior, Windows version, and commit
or release version. Include relevant logs with personal paths, API keys,
and other private information removed. Use synthetic or shareable images
when a photo is needed to reproduce a problem.

## Files and licensing

Keep generated output, local agent settings, databases, caches, credentials,
and model weights out of commits. Check `git diff --cached` before committing.
Model weights are not included in this repository.

Contributions to this project's original code are provided under its
[MIT License](LICENSE). Third-party libraries, model weights, and datasets
retain their own licenses and terms.
