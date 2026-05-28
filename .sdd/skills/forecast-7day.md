---
name: forecast-7day
description: Forecast 7-day lead/spend/conversion bằng ML.NET TimeSeries SSA
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: []
    emoji: 🔮
    homepage: https://learn.microsoft.com/en-us/dotnet/machine-learning/tutorials/time-series-demand-forecasting
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Report]
    csharp_adapter: Clawbot.Agents.Core.Skills.Ops.IForecaster
    owner: P5 PM / Data
    updated: 2026-05-27
---

# Skill — 7-Day Forecast

## Purpose
UC-D10 pipeline forecast tuần. Đầu vào: `kpi_daily` chuỗi 60 ngày → forecast 7 day ra (point + lower/upper band).

## Backing
- ML.NET TimeSeries SSA (Singular Spectrum Analysis).
- Window 14, series length 60, train per-tenant per-platform.

## Guarantees
- p95 < 500 ms cho series 60 points.
- Forecast cached 24h trong Redis (key per tenant+platform+date).
