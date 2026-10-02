# Dashboard

Open **Tools → Dashboard** in the GUI. The window has two tabs:

- **Consumption** retains the token totals, input/output timeline, model distribution and detailed call history.
- **Speed** shows average, minimum and maximum throughput in output tokens per second, a timeline, model comparisons and detailed measurements.

Both tabs share filters for **Today, 3 days, 7 days, 30 days or 1 year**, provider connection, model, activity and project. They refresh automatically every 15 seconds. Newly completed calls appear on the next refresh; **Refresh** also reloads them immediately. Each tab retains its own detail page, and **Copy details as CSV** exports all filtered rows for the selected tab.

## How speed is measured

Per-call throughput is the recorded output-token count divided by the recorded total request duration. This duration includes waiting for the model, prompt processing and reasoning. Output includes reasoning tokens when included in the provider's output counter. A model's average, and each timeline bucket's average, use total output tokens divided by total duration. Minimum and maximum are the slowest and fastest individual call averages.

Only completed calls with a known model, a positive output-token count and a valid positive duration contribute to speed statistics. Failed, interrupted or unmeasurable calls remain in Consumption and are counted as excluded in Speed. **≈** marks an estimated output count. Historical records can contribute when they contain the required data.

The Dashboard reuses the existing local usage ledger, including calls from the CLI using that same database. It does not send additional requests to model providers. Date filters use the local calendar; hourly buckets preserve daylight-saving transitions.
