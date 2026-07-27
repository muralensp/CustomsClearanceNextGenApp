# Day 12 Execution Log — Code Scanning & Coverage Gates

Phase -1 (Architecture & Engineering Preparation), Day 12 of 20. This is a step-by-step transcript of what was actually run and what each step produced, exported from the working session per the user's request — companion to `Day12_Code_Scanning_Coverage_Gates.docx` (the narrative deliverable) and `adr/0007-static-analysis-and-coverage-gates.md` (the decision record).

---

## Step 1 — Inspect the existing test project

**Command:**
```bash
cat tests/Unit/Platform.Common.Tests/CustomsClearance.Platform.Common.Tests.csproj
```

**Output:** confirmed `coverlet.collector` v6.0.0 was already present (from the `dotnet new xunit` template used on Day 11), but no coverage collection or thresholds were wired into the pipeline yet.

---

## Step 2 — Verify basic coverage collection works

**Command:**
```bash
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
```

**Output:**
```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
Attachments:
  C:\Murale\Projects\CustomsCleranceNextGenApp\TestResults\19b197ce-...\coverage.cobertura.xml
```
Confirmed coverage collection produces a cobertura file via the collector-based approach.

---

## Step 3 — Add ReportGenerator as a local dotnet tool

**Commands:**
```bash
dotnet new tool-manifest
dotnet tool install dotnet-reportgenerator-globaltool
```

**Output:**
```
The template "Dotnet local tool manifest file" was created successfully.
Tool 'dotnet-reportgenerator-globaltool' (version '5.5.10') was successfully installed.
Entry is added to the manifest file .config/dotnet-tools.json.
```

---

## Step 4 — Add coverlet.msbuild for threshold enforcement

**Change:** added `<PackageReference Include="coverlet.msbuild" Version="6.0.0" />` alongside the existing `coverlet.collector` in the test project.

**Verification command:**
```bash
dotnet test tests/Unit/Platform.Common.Tests/CustomsClearance.Platform.Common.Tests.csproj \
  -p:CollectCoverage=true -p:Threshold=80 -p:ThresholdType=line -p:ThresholdStat=total \
  -p:CoverletOutputFormat=cobertura -p:CoverletOutput=./TestResults/coverage/
```

**Output:**
```
+----------------------------------+------+--------+--------+
| Module                           | Line | Branch | Method |
+----------------------------------+------+--------+--------+
| CustomsClearance.Platform.Common | 100% | 100%   | 100%   |
+----------------------------------+------+--------+--------+
```
Threshold enforcement via MSBuild properties confirmed working.

---

## Step 5 — Prove the gate actually fails when coverage drops

**Action:** temporarily added `src/Platform/Common/TempUncoveredCode.cs` with an uncalled method.

**Command:**
```bash
dotnet test tests/Unit/Platform.Common.Tests/CustomsClearance.Platform.Common.Tests.csproj \
  -p:CollectCoverage=true -p:Threshold=80 -p:ThresholdType=line -p:ThresholdStat=total \
  -p:CoverletOutputFormat=cobertura -p:CoverletOutput=./TestResults/coverage/ > /tmp/covout.txt 2>&1; echo "EXIT: $?"
```

**Output:**
```
| CustomsClearance.Platform.Common | 0%   | 100%   | 0%     |
error : The total line coverage is below the specified 80
EXIT: 1
```
Confirmed: exit code 1 when under threshold — a real, enforceable gate, not cosmetic.

**Cleanup:** removed `TempUncoveredCode.cs`, re-ran to confirm back to 100%.

---

## Step 6 — Embed thresholds in the .csproj itself (not CLI flags)

**Change:** added to `Platform.Common.Tests.csproj`'s `PropertyGroup`:
```xml
<CollectCoverage>true</CollectCoverage>
<CoverletOutputFormat>cobertura</CoverletOutputFormat>
<Threshold>80</Threshold>
<ThresholdType>line</ThresholdType>
<ThresholdStat>total</ThresholdStat>
```

**Verification command:**
```bash
dotnet test
```

**Output:** same 100%/100%/100% table, confirming properties embedded in the project file work identically to CLI flags — no `-p:` arguments needed at the CI call site.

---

## Step 7 — Update `build/scripts/test.sh` to add report generation

**Change:** added `dotnet tool restore` + `dotnet reportgenerator` steps after the test run, targeting `**/TestResults/**/coverage.cobertura.xml`.

**First run output (unexpected):**
```
Assemblies: 0
Classes: 0
No assemblies have been covered.
```

---

## Step 8 — Diagnose the empty coverage report (three real findings)

### Finding A — stale/leftover coverage files from earlier manual runs
```bash
find . -name "coverage.cobertura.xml"
```
Found stale files at `tests/Unit/Platform.Common.Tests/coverage.cobertura.xml` and a `TestResults/coverage/` subfolder left from Step 5's manual CLI testing. Cleaned up:
```bash
rm -f tests/Unit/Platform.Common.Tests/coverage.cobertura.xml
rm -rf tests/Unit/Platform.Common.Tests/TestResults
rm -rf TestResults
```
Also added to `.gitignore`: `TestResults/`, `coverage.cobertura.xml`, `*.trx`.

### Finding B — `coverlet.collector` + `coverlet.msbuild` conflict
Re-running after cleanup still produced an empty cobertura file (`<classes />`, `lines-valid="0"`) despite the console table showing 100%. Diagnosed as both coverage packages being present in the same project causing conflicting output. **Fix:** removed `coverlet.collector`, kept `coverlet.msbuild` only.

### Finding C — `--no-build` skips instrumentation
Still empty after Finding B's fix. Diagnosed: `test.sh` was calling `dotnet test --no-build`, and coverlet.msbuild's instrumentation hooks into `dotnet test`'s own build step — reusing `build.sh`'s prior build output skips instrumentation entirely, producing an empty report with no error.

**Verification command (full clean rebuild, no `--no-build`):**
```bash
rm -rf tests/Unit/Platform.Common.Tests/{TestResults,bin,obj} src/Platform/Common/{bin,obj} TestResults
dotnet test
```

**Output:**
```
+----------------------------------+------+--------+--------+
| CustomsClearance.Platform.Common | 100% | 100%   | 100%   |
+----------------------------------+------+--------+--------+
```
Console showed 100%, but the persisted `coverage.cobertura.xml` file *still* showed `<classes />` (empty). This led to Finding D.

### Finding D — const-only class produces a vacuous "100%"
`PlaceholderModuleMarker` originally contained only `public const string ModuleName = "..."` — a compile-time literal with no IL method body, hence zero coverable lines. Coverlet correctly reports 0-of-0 as a vacuous 100% (`line-rate="1"`), which is indistinguishable at a glance from genuine full coverage of real code.

**Fix:** added a real method to prove the pipeline against actual coverable code:
```csharp
public static string Describe() => $"Module: {ModuleName}";
```
plus a corresponding test:
```csharp
[Fact]
public void Describe_ReturnsModuleNameWithPrefix()
{
    Assert.Equal("Module: Platform.Common", PlaceholderModuleMarker.Describe());
}
```

**Final verification command:**
```bash
rm -rf tests/Unit/Platform.Common.Tests/{TestResults,bin,obj} src/Platform/Common/{bin,obj} TestResults
bash build/scripts/build.sh
bash build/scripts/test.sh
```

**Output (genuine, non-vacuous):**
```
Summary
  Assemblies: 1
  Classes: 1
  Files: 1
  Line coverage: 100%
  Covered lines: 1
  Coverable lines: 1
  Total lines: 19
  Method coverage: 100% (1 of 1)

CustomsClearance.Platform.Common                              100%
  CustomsClearance.Platform.Common.PlaceholderModuleMarker    100%
```
This confirmed the whole pipeline (build → test → coverage → threshold → report) works against real, measurable code — not just an empty scaffold.

---

## Step 9 — Add static analysis (SonarAnalyzer.CSharp)

**Change:** added to `Directory.Build.props`:
```xml
<ItemGroup>
  <PackageReference Include="SonarAnalyzer.CSharp" Version="9.32.0.97167" PrivateAssets="all" />
</ItemGroup>
```

**Verification command:**
```bash
dotnet build
```

**Output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
Clean build confirmed the analyzer doesn't break anything by default.

---

## Step 10 — Prove the analyzer actually fires on real issues

**Action:** temporarily added `src/Platform/Common/TempAnalyzerCheck.cs` containing a duplicate-branch bug and a method that should be static.

**Command:**
```bash
dotnet build src/Platform/Common/CustomsClearance.Platform.Common.csproj
```

**Output:**
```
warning S1862: This branch duplicates the one on line 7. (RSPEC-1862)
warning S2325: Make 'DoSomething' a static method. (RSPEC-2325)
Build succeeded.
    2 Warning(s)
```
Confirmed real Sonar rules firing, not a no-op inclusion.

**Cleanup:** removed `TempAnalyzerCheck.cs`, re-ran build — back to 0 warnings.

---

## Step 11 — Confirm lint still passes with the new analyzer present

**Command:**
```bash
dotnet format --verify-no-changes --severity warn
```

**Output:** (no output — clean pass, no violations)

---

## Step 12 — Update CI workflows to match

**`.github/workflows/ci.yml`:**
- Test job renamed "Unit Tests + Coverage Gate"
- Removed the redundant separate "Build" step before Test (since `test.sh`'s `dotnet test` now builds itself — reusing a prior `--no-build` output would silently skip coverage instrumentation, per Step 8 Finding C)
- Updated comments to explain the coverage-gate mechanism and the no-`--no-build` requirement

**`build/azure-pipelines.yml`** (kept in lockstep per ADR-0005):
- Same redundant-Build-step removal in the Test stage
- Added `PublishCodeCoverageResults@2` task (Azure DevOps-native equivalent of the GitHub Actions artifact upload)

---

## Step 13 — Full local pipeline re-run (final verification, all three stages)

**Commands:**
```bash
rm -rf tests/Unit/Platform.Common.Tests/{TestResults,bin,obj} src/Platform/Common/{bin,obj} TestResults
bash build/scripts/build.sh
bash build/scripts/lint.sh
bash build/scripts/test.sh
```

**Output:**
```
=== BUILD ===
    0 Warning(s)
    0 Error(s)

=== LINT ===
== Lint (dotnet format --verify-no-changes) ==
(clean)

=== TEST ===
Summary
  Assemblies: 1
  Classes: 1
  Line coverage: 100%
  Coverable lines: 1
  Method coverage: 100% (1 of 1)

CustomsClearance.Platform.Common                              100%
  CustomsClearance.Platform.Common.PlaceholderModuleMarker    100%
```

All three stages (Build, Lint, Test+Coverage) pass end-to-end locally, with genuine non-vacuous coverage numbers.

---

## Step 14 — Documentation and records produced

| File | Purpose |
|---|---|
| `adr/0007-static-analysis-and-coverage-gates.md` | Formal decision record — tool choices, threshold classification table, and the four findings from Steps 8/10 written up as permanent "worth knowing before touching this again" notes |
| `Phase-1_Architecture_Engineering_Prep/Day12_Code_Scanning_Coverage_Gates.docx` | Narrative deliverable — what was adopted and why, structured per the Day-Wise Plan's Day 12 requirements |
| `Phase-1_Architecture_Engineering_Prep/Day12_Execution_Log.md` | This file — raw step-by-step commands and outputs |
| `CONTRIBUTING.md` (updated) | Definition of Done now requires per-project coverage threshold declaration and review of new SonarAnalyzer warnings |
| `docs/WORK-ITEMS.md` | Unchanged this day (no new follow-up items generated beyond what's already logged as PLAT-001 from Day 11) |

---

## Summary of Files Changed/Added This Day

- `Directory.Build.props` — added SonarAnalyzer.CSharp reference
- `.gitignore` — added `TestResults/`, `coverage.cobertura.xml`, `*.trx`
- `.config/dotnet-tools.json` — new (local tool manifest for ReportGenerator)
- `tests/Unit/Platform.Common.Tests/CustomsClearance.Platform.Common.Tests.csproj` — coverlet.msbuild only (collector removed), coverage properties embedded
- `src/Platform/Common/PlaceholderModuleMarker.cs` — added `Describe()` method
- `tests/Unit/Platform.Common.Tests/PlaceholderModuleMarkerTests.cs` — added covering test
- `build/scripts/test.sh` — removed `--no-build`, added ReportGenerator step
- `.github/workflows/ci.yml` — Test job updated
- `build/azure-pipelines.yml` — Test stage updated to match
- `CONTRIBUTING.md` — Definition of Done updated
- `adr/0007-static-analysis-and-coverage-gates.md` — new
- `Phase-1_Architecture_Engineering_Prep/Day12_Code_Scanning_Coverage_Gates.docx` — new

Not yet committed or pushed — pending your review and commit-message request, per the established workflow.
