# Agent Notes

## Packaging

- `src/dotnet-stop.csproj` targets `net10.0` only.
- Hybrid Native AOT/CoreCLR tool packaging is opt-in via `HybridToolPackage=true`.
- Keep `.github/workflows/build.yml` on the default non-AOT package path for fast CI and dogfooding.
- In `.github/workflows/publish.yml`, publish RID-specific packages and the `any` fallback before publishing the top-level pointer package.
