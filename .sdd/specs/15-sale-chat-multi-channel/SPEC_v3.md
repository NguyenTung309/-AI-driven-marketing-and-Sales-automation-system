# SPEC v3 — Channel Auto-Naming from Pancake API

## Overview

Khi admin tao channel moi (Zalo/FB), thay vi phai nhap tay "Ten kenh" (VD: "Sale A"),
backend tu dong lay ten chu kenh tu Pancake API va set lam Inbox.Name.

Hien tai ChannelCard hien:
- Ten kenh = "Sale A" (admin nhap tay)
- Sale phu trach = "Sale A" (memberDisplayName) 
- Dong duoi: "zalo · pzl_13497..." → can bo pzl_id, chi giu platform

## Flow

### Tao channel moi

Admin dien form:
1. Platform: dropdown [Zalo | Facebook]
2. External Page ID: text input (VD: "pzl_134970094277281958")
3. Page Access Token: password input (Pancake JWT token)
4. Click [Tao kenh]

Backend xu ly:
1. Validate token (JWT decode, kiem tra id claim == externalPageId)
2. Goi GET /v2/pages/{pageId}/conversations?per_page=1 bang token nay
3. Parse response tim last_sent_by.name WHERE last_sent_by.id == page_id  
4. Neu co → dung lam Inbox.Name
5. Neu khong (page chua co hoi thoai) → fallback "Zalo OA - {pageId}"
6. Luu Inbox + EncryptedAccessToken

### Hien thi ChannelCard

```
[icon] Le Minh Thang     ← Inbox.Name (tu Pancake)
       Sale A            ← memberDisplayName (hoac "Chua gan")
       zalo              ← platform (bo externalPageId)
```

## API changes

### POST /api/admin/inboxes
Hien tai:
```json
{ "platform": "zalo", "externalPageId": "...", "name": "Sale A", "pageAccessToken": "..." }
```
Sua thanh (bo name):
```json
{ "platform": "zalo", "externalPageId": "...", "pageAccessToken": "..." }
```

### Frontend ChannelManagementPage

Bo input "Ten kenh". Giu:
- Platform dropdown
- External Page ID text input  
- Page Access Token password input (co eye toggle)
- Button [Tao kenh]

### ChannelCard  
Bo dong {channel.externalPageId}. Chi hien platform.

### GET /api/inbox/channels
Khong can sua API — Inbox.Name da duoc set dung tu Pancake khi tao.

## Data flow

```
ChannelManagementPage
  | POST /api/admin/inboxes { platform, externalPageId, pageAccessToken }
  v
AdminInboxEndpoints.CreateAsync
  | 1. Decode JWT -> pageId tu claim "id"
  | 2. Validate pageId == externalPageId  
  | 3. GET /v2/pages/{pageId}/conversations?per_page=1
  | 4. Parse: last_sent_by.name WHERE last_sent_by.id == pageId -> pageName
  | 5. Fallback: "Zalo OA - {pageId}"
  | 6. Create Inbox { Name=pageName, Platform, ExternalPageId, EncryptedAccessToken }
  v
ChannelCard hien thi pageName
```

## Non-goals

- Khong sua PancakePollingService (da co o v2)
- Khong sua webhook path  
- Khong sua hien thi conversation (da co o v2)
- Khong sua Inbox.Name sau khi tao (chi set 1 lan luc tao)

## Known improvements (defer)

- **HttpClientFactory**: FetchPageNameAsync dang tao `new HttpClient()` moi moi lan goi. Can inject `IHttpClientFactory` de tranh ton tai nguyen.
- **Button disabled**: Form tao channel can check `!createForm.pageAccessToken` de khoa button khi token trong.

