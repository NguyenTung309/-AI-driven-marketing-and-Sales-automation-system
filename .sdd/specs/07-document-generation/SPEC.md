# SPEC-07 — Document Generation

Status: `DRAFT`
Traces to: FR-07, UC-B09..B12, UC-G05, SW-107..114

## 1. Business Context

Sale tạo báo giá PDF cá nhân hóa <30s từ context conversation. Brochure khóa học, slide demo, onboarding kit.

## 2. User Stories

- AS A Sale I WANT to click "Generate Quote" and get a personalized PDF sent to the customer SO THAT I close faster.

## 3. Acceptance Criteria (EARS)

- WHEN `POST /api/docs/generate` is called with `(contact_id, template_id, vars)` THE SYSTEM SHALL render and return URL in <30s.
- THE SYSTEM SHALL store the rendered file in MinIO with `created_at + 7d` retention link.
- WHEN sent THE SYSTEM SHALL record `sent_via` and `sent_at`. WHEN opened THE SYSTEM SHALL record `opened_at`.
- WHILE template `deleted_at IS NOT NULL` THE SYSTEM SHALL forbid generation against it.
- IF the template references missing fields THEN THE SYSTEM SHALL return 422 with the missing keys.

## 4. API Contracts / Data Models

- `POST /api/docs/generate`
- `GET /api/docs?contact_id=` — list documents for a contact
- `GET /api/docs/{id}/track` — pixel for read receipt
- gRPC: `DocsAgent.Generate`
- Tables: `document_templates`, `generated_documents`.

## 5. Technical Constraints

- PDF render via Python ReportLab worker OR .NET QuestPDF.
- Async pipeline: API enqueues → worker renders → API polls / SignalR push URL.

## 6. Out of Scope

- DOCX output (PDF only Phase 1).

## 7. NFR

NFR-01 (gen p95 <30s), NFR-04 (queue throughput).

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| Template missing field | preflight | 422 + key list | sale edits |
| MinIO down | adapter exception | "retry later" | manual retry |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| QuestPDF vs ReportLab | P1 | T9 | open |
