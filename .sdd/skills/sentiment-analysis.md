---
name: sentiment-analysis
description: Polarity 3-class (negative/neutral/positive) cho tin nhắn tiếng Việt
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [SENTIMENT_INFERENCE_URL]
      bins: []
    primaryEnv: SENTIMENT_INFERENCE_URL
    emoji: 😊
    homepage: https://huggingface.co/wonrax/phobert-base-vietnamese-sentiment
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Chat, Agent-SaleAssist, Agent-Report]
    csharp_adapter: Clawbot.Agents.Core.Skills.Nlp.ISentimentAnalyzer
    owner: P4 QA
    updated: 2026-05-27
---

# Skill — Sentiment Analysis

## Purpose
Phát hiện khách bực bội → escalate sale. Tracking sentiment drift theo time → cảnh báo churn risk.

## Backing
- Primary: wonrax/phobert-base-vietnamese-sentiment.
- Alt: 5CD-AI/Vietnamese-Sentiment-visobert.

## Guarantees
- p95 < 150 ms.
- Negative + confidence > 0.85 trong 3 tin liên tiếp → tự động escalate.
