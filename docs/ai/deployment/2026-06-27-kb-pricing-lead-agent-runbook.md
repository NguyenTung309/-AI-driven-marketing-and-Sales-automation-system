---
phase: deployment
title: KB Pricing Lead Agent Runbook
description: Operator setup for course FAQ RAG, quote pricing vars, and lead scoring rules
feature: kb-pricing-lead-agent
date: 2026-06-27
status: draft
---

# KB Pricing Lead Agent Runbook

## Context

Use 3 different paths:

| Need | Correct path | Not for this |
|---|---|---|
| Course FAQ: sĩ số, lịch học, giáo viên, cam kết | KB module → deploy → Qdrant | MinIO |
| Exact price quote | `quote` document template + `vars` | RAG |
| Lead score criteria | `lead_scoring_rules` + message classifier events | RAG/MinIO |

MinIO stores generated PDFs only. Uploading sheets/docs to MinIO does not train agents.

## 1. Infrastructure

Start local infrastructure:

```powershell
docker compose --env-file deploy\.env -f deploy\docker-compose.yml up -d sqlserver redis rabbitmq qdrant minio
```

Required env/config:

| Key | Used by | Notes |
|---|---|---|
| `Embedding__ApiKey` | API + AgentService | Required for real semantic RAG. Empty key falls back to hash embeddings. |
| `Embedding__Model` | API + AgentService | Default `text-embedding-3-small`. |
| `Embedding__Dimension` | API + AgentService | Default `1536`. Qdrant collection becomes `kb_v1536`. |
| `Embedding__BaseUrl` | API + AgentService | Optional OpenAI-compatible endpoint. |
| DB `llm_configs` + bound `AgentConfig.LlmConfigId` | AgentService | Required for chat replies. `Anthropic__ApiKey` in appsettings is legacy/not enough for scoped agent calls. |

Keep keys in env/user-secrets/secret manager or encrypted `llm_configs`. Do not commit real keys.

## 1.1 LLM config for chat

Create active LLM config through API/UI, then bind agents to it:

```http
POST /api/llm-configs
{
  "provider": "anthropic",
  "modelId": "claude-sonnet-4-6",
  "apiKey": "<secret>",
  "displayName": "Claude Sonnet"
}

PUT /api/agents/chat/settings
{
  "llmConfigId": "<llm-config-id>"
}

PUT /api/agents/sale_assist/settings
{
  "llmConfigId": "<llm-config-id>"
}
```

Use same binding for any agent that calls LLM. Unbound agents fail with `llm_config_not_configured`.

## 2. Course FAQ via KB/RAG

Create 1 KB module per knowledge area:

- `hsk-faq`
- `giaotiep-faq`
- optional shared `course-policy`

Write markdown for semantic retrieval:

```md
# HSK FAQ

## Sĩ số lớp
Lớp HSK tiêu chuẩn có ... học viên. Nếu lớp vượt ... trung tâm sẽ ...

## Lịch học
Lịch học có các khung ... Có thể đổi lịch theo ...

## Giáo viên
Giáo viên phụ trách ...

## Cam kết đầu ra
Cam kết ... Điều kiện áp dụng ...
```

Rules:

- One topic per paragraph block.
- Separate blocks with blank line.
- Keep each block under ~1000 chars.
- Put exact answer near topic title.
- Do not put active prices/flash sale amounts here.

Flow in UI:

1. Open Knowledge Base page.
2. Create module.
3. Create version with markdown.
4. Deploy version.
5. Add test cases.
6. Run test and keep accuracy >= 85%.

API equivalent:

```http
POST /api/kb/modules
POST /api/kb/modules/{moduleId}/versions
POST /api/kb/modules/{moduleId}/versions/{versionId}/deploy
POST /api/kb/modules/{moduleId}/test-cases
POST /api/kb/modules/{moduleId}/test
```

### Upload file → auto markdown (docx/xlsx/csv/pdf/txt/md)

Knowledge Base page has a **Tải tệp** button. Pick a file → server converts to markdown and saves a **draft** version (does NOT deploy). Operator reviews/edits the converted text, then clicks **Phát hành**.

```http
POST /api/kb/modules/{moduleId}/upload   (multipart/form-data, field "file")
→ 201 { version, sourceFormat, charCount, contentMd }
```

Format notes:
- **xlsx / csv** → markdown table per sheet. Best for the Google Sheet source (export `.xlsx`).
- **docx** → headings + paragraphs + tables.
- **pdf** → text only; tables/layout may break; scanned/image PDFs are rejected (no text layer).
- Max 10 MB. Permission `kb:write`.

Why draft-not-deploy: extraction always carries some noise (page breaks, merged cells). Review before it reaches Qdrant.

## 3. Exact pricing for Docs quote

Pricing links:

- HSK: https://docs.google.com/spreadsheets/d/1r_i4EDT4mUX81GKYfehA0xTV4ist811MCV_9iQaCWEY/edit?gid=1980443386#gid=1980443386
- Giao tiếp: https://docs.google.com/spreadsheets/d/1r_i4EDT4mUX81GKYfehA0xTV4ist811MCV_9iQaCWEY/edit?gid=1104699127#gid=1104699127

Docs agent renders templates. It does not calculate pricing unless caller passes calculated variables.

Formula:

```text
hoc_phi_cuoi = gia_niem_yet * (1 - uu_dai_percent) - qua_tang_tien_mat
```

Use quote template variables:

```html
<p>Học phí niêm yết: {{gia_niem_yet}}</p>
<p>Ưu đãi khoá lẻ: {{uu_dai_khoa_le}}</p>
<p>Ưu đãi combo: {{uu_dai_combo}}</p>
<p>Quà tặng tiền mặt: {{qua_tang_tien_mat}}</p>
<p><strong>Học phí sau ưu đãi: {{hoc_phi_cuoi}}</strong></p>
<p>{{ghi_chu_han_ap_dung}}</p>
```

Flash sale/monthly offer rule:

- Update variables for each campaign.
- Always fill `ghi_chu_han_ap_dung`, example:
  - `Áp dụng 00:00 28/06/2026 → 23:59 30/06/2026 (Flash sale 3 ngày).`
  - `Ưu đãi tháng 07/2026, áp dụng đến 31/07/2026.`
- When campaign ends, update variables back. No automatic scheduler exists yet.

Generate PDF:

```http
POST /api/docs/generate
```

Body includes `templateCode`, optional `contactId`, and `vars` with calculated values. Output PDF goes to MinIO and generated document metadata goes to SQL.

## 4. Lead scoring criteria

Current engine only applies configured rules by `event_code`. Seed rules first:

| event_code | Weight | Meaning |
|---|---:|---|
| `asked_substantive_question` | 8 | Question with real buying/learning intent. Exclude "vâng ạ", "để em xem". |
| `asked_class_size` | 12 | Asked class size/sĩ số. |
| `asked_schedule` | 10 | Asked schedule/lịch học. |
| `asked_teacher` | 10 | Asked teacher/giáo viên. |
| `asked_commitment` | 15 | Asked output guarantee/cam kết. |
| `fast_reply` | 5 | Customer replies within configured quick-reply window. |

Fastest setup — seed the default education rules in one call:

```http
POST /api/lead-scoring-rules/seed-defaults
→ { "created": N, "total": 8 }
```

Other APIs:

```http
GET /api/lead-scoring-rules
POST /api/lead-scoring-rules
DELETE /api/lead-scoring-rules/{id}
POST /api/leads/{leadId}/activities
```

Manual smoke:

```http
POST /api/leads/{leadId}/activities
{
  "eventCode": "asked_commitment",
  "platform": "facebook",
  "notes": "Khách hỏi cam kết đầu ra"
}
```

Expected: lead score increases by rule weight; stage recalculates cold/warm/hot.

## 5. Auto-scoring from chat messages (IMPLEMENTED)

Each inbound customer message handled by the chat agent is auto-scored:

1. `ILeadSignalClassifier` labels the message → interest event codes. LLM-backed
   (`ClaudeLeadSignalClassifier`) when a chat LLM config is bound; otherwise the
   `KeywordLeadSignalClassifier` baseline (always works, no LLM needed).
2. `fast_reply` is added when the gap to the previous customer message is ≤ 5 minutes.
3. `LeadAutoScorer` sums each signal's `LeadScoringRule` weight and applies one
   `Lead.AdjustScore` (creating the lead for the contact if none exists).

Runs best-effort after the reply is sent — a scoring failure never blocks the chat reply.
Event codes are the same ones in the rule table above, so **seed-defaults is the only
setup step**. To tune, edit weights via `POST /api/lead-scoring-rules` or deactivate a rule.

Wiring: [ChatAgentGrpcService.TryAutoScoreLeadAsync](../../src/agents/Clawbot.AgentService/Services/ChatAgentGrpcService.cs) →
[LeadAutoScorer](../../src/agents/Clawbot.AgentService/Services/LeadAutoScorer.cs).

To verify: send a customer message like "Lớp cam kết đầu ra thế nào ạ?" through the chat
agent → `GET /api/leads/{id}` shows score increased and stage moved toward warm/hot.

## Verification checklist

- `Embedding__ApiKey` present in API and AgentService runtime env.
- Active `llm_configs` record exists and `chat`/`sale_assist` agents are bound to it.
- Qdrant has `kb_v1536` collection after KB deploy.
- KB test case for "sĩ số" passes.
- Quote PDF shows exact `hoc_phi_cuoi` and campaign validity text.
- `POST /api/lead-scoring-rules/seed-defaults` returns created count.
- `POST /api/leads/{leadId}/activities` changes score.
- A customer chat message about cam kết/sĩ số auto-raises the lead score.
