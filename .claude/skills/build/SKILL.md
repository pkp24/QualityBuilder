---
name: build
description: Build the QualityBuilder mod (Release) and deploy the DLL to Versions/v1.6/Assemblies/. Use when asked to build, compile, or deploy the mod after code changes.
disable-model-invocation: true
---

Build the mod and deploy the compiled DLL to the active RimWorld 1.6 version folder.

1. Compile Release:
   ```
   dotnet build _PROJECT/QualityBuilder.csproj -c Release
   ```
   This targets .NET Framework 4.7.2 and writes `TargetAssemblies/QualityBuilder.dll`.

2. If the build fails, report the compiler errors and stop — do not copy a stale DLL.

3. On success, copy the built DLL into the 1.6 load folder:
   ```
   cp TargetAssemblies/QualityBuilder.dll Versions/v1.6/Assemblies/QualityBuilder.dll
   ```
   (`LoadFolders.xml` maps RimWorld 1.6 to `Versions/v1.6`.)

4. Confirm the copy succeeded and report the deployed path.

Notes:
- The `.csproj` references RimWorld/Harmony DLLs by absolute path in the local Steam install; if the build can't find them, the install moved — report that rather than guessing.
- To deploy to a different supported version, copy into the matching `Versions/v1.x/Assemblies/` folder instead (see `LoadFolders.xml`).
- Never copy `0Harmony.dll` into the mod — Harmony is supplied by the official Harmony mod (`brrainz.harmony`, declared in `About.xml`) at runtime.
