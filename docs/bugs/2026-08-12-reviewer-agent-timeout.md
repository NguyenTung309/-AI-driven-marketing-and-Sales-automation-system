# Bug: reviewer-agent timeout - tất cả task review đều fail

**Ngày phát hiện**: 2026-08-12  
**Mức độ**: CRITICAL - Blocking toàn bộ content review workflow  
**Ảnh hưởng**: 
- Orchestrator plan có task review_agent → timeout
- Content tạo từ brief → fail ở bước review
- Nội dung chờ duyệt → button "Duyệt phát hành", "Lưu sửa đổi" không mở được
- Lịch tự động tạo content → bị chặn ở review

## Triệu chứng

1. **UI**: "Lý do agent review: reviewer_error" / "Lý do duyệt phát hành: agent_non_pass"
2. **Orchestrator**: Task `review_agent` bị timeout/fail
3. **Log**: Warning lặp lại liên tục:
   ```
   [WRN] LLM config fallback for agent reviewer-agent tenant "c28c58f3-9870-4000-bdef-87c8b25cad6c": 
   bound config "ae1aa695-af6b-451b-af9b-24dc761d50f2" is inactive/missing, 
   using active config "d95289b1-1606-43d7-b5a0-5d24fafa48fb"
   ```

## Root Cause

**reviewer-agent đang dùng LLM config fallback có vấn đề**:

1. Config bind ban đầu `ae1aa695-af6b-451b-af9b-24dc761d50f2` đã bị tắt/xóa
2. `LlmConfigResolver` fallback sang config active cũ nhất: `d95289b1-1606-43d7-b5a0-5d24fafa48fb`
3. Config fallback này có một trong các vấn đề:
   - API key sai/hết hạn/thiếu
   - Base URL không đúng (proxy/gateway đã đổi)
   - Timeout quá thấp (< 20s cho review gate)
   - Model không tồn tại trên provider
   - Provider không support model

4. LLM call throw exception → `ContentReviewer` catch và trả về `reviewer_error`:
   ```csharp
   catch (Exception ex) when (ex is not OperationCanceledException)
   {
       return FailedOutcome("reviewer_unavailable: " + ex.Message);
   }
   ```

5. `ContentReviewCoordinator` nhận kết quả fail → ghi `reviewer_error` vào DB

## Code Paths

**ContentReviewer.cs:228-238**:
```csharp
if (_reviewClientFactory is null || _llmConfigResolver is null)
{
    return FailedOutcome("reviewer_not_configured");
}

using var _ = _llmScope.Begin(tenantId, AgentCode);
var config = await _llmConfigResolver.ResolveAsync(tenantId, AgentCode, ct); // Fallback ở đây
var client = _reviewClientFactory.Create(config); // OK
// LLM call sau đây throw exception
var envelope = await client.CompleteTextAsync(...);
```

**LlmConfigResolver.cs:62-76**:
```csharp
if (cfg is null) // Config bind đã tắt
{
    // Fallback config active cũ nhất
    cfg = await db.LlmConfigs
        .Where(c => c.TenantId == tenantId && c.IsActive)
        .OrderBy(c => c.CreatedAt)
        .FirstOrDefaultAsync(ct);
    
    fellBack = true;
    LogFallback(_logger, agentCode, tenantId, configId.Value, cfg.Id); // Warning trong log
}
```

## Khẩn cấp - Fix ngay

### Option 1: Bật lại config cũ (nếu chỉ bị tắt nhầm)

```sql
UPDATE dbo.llm_configs 
SET is_active = 1,
    updated_at = SYSDATETIMEOFFSET()
WHERE id = 'ae1aa695-af6b-451b-af9b-24dc761d50f2'
  AND tenant_id = 'c28c58f3-9870-4000-bdef-87c8b25cad6c';
```

### Option 2: Bind reviewer-agent sang config active khác

```sql
DECLARE @TenantId UNIQUEIDENTIFIER = 'c28c58f3-9870-4000-bdef-87c8b25cad6c';
DECLARE @NewConfigId UNIQUEIDENTIFIER;

-- Tìm config active tốt (có API key, provider anthropic/openai)
SELECT TOP 1 @NewConfigId = id
FROM dbo.llm_configs
WHERE tenant_id = @TenantId
  AND is_active = 1
  AND provider IN ('anthropic', 'openai', 'openai-compatible')
  AND api_key_encrypted IS NOT NULL
  AND base_url IS NOT NULL
ORDER BY created_at DESC; -- Mới nhất

-- Update binding
UPDATE dbo.agent_configs
SET llm_config_id = @NewConfigId,
    updated_at = SYSDATETIMEOFFSET()
WHERE tenant_id = @TenantId
  AND code = 'reviewer-agent'
  AND deleted_at IS NULL;

-- Nếu không có trong agent_configs, update agent_definitions
UPDATE dbo.agent_definitions
SET llm_config_id = @NewConfigId,
    updated_at = SYSDATETIMEOFFSET()
WHERE tenant_id = @TenantId
  AND code = 'reviewer-agent'
  AND deleted_at IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.agent_configs 
      WHERE tenant_id = @TenantId AND code = 'reviewer-agent' AND deleted_at IS NULL
  );
```

### Option 3: Sửa config fallback

```sql
-- Kiểm tra config fallback hiện tại
SELECT id, name, provider, model_id, base_url, timeout_seconds, max_output_tokens
FROM dbo.llm_configs
WHERE id = 'd95289b1-1606-43d7-b5a0-5d24fafa48fb'
  AND tenant_id = 'c28c58f3-9870-4000-bdef-87c8b25cad6c';

-- Nếu timeout quá thấp, tăng lên
UPDATE dbo.llm_configs
SET timeout_seconds = 120, -- Review gate cần ít nhất 60s
    updated_at = SYSDATETIMEOFFSET()
WHERE id = 'd95289b1-1606-43d7-b5a0-5d24fafa48fb';

-- Nếu base_url sai, sửa lại
-- UPDATE dbo.llm_configs SET base_url = 'https://api.anthropic.com' ...
```

## Verify Fix

1. **Chạy một trong 3 fix SQL ở trên**

2. **Restart AgentService** (để clear cache nếu có):
   ```bash
   # Windows
   taskkill /F /IM Clawbot.AgentService.exe
   # Hoặc restart service
   ```

3. **Test ngay từ UI**:
   - Vào /agents → tạo plan mới có task review_agent
   - Hoặc vào /content → tạo brief → Generate content
   - Hoặc vào /content/schedules → chạy lịch tự động

4. **Kiểm tra log** (không còn fallback warning):
   ```bash
   tail -f src/agents/Clawbot.AgentService/logs/agent-YYYYMMDD.log | grep reviewer-agent
   ```

5. **Kiểm tra DB** (task review thành công):
   ```sql
   SELECT TOP 5
       crt.id,
       crt.status,
       crt.failure_reason,
       ci.agent_review_status,
       ci.agent_review_reason_code,
       crt.created_at,
       crt.completed_at
   FROM dbo.content_review_tasks crt
   INNER JOIN dbo.content_items ci ON ci.id = crt.content_item_id
   WHERE crt.tenant_id = 'c28c58f3-9870-4000-bdef-87c8b25cad6c'
   ORDER BY crt.created_at DESC;
   ```

   Expect: `status = 'completed'`, `agent_review_status = 'passed'` hoặc `'needs_human'`

## Long-term Fix

### 1. Thêm health check cho LLM config

```csharp
// Infrastructure/Agents/LlmConfigHealthCheck.cs
public class LlmConfigHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(...)
    {
        // Test API key bằng cách gọi model list hoặc echo simple prompt
        // Nếu fail → report Degraded với detail config ID
    }
}
```

### 2. Alert khi config fallback

```csharp
// LlmConfigResolver.cs:75 - thêm metric/alert
if (fellBack)
{
    LogFallback(_logger, agentCode, tenantId, configId.Value, cfg.Id);
    
    // TODO: Emit metric để alert
    // _metrics?.RecordConfigFallback(tenantId, agentCode, configId.Value);
}
```

### 3. UI warning khi agent bind config đã tắt

Trong admin UI /settings/agents, hiện warning nếu:
- `agent_configs.llm_config_id` trỏ config `is_active = 0`
- Hoặc `agent_definitions.llm_config_id` trỏ config đã xóa

### 4. Validation khi tắt config

Trước khi cho phép admin tắt config, check:
```sql
-- Liệt kê agents đang dùng config này
SELECT code FROM agent_configs WHERE llm_config_id = @ConfigId AND deleted_at IS NULL
UNION
SELECT code FROM agent_definitions WHERE llm_config_id = @ConfigId AND deleted_at IS NULL;
```

Nếu có agent đang bind → show modal cảnh báo + yêu cầu bind agents sang config khác trước.

## Files liên quan

- `src/agents/Clawbot.Agents.Core/Content/ContentReviewer.cs:228-238` - Fail-closed check
- `src/agents/Clawbot.Agents.Core/Content/ContentReviewer.cs:330-337` - Exception catch
- `src/shared/Clawbot.Infrastructure/Agents/LlmConfigResolver.cs:62-76` - Fallback logic
- `src/agents/Clawbot.AgentService/Services/ContentReviewCoordinator.cs:198-214` - RunReviewAsync
- `src/agents/Clawbot.AgentService/Program.cs:153-161` - DI registration

## Script debug

```bash
# 1. Chạy debug query
sqlcmd -S localhost -d ClawbotDb -i scripts/debug-reviewer-config.sql

# 2. Chạy fix (chọn option phù hợp)
sqlcmd -S localhost -d ClawbotDb -i scripts/fix-reviewer-config-emergency.sql

# 3. Tail log realtime
tail -f src/agents/Clawbot.AgentService/logs/agent-$(date +%Y%m%d).log | grep -i review
```
