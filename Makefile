.PHONY: test publish

test:
	dotnet run --project tests/CursorBar.Core.Check/CursorBar.Core.Check.csproj -c Release

publish:
	pwsh -File scripts/package.ps1
