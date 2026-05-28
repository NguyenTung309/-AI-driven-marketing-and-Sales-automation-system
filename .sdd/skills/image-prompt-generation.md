---
name: image-prompt-generation
description: Sinh prompt visual cho FLUX/DALL-E từ content brief + brand tokens
version: 1.0.0
metadata:
  openclaw:
    requires:
      env: [ANTHROPIC_API_KEY]
      bins: []
    primaryEnv: ANTHROPIC_API_KEY
    envVars:
      - name: REPLICATE_API_TOKEN
        required: false
        description: Cần khi muốn end-to-end render ảnh, không chỉ prompt
    emoji: 🖼️
    homepage: https://replicate.com/black-forest-labs/flux-schnell
    os: [linux, windows, macos]
  clawbot:
    agents: [Agent-Content]
    csharp_adapter: Clawbot.Agents.Core.Skills.Content.IImagePromptGenerator
    owner: P2 Growth + Content team
    updated: 2026-05-27
---

# Skill — Image Prompt Generation

## Purpose
Content brief: "post Reels mời học HSK3" → prompt FLUX với style: bright, Asian young learner, classroom, no text overlay (CapCut sẽ add).

## Backing
- Claude Sonnet 4.6 → structured prompt + negative prompt.
- Optional Replicate FLUX để render thực.

## Guarantees
- p95 < 4s (prompt-gen only).
- Negative prompt mặc định block: text overlay, watermark, low-quality, blurred.
