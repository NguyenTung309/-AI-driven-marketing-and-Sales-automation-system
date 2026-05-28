---
name: pdf-table-renderer
description: Render bảng giá / lộ trình lessons → PDF branded (báo giá cá nhân hóa, brochure)
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: []
    emoji: 📄
    homepage: https://www.questpdf.com/
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Docs]
    csharp_adapter: Clawbot.Agents.Core.Skills.Ops.IPdfTableRenderer
    owner: P3 Sales Lead + BGĐ
    updated: 2026-05-27
---

# Skill — PDF Table Renderer

## Purpose
FR-07: render PDF < 30s. QuestPDF native .NET — không cần subprocess.

## Backing
- QuestPDF Community license (free < $1M revenue org).
- Brand tokens (logo/colors/font) load từ tenant config (SW-076).

## Guarantees
- p95 < 1s cho doc < 5 trang.
- Output PDF/A-compliant (cho audit + email forward).
