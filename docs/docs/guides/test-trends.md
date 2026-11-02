---
sidebar_position: 12
---

# Test Trends (Run History)

The [HTML report](/docs/guides/html-report) shows one run. To see how your tests behave **over time**, keep a small run history with the `tunit-report` tool and render it as a trends page:

- pass rate and total duration per run
- tests that started failing in the latest run
- the most failing and the slowest tests across recent runs

It's opt-in and runs after your tests, so nothing changes in the test run itself.

## Quick Start

```bash
dotnet tool install --global TUnit.Reporting.Tool

# After each test run: record it in the history
tunit-report history append --report TestResults --store .tunit-history

# Whenever you want: render the trends page
tunit-report trends --store .tunit-history --output trends.html
```

`--report` takes either a single `*.tunit-report.json` sidecar or a directory. Every sidecar found in a directory is merged into **one run**, so a job that tests several projects or target frameworks adds a single point to the trend.

The history is a plain [JSON Lines](https://jsonlines.org/) file (`history.jsonl`) in the store directory, with one compact line per run: the commit, branch and timestamp, plus each test's status and duration. Output, exceptions and traces are not kept, so the file stays small. Appending the same report twice is a no-op.

## Options

| Command | Option | Description |
|---|---|---|
| `history append` | `--report <file\|dir>` | Sidecar file, or a directory of sidecars to merge into one run. |
| | `--store <dir>` | History directory. Created on demand. |
| | `--keep <n>` | Keep only the newest `n` runs. Default: `200`. |
| `trends` | `--store <dir>` | History directory written by `history append`. |
| | `--output <file>` | Trends page path. Default: `<store>/trends.html`. |
| | `--last <n>` | Analyze only the newest `n` runs. Default: `50`. |
| | `--branch <name>` | Analyze only runs from this branch. |

## GitHub Actions

Keep the history between workflow runs with a cache. Each run restores the latest history for its branch, falling back to the default branch, then saves the updated history under a new key:

```yaml
- name: Restore test history
  uses: actions/cache/restore@v4
  with:
    path: .tunit-history
    key: tunit-history-${{ github.ref_name }}-${{ github.run_id }}
    restore-keys: |
      tunit-history-${{ github.ref_name }}-
      tunit-history-main-

- name: Test
  run: dotnet test

- name: Update test trends
  if: always()
  run: |
    dotnet tool install --global TUnit.Reporting.Tool
    tunit-report history append --report . --store .tunit-history
    tunit-report trends --store .tunit-history --output trends.html

- name: Save test history
  if: always()
  uses: actions/cache/save@v4
  with:
    path: .tunit-history
    key: tunit-history-${{ github.ref_name }}-${{ github.run_id }}

- name: Upload trends page
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: test-trends
    path: trends.html
```

TUnit detects the commit and branch on GitHub Actions, GitLab CI and Bitbucket Pipelines, so each run in the history is labelled automatically.

:::tip
Append once per pipeline run, after all test steps. If parallel jobs append to the same store, run the append in a final job after downloading every job's sidecars, the same way as [merging reports from matrix jobs](/docs/guides/report-aggregation#multiple-steps-or-matrix-jobs-the-tunit-report-tool).
:::
