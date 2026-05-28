---
name: language-detection
description: Phát hiện ngôn ngữ (vi/en/zh/...) trong tin nhắn để route tone-voice phù hợp
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: []
      bins: [fasttext]
    emoji: 🌐
    homepage: https://fasttext.cc/docs/en/language-identification.html
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Chat, Agent-SaleAssist]
    csharp_adapter: Clawbot.Agents.Core.Skills.Nlp.ILanguageDetector
    owner: P4 QA
    updated: 2026-05-27
---

# Skill — Language Detection

## Purpose
Khách viết EN/中文 → switch sang skill `vi-zh-translation` hoặc trả lời EN. Auto-pick `kb_modules.code = 'KB-06-platform-specs'` tone tương ứng.

## Backing
- Model: fasttext lid.176.ftz (~1 MB).
- Load 1 lần khi service start; ScoreAsync trả top-1 + confidence.

## Guarantees
- p95 < 20 ms (in-process).
- Min text length 5 chars; ngắn hơn → trả `unknown`.
