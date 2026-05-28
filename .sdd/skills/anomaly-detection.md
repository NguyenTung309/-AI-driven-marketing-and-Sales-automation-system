---
name: anomaly-detection
description: Z-score anomaly trên time series KPI (CPL spike, lead drop, response-time outlier)
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: []
    emoji: 📈
    homepage: https://numerics.mathdotnet.com/
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Report, Agent-Ads]
    csharp_adapter: Clawbot.Agents.Core.Skills.Ops.IAnomalyDetector
    owner: P5 PM / Data
    updated: 2026-05-27
---

# Skill — Anomaly Detection

## Purpose
UC-I05 CPL spike alert + UC-A10 SLA alert. Đơn giản, deterministic, in-process.

## Backing
- Math.NET Numerics Statistics (mean + stddev).
- Rolling window 14 days mặc định, threshold |z| > 2.5.

## Guarantees
- p95 < 100 ms cho series 1k points.
- Output kèm reason (z-score value) cho Telegram alert message.
