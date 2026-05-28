# SPEC-02 — Knowledge Base

Status: `DRAFT`
Traces to: FR-02, UC-B01..B12, SW-023..034

## 1. Business Context

KB tiếng Trung là IP của trung tâm. Agent chỉ tư vấn chính xác khi KB chuẩn. Mục tiêu accuracy ≥85% trên 20-câu test set.

## 2. User Stories

- AS A QA I WANT to deploy a new KB version without downtime SO THAT chat agents pick it up immediately.
- AS A QA I WANT accuracy score per version SO THAT I can rollback if it drops.

## 3. Acceptance Criteria (EARS)

- THE SYSTEM SHALL store KB modules in `kb_modules` keyed by `(tenant_id, code)`.
- WHEN a new `kb_versions.status` transitions to `'deployed'` THE SYSTEM SHALL invalidate agent RAG cache for that module within 5 seconds.
- THE SYSTEM SHALL store each version's embedding as JSON float-array in `kb_versions.embedding NVARCHAR(MAX)` AND upsert the same vector into Qdrant collection `kb_versions` (point id = `kb_versions.id`). Vector similarity search uses Qdrant only.
- WHEN `accuracy_score < 0.85` THE SYSTEM SHALL emit a Telegram alert and prevent auto-deploy.
- WHILE a version is `deployed` THE SYSTEM SHALL prevent edits — new content goes to new version.

## 4. API Contracts / Data Models

- `GET /api/kb/modules` — list module + latest version
- `POST /api/kb/modules/{id}/versions` — create draft
- `POST /api/kb/versions/{id}/deploy` — deploy + reload agents
- `POST /api/kb/versions/{id}/rollback` — flip to previous deployed
- Tables: `kb_modules`, `kb_versions`, `kb_test_cases`.

## 5. Technical Constraints

- Embedding generated server-side via Semantic Kernel `ITextEmbeddingGeneration`.
- Vector ops via `IVectorStore` (Qdrant only — SQL Server has no native vector index).

## 6. Out of Scope

- Multi-language KB (only Chinese-Vietnamese pairing).
- Auto-ingest from external sources (manual upload only in Phase 1).

## 7. NFR

NFR-05 (accuracy ≥85% / alert on drop), NFR-01 (search p95 <200 ms).

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| Embedding API timeout | SK exception | "retry" toast | retry x3 with backoff |
| Index rebuild fails | bg job logs error | banner in admin | manual `REINDEX` script |
| Deploy fails mid-flight | atomic txn | rollback | none — txn rolls back |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| Embedding model choice (text-embedding-3-small vs Cohere) | P4 | T2 | open |
