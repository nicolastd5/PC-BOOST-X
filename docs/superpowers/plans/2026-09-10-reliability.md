# Reliability fixes implementation plan

> **For agentic workers:** Execute the independent cleanup, profiles and command/UI tasks using the dispatching-parallel-agents skill, with integration and verification by the primary agent.

**Goal:** Correct the defects established in the review without applying optimizations to the development machine.

**Architecture:** Retain WPF/MVVM. Make registry snapshots lossless and uniquely identified, replay pending snapshots in reverse chronological order, archive completed restores, and retain failed work for retries. Record original non-registry settings before changing them. Propagate command failures to callers and distinguish partial outcomes in the UI.

**Tech stack:** .NET 8, WPF, CommunityToolkit.Mvvm. Dependency-free console regression runners compile the real service sources with substitutes only at OS boundaries.

**Spec:** The preceding project review and the user's instruction to correct every finding.

## Constraints

- No optimizations, service changes, restore points, or cleanup on the host Windows installation during tests.
- Fixtures are confined to test-owned directories. Registry tests use an in-memory implementation.
- Do not guess original values missing from legacy backups; report unrecoverable legacy data.
- Preserve user changes and the existing interface structure.

## Tasks

- [x] Registry and reversal: `RegistryBackupService.cs`, `RevertAllService.cs`, `SystemSettingsBackupService.cs`, `OptimizationStateStore.cs`, `tests/Backup.Tests`. Reproduce collisions, binary/QWORD/expandable-string loss, reversed ordering, false success and retry behavior. Use unique snapshots, strict validation, archive-on-success and original settings rather than hard-coded defaults.
- [x] Cleanup and startup: `CleanupService.cs`, `StartupService.cs`, `tests/CleanupStartup.Tests`. Reproduce thumbnail over-deletion and broken startup reactivation. Share selection policy, protect traversal boundaries, preserve disabled items and restore their original values/files.
- [x] Game profiles: `GameProfileService.cs`, `tests/GameProfiles.Tests`. Reproduce overwritten compatibility/GPU preferences; snapshot before writes, preserve unrelated values, restore snapshots and report failures. Fix nullability warnings.
- [x] Commands and UI: `ProcessRunner.cs`, `RestorePointService.cs`, both viewmodels, `MainWindow.xaml`, `tests/Commands.Tests`. Reproduce process/PowerShell failure and false restore-point success. Verify creation, abort protected operations after failed restore-point creation, release busy state, expose cancellation, and refresh restored state.
- [x] Catalog integration: `OptimizationCatalog.cs`, `PowerPlanService.cs`, `GameOptimizationService.cs`, `OptimizationStateDetector.cs`. Back up every persistent mutation, validate commands, correct missing/mismatched snapshots, and avoid persisting transient actions as applied.
- [x] Verification: run every regression runner, Release build and publish; inspect the combined diff and document limits. Keep source and regression tests in the workspace for review.

## Commands

```powershell
dotnet run --project tests/Backup.Tests -c Release
dotnet run --project tests/CleanupStartup.Tests -c Release
dotnet run --project tests/GameProfiles.Tests -c Release
dotnet run --project tests/Commands.Tests -c Release
dotnet build ProjectBoostX.sln -c Release
dotnet publish src/ProjectBoostX/ProjectBoostX.csproj -c Release -r win-x64 --self-contained false -o publish
```
