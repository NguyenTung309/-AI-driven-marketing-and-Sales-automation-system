# SPEC-XX — <Feature title>

Status: `DRAFT | REVIEW | APPROVED | IMPLEMENTING | DONE`
Spec lead: `<name>`
Last updated: `YYYY-MM-DD`
Traces to: FR-XX, UC-Yzz, SW-zzz

---

## 1. Business Context

Why this exists. What problem it solves. Who feels the pain. Quantify if possible.

## 2. User Stories

- AS A `<role>` I WANT `<capability>` SO THAT `<benefit>`.

## 3. Acceptance Criteria (EARS Notation)

Use 5 patterns; one criterion per line.

- **Ubiquitous**: `THE SYSTEM SHALL <requirement>.`
- **Event**: `WHEN <trigger> THE SYSTEM SHALL <action>.`
- **State**: `WHILE <state> THE SYSTEM SHALL <restriction>.`
- **Optional**: `WHERE <feature included> THE SYSTEM SHALL <behavior>.`
- **Unwanted**: `IF <unwanted condition> THEN THE SYSTEM SHALL <safe response>.`

## 4. API Contracts / Data Models

- REST endpoints, gRPC methods, message contracts
- Reference tables in `deploy/migrations/0001_init.sql`

## 5. Technical Constraints

- Performance budgets, latency targets
- Dependencies (libraries, services)
- Compatibility (versions, OS, browser)

## 6. Out of Scope

Explicit list of what this spec does NOT cover. Reference future SPECs by id.

## 7. Non-Functional Requirements

Map to project NFR-01..NFR-05 from `docs/spec-audit.md`.

## 8. Error Handling Matrix

| Error condition | Detection | User-visible response | Recovery |
|---|---|---|---|
| ... | ... | ... | ... |

## 9. Open Questions

Item | Owner | Due | Status
---|---|---|---
