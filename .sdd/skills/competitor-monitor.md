---
name: competitor-monitor
description: Theo dõi post mới của competitor (RSS / scheduled scrape) để inform content + ads
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: []
    envVars:
      - name: COMPETITOR_FEEDS_JSON
        required: true
        description: JSON array of RSS / scrape URLs per tenant
    emoji: 👀
    homepage: https://learn.microsoft.com/en-us/dotnet/api/system.servicemodel.syndication
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Research]
    csharp_adapter: Clawbot.Agents.Core.Skills.Content.ICompetitorMonitor
    owner: P2 Growth
    updated: 2026-05-27
---

# Skill — Competitor Monitor

## Purpose
Weekly: scan 5-10 competitor trung tâm tiếng Trung VN — title + snippet + URL → feed Agent-Research để propose counter-content.

## Backing
- `System.ServiceModel.Syndication` (BCL) cho RSS.
- HttpClient + AngleSharp cho scrape khi feed không có.
- ETag/Last-Modified caching.

## Guarantees
- p95 < 5s cho 10 sources.
- Dedupe by URL hash + content fingerprint.
