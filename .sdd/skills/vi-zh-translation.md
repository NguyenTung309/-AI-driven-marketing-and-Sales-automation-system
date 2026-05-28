---
name: vi-zh-translation
description: Dịch Việt ↔ Trung với glossary KB constrain (giữ tên giáo trình / level chuẩn)
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [ANTHROPIC_API_KEY]
      bins: []
    primaryEnv: ANTHROPIC_API_KEY
    emoji: 🔄
    homepage: https://docs.anthropic.com/
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Chat, Agent-Content]
    csharp_adapter: Clawbot.Agents.Core.Skills.Content.IViZhTranslator
    owner: P4 QA + Học thuật
    updated: 2026-05-27
---

# Skill — VI ↔ ZH Translation

## Purpose
Bilingual support: học viên hỏi 1 cụm 中文 → trả lời VI + 中文. Content phòng marketing VN ↔ Trung. Glossary giữ thuật ngữ chuẩn (HSK level, giáo trình tên riêng).

## Backing
- Claude Sonnet 4.6 + glossary KB module `GLOSSARY-VI-ZH` (xem `kb_modules.code`).

## Guarantees
- p95 < 2s.
- Output luôn kèm danh sách glossary hits (terminology used) để QA audit.
