-- Prompt chaining P6 (§4.7): bộ đếm vòng refine trên content_review_tasks. Reviewer reject kèm lý do => chạy lại
-- L3+L4 đúng 1 vòng cho mỗi revision (RefineAttemptCount==0 mới kích), vòng 2 vẫn reject => needs_human. Đếm trên
-- task (bền, chống restart) chứ không trong bộ nhớ tiến trình. Default 0. Một SqlCommand, không GO. An toàn chạy lại.
IF COL_LENGTH(N'dbo.content_review_tasks', N'refine_attempt_count') IS NULL
    ALTER TABLE dbo.content_review_tasks ADD refine_attempt_count INT NOT NULL CONSTRAINT DF_content_review_tasks_refine_attempt_count DEFAULT 0;
