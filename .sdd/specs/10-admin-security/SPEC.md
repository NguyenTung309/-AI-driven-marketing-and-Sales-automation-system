# SPEC-10 — Admin & Security

Status: `DRAFT`
Traces to: FR-10, UC-J02..J06, SW-001..010, SW-097..106

## 1. Business Context

RBAC, 2FA, audit log, backup, secret mgmt, env config.

## 2. User Stories

- AS AN Admin I WANT RBAC role+permission editor tenant-scoped SO THAT a "QA" role can edit KB but not delete agents.
- AS A Compliance reviewer I WANT every state change in an audit log SO THAT I can answer "who changed what when".

## 3. Acceptance Criteria (EARS)

- THE SYSTEM SHALL enforce login via JWT with TTL 15 min + refresh token httpOnly.
- WHEN login fails 5 times THE SYSTEM SHALL lock account for 30 min.
- THE SYSTEM SHALL support 2FA TOTP per user (optional in Phase 1, required for `admin` role).
- THE SYSTEM SHALL append-write to `audit_logs` on every authn, role change, KB deploy, agent start/stop, ads action.
- WHILE a tenant `is_active = false` THE SYSTEM SHALL reject all API calls except `superadmin.support`.
- IF a request lacks tenant claim THEN THE SYSTEM SHALL respond 401.

## 4. API Contracts / Data Models

- `POST /auth/login`, `POST /auth/refresh`, `POST /auth/2fa/verify`
- `GET /api/admin/roles`, `POST /api/admin/roles/{id}/permissions`
- `GET /api/admin/audit?from=&action=`
- Tables: `users`/Identity, `roles`, `permissions`, `role_permissions`, `audit_logs`, `api_keys`, `tenants`.

## 5. Technical Constraints

- Permissions enforced at handler boundary via policy `RequirePermission("kb.write")`.
- Audit log writes batched within request scope, flushed on commit.

## 6. Out of Scope

- SSO / SAML / OIDC (Phase 2).

## 7. NFR

NFR-03 (TLS, encryption, 30-day retention), NFR-02 (auth uptime ≥99.5%).

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| Token expired | 401 | redirect to login | refresh flow |
| 2FA wrong | 401 with code | "wrong code" | retry x3 then lock |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| Identity custom-table mapping | P1 | T1 | open |
