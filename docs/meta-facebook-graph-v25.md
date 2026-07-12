# Tích hợp Meta Graph API v25.0

Tài liệu này mô tả luồng Facebook Login for Business dùng cho đăng bài Facebook Page và Meta Ads trong ClawBot. Inbox Facebook vẫn đi qua Pancake; các Page Access Token trong màn quản lý inbox không thuộc luồng Graph API này. ClawBot không yêu cầu nhập Page Access Token thủ công.

## 1. Kiến trúc token

ClawBot hỗ trợ hai chế độ, cùng dùng một **Facebook Login for Business Configuration ID** nhưng khác loại token:

| Chế độ trên ClawBot | Loại token trong Meta Configuration | Dùng khi nào | Business Webhook |
|---|---|---|---|
| `development_user` | **User access token** | Test localhost bằng tài khoản có vai trò trong App và quản trị Page của chính mình | Không cần |
| `business_system_user` | **System-user access token** | Production, tác vụ nền dài hạn, triển khai cho doanh nghiệp | Nên cấu hình |

Luồng chung:

1. Admin tenant bấm **Kết nối Meta** trong `/system`.
2. Backend tạo OAuth `state` ngẫu nhiên, chỉ lưu SHA-256 của state và cho state hết hạn sau 10 phút.
3. Meta trả về authorization code tại `/api/admin/meta/callback`.
4. Backend đổi code lấy token bằng server-to-server call.
5. Backend kiểm tra token bằng `/debug_token`, xác nhận đúng App ID và đúng loại token đã chọn. Chế độ production kiểm tra thêm `client_business_id` từ `/me`.
6. Backend gọi `/me/accounts` để đồng bộ các Page, Page token và danh sách task được cấp.
7. Root token và Page token được mã hóa trước khi lưu database; frontend không nhận lại token.

User access token ở chế độ phát triển vẫn có hạn theo thời gian Meta trả về. Khi hết hạn, người dùng bấm **Kết nối lại Meta**; không phải sao chép rồi dán token thủ công. Chế độ này phù hợp cho giai đoạn phát triển, không nhằm thay thế token production dài hạn.

Business Integration System User access token (BISU) được Meta thiết kế cho tác vụ tự động server-to-server và có thể chọn **không hết hạn** nếu tài khoản/cấu hình được phép. Đây không phải refresh-token flow: không có cron đổi refresh token. Token vẫn có thể bị thu hồi khi doanh nghiệp gỡ ứng dụng, quyền/tài sản thay đổi hoặc Meta vô hiệu hóa token. ClawBot kiểm tra hằng ngày; khi token không còn hợp lệ, trạng thái chuyển thành `reconnect_required` và admin cấp quyền lại trên UI.

Page token được đồng bộ lại hằng ngày và được lấy lại một lần khi Graph trả lỗi token trong lúc đăng bài. Cách này tránh yêu cầu admin dán lại Page Access Token thủ công.

## 2. Tạo Meta App

### 2.1. Phát triển và test localhost

Trong bảng điều khiển Meta bằng giao diện tiếng Việt:

1. Mở App và để App ở chế độ **Phát triển**.
2. Thêm sản phẩm **Đăng nhập Facebook dành cho doanh nghiệp (Facebook Login for Business)**.
3. Vào sản phẩm trên > **Cấu hình**, tạo một Configuration dùng Authorization Code và chọn loại token **User access token**.
4. Chọn tối thiểu các quyền Page ở phần dưới và chỉ chọn Facebook Page; không cần chọn Instagram nếu chưa tích hợp Instagram.
5. Thêm tài khoản Facebook đang test vào **Vai trò trong ứng dụng** và bảo đảm tài khoản đó có **Toàn quyền kiểm soát** Page cần dùng.
6. Thêm `http://localhost:15873/api/admin/meta/callback` vào URL chuyển hướng OAuth hợp lệ của Configuration.
7. Sao chép ID của Configuration vào ô **Login for Business Configuration ID** trên ClawBot.

Với người dùng có vai trò trong App và tài sản do chính họ quản trị, bạn có thể test khi App còn ở chế độ Phát triển; không cần Business Webhook và thường chưa cần xác minh giấy tờ doanh nghiệp/App Review. Tài khoản hoặc Page không đủ hai điều kiện trên có thể không xuất hiện trong màn chọn tài sản dù tài khoản đang là admin Page.

### 2.2. Production

Khi triển khai cho doanh nghiệp hoặc người dùng ngoài danh sách vai trò của App:

1. Tạo app loại **Business** và liên kết app với business portfolio do đơn vị phát triển sở hữu.
2. Thêm sản phẩm **Facebook Login for Business**.
3. Tạo một Configuration, chọn token loại **System-user access token** và Authorization Code grant.
4. Chọn thời hạn token **Never expire** cho tác vụ nền nếu chính sách tài khoản cho phép.
5. Chọn đúng tài sản mà ClawBot cần: Facebook Pages và ad accounts. Chỉ xin quyền tối thiểu cần dùng.
6. Copy Configuration ID vào form **Cấu hình Meta App** trong `/system`.
7. Thêm chính xác callback URL vào danh sách Valid OAuth Redirect URIs. URL phải trùng giá trị **OAuth Callback URL** đã lưu trên UI.

Meta yêu cầu người cài đặt chấp thuận toàn bộ quyền mà Configuration yêu cầu; nếu bỏ một quyền, app có thể không nhận được quyền nào cho lần cài đặt đó. Vì vậy nên tách Configuration nếu các nhóm khách hàng cần tập quyền khác nhau.

Quyền tối thiểu cho đăng Page thường gồm:

- `pages_show_list`
- `pages_read_engagement`
- `pages_manage_posts`
- `pages_manage_metadata` nếu cấu hình/luồng Page cần quản lý metadata

Nếu bật Meta Ads automation, thêm các quyền thực sự dùng như `ads_read`, `ads_management` và `business_management`. Configuration phải cấp Page task `CREATE_CONTENT`; Page không có task này sẽ không xuất hiện trong danh sách đích đăng bài.

App phục vụ doanh nghiệp không thuộc sở hữu của bạn phải hoàn tất Business Verification và được duyệt Advanced Access/App Review cho các quyền tương ứng. Meta cũng yêu cầu Ongoing Review đối với app có Advanced Access. Đây là yêu cầu của luồng production, không phải điều kiện để thử chế độ `development_user` với tài khoản/App/Page của bạn.

## 3. Cấu hình trong giao diện quản trị

Vào `/system` > **Kênh đăng bài** > **Meta Facebook**, nhập và lưu:

- Chế độ kết nối: **Phát triển / kiểm thử** hoặc **Production / tác vụ nền**
- Meta App ID
- Meta App Secret
- Facebook Login for Business Configuration ID
- Business Webhook Verify Token, chỉ hiện ở chế độ production
- OAuth Callback URL
- URL quay về giao diện sau OAuth

App Secret, mode và webhook verify token được mã hóa trong `social_credentials` theo từng tenant và không được trả lại frontend. Khi để trống ô secret ở lần cập nhật sau, ClawBot giữ nguyên giá trị đã lưu. Đổi mode làm kết nối hiện tại chuyển sang `reconnect_required`, vì Configuration phải cấp đúng loại token tương ứng.

Các biến môi trường sau chỉ còn là bootstrap fallback cho tenant chưa lưu cấu hình trên UI:

```dotenv
Meta__Graph__AppId=your-meta-app-id
Meta__Graph__AppSecret=your-meta-app-secret
Meta__Graph__ConfigurationId=your-login-for-business-configuration-id
Meta__Graph__AuthorizationMode=development_user
Meta__Graph__WebhookVerifyToken=your-random-webhook-verify-token
Meta__Graph__RedirectUri=https://api.example.com/api/admin/meta/callback
Meta__Graph__FrontendReturnUrl=https://app.example.com/system
Meta__Graph__ApiVersion=v25.0
```

Không đưa secret vào URL public, log hoặc commit Git. Cấu hình lưu từ UI được ưu tiên hơn fallback môi trường.

Chỉ với mode `business_system_user`, đăng ký Application Webhook tại URL `https://api.example.com/webhooks/meta/business-integration`, dùng đúng `Meta__Graph__WebhookVerifyToken`, và subscribe ba field: `business_integration_install`, `business_integration_update`, `business_integration_uninstall`. ClawBot xác thực `X-Hub-Signature-256`, đưa việc xử lý vào Hangfire rồi đồng bộ hoặc khóa kết nối theo thời gian thực; lịch kiểm tra hằng ngày vẫn là lớp dự phòng. Mode `development_user` không dùng endpoint này.

Nếu reverse proxy của môi trường production bắt buộc Webhooks mTLS, trust root CA mới của Meta theo changelog v25.0; CA cũ không còn đủ cho luồng webhook sau đợt chuyển đổi ngày 31/03/2026.

Meta Ads automation có công tắc riêng:

```dotenv
Ads__Meta__Enabled=true
```

Kết nối Meta không tự bật các hành động Ads. Khi `Ads__Meta__Enabled=false`, connector không đọc hoặc thay đổi campaign dù tenant đã kết nối Meta. Graph base URL và version dùng chung từ `Meta__Graph__BaseUrl`/`Meta__Graph__ApiVersion`, tránh để cấu hình Page và Ads lệch phiên bản.

## 4. Vận hành trong ClawBot

1. Chạy `run-all.bat`; migration `0055` được áp dụng tự động cho cả database cũ.
2. Vào `/system`, lưu **Cấu hình Meta App** trên giao diện.
3. Bấm **Kết nối Meta**.
4. Ở mode phát triển, đăng nhập bằng tài khoản có vai trò trong App rồi chọn Page cần test. Ở mode production, chọn business portfolio, Pages, ad accounts và cấp đủ quyền.
5. Sau khi quay lại ClawBot, kiểm tra danh sách Page và chọn Page mặc định.
6. Khi lên lịch nội dung Facebook, chọn Page đích. Schedule lưu `meta_asset_id`, vì vậy job nền luôn đăng đúng Page của tenant.
7. Dùng **Kiểm tra token** hoặc **Đồng bộ Pages** khi vừa thay đổi tài sản/quyền ở Meta.

Nút **Ngắt kết nối** xóa token khỏi ClawBot. Để thu hồi hoàn toàn ở phía Meta, gỡ ứng dụng trong phần cài đặt Facebook/Meta tương ứng.

Không cần migration DB mới cho hai mode: cấu hình mode nằm trong JSON đã mã hóa của `social_credentials`, còn loại token dùng cột `meta_connections.token_type` đã có trong migration `0055`. Cấu hình cũ chưa có mode được hiểu là `business_system_user` để giữ nguyên hành vi production.

## 5. Bảo mật và xử lý lỗi

- Mọi Graph call server-to-server kèm `appsecret_proof`, là HMAC-SHA256 của access token với App Secret làm khóa.
- Logging mặc định của `HttpClientFactory` bị tắt riêng cho Meta client để code, access token và App Secret không lọt vào log qua query string OAuth/debug-token.
- Token được mã hóa bằng `IEncryptor`; API status chỉ trả ID, tên, task, thời hạn và trạng thái.
- OAuth callback dùng state một lần, có hạn 10 phút và không phụ thuộc cookie frontend.
- Lỗi Graph code `190` hoặc các subcode token phổ biến kích hoạt đồng bộ token một lần; nếu vẫn lỗi, tenant phải kết nối lại.
- Không tự retry POST đăng bài vì retry mù có thể tạo bài trùng. Usage headers của Meta được ghi ở mức Debug để theo dõi quota.
- Chỉ Page có task `CREATE_CONTENT` mới được dùng làm publish target.

## 6. Graph API v25.0

Graph API v25.0 được Meta phát hành ngày 18/02/2026. ClawBot cố định toàn bộ call Page publishing và Marketing API mới ở `/v25.0`.

Thay đổi v25 đáng chú ý với phần Ads là không còn cho tạo, nhân bản hoặc cập nhật cấu trúc Advantage+ Shopping Campaign và Advantage+ App Campaign cũ. Nếu ClawBot bổ sung luồng tạo campaign, phải dùng cấu trúc Advantage+ Campaign mới. Các call hiện tại chỉ đọc insight và áp dụng hành động lên campaign có sẵn.

## 7. Tài liệu Meta chính thức

- Facebook Login for Business: https://developers.facebook.com/documentation/facebook-login/facebook-login-for-business
- Secure Graph API calls và `appsecret_proof`: https://developers.facebook.com/docs/graph-api/guides/secure-requests/
- Debug Token reference v25.0: https://developers.facebook.com/docs/graph-api/reference/v25.0/debug_token
- Business Integration Webhooks: https://developers.facebook.com/documentation/facebook-login/facebook-login-for-business/integration-webhooks
- Pages API getting started: https://developers.facebook.com/documentation/pages-api/getting-started
- Pages API posts/photos: https://developers.facebook.com/documentation/pages-api/posts
- Graph API v25.0 changelog: https://developers.facebook.com/docs/graph-api/changelog/version25.0/
