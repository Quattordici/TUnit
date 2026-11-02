---
sidebar_position: 12
---

# Test Trends (Run History)

The [HTML report](/docs/guides/html-report) shows one run. To see how your tests behave **over time**, keep a small run history with the `tunit-report` tool and render it as a trends page.

![Test trends page with pass-rate tiles, a results-per-run chart, a duration chart and tables of newly failing, flaky and slowing-down tests](/img/test-trends.png)

It's opt-in and runs after your tests, so the test run itself doesn't change.

## Quick Start

```bash
dotnet tool install --global TUnit.Reporting.Tool

# After each test run: record it in the history
tunit-report history append --report TestResults --store .tunit-history

# Whenever you want: render the trends page
tunit-report trends --store .tunit-history --output trends.html
```

`--report` takes either a single `*.tunit-report.json` sidecar or a directory. Every sidecar found in a directory is merged into **one run**, so a job that tests several projects or target frameworks adds a single point to the trend.

## What the Page Shows

- **Latest run vs. the previous one:** pass rate, duration, test count and failures, with the change since the previous run.
- **Results and duration per run:** a stacked passed/failed/skipped chart and a duration chart. Hover a bar or point for its date, commit and numbers.
- **Newly failing:** tests that failed in the latest run but not in their previous one, including new tests that fail straight away.
- **Flaky tests:** tests that passed only after a [retry](/docs/execution/retrying), or had a one-off result between opposite outcomes (a lone failure between passes, or the reverse). A test that breaks and stays broken until it's fixed is a regression, not flakiness, so it isn't listed here.
- **Slowing down:** tests that took at least 1.5× (and at least 50 ms) longer in the latest run than their median over at least three earlier runs.
- **Most failing and slowest tests** across the analyzed runs.
- **Recent runs**, with a link to each commit when the run came from GitHub, GitLab or Bitbucket.

Each test row shows its recent results as colored marks, oldest first, so you can tell a one-off from a pattern at a glance.

## The History File

The history is a plain [JSON Lines](https://jsonlines.org/) file (`history.jsonl`) in the store directory, with one compact line per run: the commit, branch and timestamp, plus each test's status, duration and attempt count. Output, exceptions and traces are not kept, so even hundreds of runs stay small.

- Appending the same report twice does nothing.
- Concurrent appends to the same store on one machine are serialized with a lock file (`history.jsonl.lock`).
- A corrupt or truncated line is skipped, so it never makes the rest of the history unreadable.

Tests are tracked per suite. A single sidecar's suite is its test assembly. A directory's suite is always `All tests`, so adding another test project later keeps the history continuous. Use `--suite` to choose your own name, e.g. to keep separate histories for unit and integration tests in one store.

## Options

| Command | Option | Description |
|---|---|---|
| `history append` | `--report <file\|dir>` | Sidecar file, or a directory of sidecars to merge into one run. |
| | `--store <dir>` | History directory. Created on demand. |
| | `--keep <n>` | Keep only the newest `n` runs. Default: `200`. |
| | `--suite <name>` | Suite name for the run. Default: the assembly name, or `All tests` for a directory. |
| | `--commit <sha>` | Commit to record. Default: detected by TUnit on GitHub Actions, GitLab CI and Bitbucket Pipelines. |
| | `--branch <name>` | Branch to record. Default: detected the same way. |
| `trends` | `--store <dir>` | History directory written by `history append`. |
| | `--output <file>` | Trends page path. Default: `<store>/trends.html`. |
| | `--last <n>` | Analyze only the newest `n` runs. Default: `50`. |
| | `--branch <name>` | Analyze only runs from this branch. |
| | `--github-summary` | Also write a Markdown summary to the GitHub Actions job summary (or stdout outside GitHub Actions). |

On other CI systems, or locally, pass `--commit` and `--branch` yourself, e.g. `--commit $(git rev-parse HEAD) --branch $(git branch --show-current)`.

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
    tunit-report trends --store .tunit-history --output trends.html --github-summary

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

With `--github-summary`, the job summary gets a compact overview: the latest pass rate and duration with sparklines, plus the newly failing, flaky and slowing-down tests.

:::tip
Append once per pipeline run, after all test steps. If parallel jobs produce reports, append in a final job after downloading every job's sidecars, the same way as [merging reports from matrix jobs](/docs/guides/report-aggregation#multiple-steps-or-matrix-jobs-the-tunit-report-tool).
:::
