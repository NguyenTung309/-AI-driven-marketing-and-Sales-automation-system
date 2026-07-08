# Chan doan Pancake stale: so sanh v2 conversations (list) voi v1 messages (chi tiet).
# Dung khi UI thieu tin nhan moi — xac dinh tang nao cua Pancake tra du lieu cu.
#   .\diagnose_pancake_stale.ps1 -PageId 573921789992601632 -Token <page_access_token> [-ConversationId <id>]
param(
    [Parameter(Mandatory = $true)][string]$PageId,
    [Parameter(Mandatory = $true)][string]$Token,
    [string]$ConversationId
)

$ErrorActionPreference = "Stop"

Write-Host "=== v2 conversations (list endpoint — poller gate doc updated_at/snippet tu day) ==="
$conv = Invoke-RestMethod "https://pages.fm/api/public_api/v2/pages/$PageId/conversations?page_access_token=$Token&per_page=10"
foreach ($c in $conv.conversations) {
    "{0}`n  updated_at = {1} (UTC)  message_count = {2}`n  snippet    = {3}" -f $c.id, $c.updated_at, $c.message_count, (($c.snippet -replace "`r?`n", " ") | Out-String).Trim()
}

$target = if ($ConversationId) { $ConversationId } else { $conv.conversations[0].id }
Write-Host ""
Write-Host "=== v1 messages (chi tiet) cho $target ==="
$msgs = Invoke-RestMethod "https://pages.fm/api/public_api/v1/pages/$PageId/conversations/$target/messages?page_access_token=$Token&limit=15"
foreach ($m in $msgs.messages) {
    "{0}  from={1}  {2}" -f $m.inserted_at, $m.from.name, (($m.message -replace "`r?`n", " ") | Out-String).Trim()
}

Write-Host ""
Write-Host "Ket luan:"
Write-Host " - Messages co tin MOI HON updated_at cua list  -> list endpoint stale; sweep 60s cua poller se ingest duoc."
Write-Host " - Messages CUNG thieu tin da thay tren Zalo    -> Pancake chua ingest; loi hoan toan phia Pancake, he thong minh khong cuu duoc."
