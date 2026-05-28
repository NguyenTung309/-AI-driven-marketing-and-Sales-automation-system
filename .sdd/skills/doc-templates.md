---
skill: doc-templates
agents: [Agent-Docs]
owner: P3 Sales Lead + BGĐ
updated: 2026-05-27
---

# Skill — Document Templates

## Template catalog (1-1 với `document_templates.code`)

| Code | Doc type | Use |
|---|---|---|
| QUOTE-V1 | quote | Báo giá cá nhân hóa theo mục tiêu khách |
| BROCHURE-HSK | brochure | Brochure chương trình HSK |
| BROCHURE-KIDS | brochure | YCT trẻ em |
| SLIDE-DEMO-5 | slide | 5 trang slide demo buổi học thử |
| ONBOARDING-KIT | onboarding | Kit học viên mới |

## Required fields per template

- **QUOTE-V1**: `customer_name`, `goal`, `course_path`, `duration_months`, `total_price`, `discount`, `offer_valid_until`.
- **BROCHURE-HSK**: `customer_name?`, `target_level (3/4/5/6)`, `expected_pass_rate`.
- **SLIDE-DEMO-5**: `customer_name`, `teacher_name`, `lesson_topic`, `homework`.

## Rendering pipeline

1. API receive `(contact_id, template_code, vars)`
2. Enqueue render job → MinIO storage
3. Return signed URL `expires_in: 7d`

## Brand
- Logo + màu chính + font theo `Admin → Brand config` (SW-076).
- Footer: số ĐT trung tâm + Zalo OA QR.

## Guarantees
- KHÔNG render khi `document_templates.deleted_at IS NOT NULL`.
- Validate required fields trước khi enqueue (422 nếu thiếu).
