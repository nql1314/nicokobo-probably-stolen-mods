# Production source-linked performance checks

Run from the repository root:

```powershell
./tools/production-perf-checks/Run-ProductionPerf.ps1 -Label after
```

The runner evaluates the existing synthesis and matrix check projects, links the
same production sources and replaces their entry points with focused workloads.
Only generated copies of the existing headless ports receive inventory-scan and
ownership counters. It does not modify the original fixtures, install packages,
load Unity, read a player save or start the game. Output stays under
`_build/review-20261031/perf-<Label>/`.

Each workload uses 100 or 1000 items, including nested inventory, and records 51
samples of 8 calls after 64 warm-up calls. Tiered compilation is disabled for the
generated executable. Results contain p50/p95 time, allocated bytes, complete
inventory scans and ownership checks per call, plus runtime metadata and hashes
of evaluated source inputs. Same-frame changes to ownership, units, inventory
membership, identifiers, run state and customer type have explicit assertions;
the existing turbo growth checks also run.

For a before/after comparison, save the original `ManufacturedEffectsRuntime.cs`
and `MatrixTurbo.cs` in a workspace directory before editing, then pass its path:

```powershell
./tools/production-perf-checks/Run-ProductionPerf.ps1 -Label before -BaselineSources ./_build/review-20261031/perf-baseline-sources
```

The timings and byte counts describe .NET 6 with simulated native ports. The
ports create lists differently from the native game. They establish call and
allocation changes at this boundary, not Unity/Mono FPS, actual player-save
latency or cross-process save compatibility. Use matching source hashes and the
same ports when comparing runs. Unchanged workloads act as timing controls;
concurrent desktop work can still move p50/p95.
