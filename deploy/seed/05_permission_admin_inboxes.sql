-- Seed permission admin:inboxes cho role Admin
-- Chay truoc khi deploy filter InboxMembers, neu khong admin cung bi giong sale
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM permissions WHERE code = 'admin:inboxes')
BEGIN
    INSERT INTO permissions (id, code, description)
    VALUES (NEWID(), 'admin:inboxes', 'Xem tat ca inbox va conversation');
END;

-- Chi seed cho role Admin, KHONG seed cho role Sale
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE r.name = 'Admin'
  AND p.code = 'admin:inboxes'
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

-- Assert: log danh sach role co quyen admin:inboxes
SELECT r.name AS RoleName, COUNT(*) AS HasPermission
FROM roles r
JOIN role_permissions rp ON rp.role_id = r.id
JOIN permissions p ON p.id = rp.permission_id AND p.code = 'admin:inboxes'
GROUP BY r.name;
COMMIT;
