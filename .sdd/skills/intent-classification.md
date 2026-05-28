---
name: intent-classification
description: Phân loại ý định khách Việt (greeting/price_inquiry/objection/schedule_request/farewell/spam/escalation_request/other)
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [PHOBERT_INFERENCE_URL]
      bins: []
    primaryEnv: PHOBERT_INFERENCE_URL
    emoji: 🎯
    homepage: https://huggingface.co/vinai/phobert-base-v2
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Chat, Agent-SaleAssist]
    csharp_adapter: Clawbot.Agents.Core.Skills.Nlp.IIntentClassifier
    owner: P4 QA
    updated: 2026-05-27
---

# Skill — Intent Classification

## Purpose
Mỗi tin nhắn inbound được phân loại 1 trong 8 intent → Agent-Chat chọn `chat_scenarios` template phù hợp.

## I/O contract
- Input: `{ text: string, locale?: string }`
- Output: `{ label: string, confidence: float }` với label ∈ {`greeting`, `price_inquiry`, `objection`, `schedule_request`, `farewell`, `spam`, `escalation_request`, `other`}

## Backing
- Model: vinai/phobert-base-v2 fine-tuned on local sale logs (≥5k labeled samples).
- Inference: HF Inference Endpoint hoặc ONNX local.

## Guarantees
- p95 latency < 200 ms.
- Cache hit ratio ≥ 60% (Redis, key = content-hash).
- Confidence < 0.7 → fallback `other` + RAG.
