# Tài liệu Đặc tả Use Case Hệ thống (System Use Case Specification)

Tài liệu này chuẩn hóa toàn bộ 73 Use Cases của hệ thống thành 9 Phân hệ (Sub-systems) chính. Tài liệu tuân thủ nghiêm ngặt các nguyên tắc UML Use Case Diagram và Domain-Driven Design (DDD) để bất kỳ ai cũng có thể tự vẽ lại sơ đồ Use Case chính xác.

## Danh sách Tổng hợp 73 Use Cases (Master Use Case List)

| Mã UC | Tên Use Case | Phân hệ |
|---|---|---|
| UC-01 | Đăng nhập hệ thống (System Login) | Phân hệ 1: Quản lý Định danh & Phân quyền |
| UC-02 | Xác thực hai yếu tố (Two-Factor Authentication (2FA)) | Phân hệ 1: Quản lý Định danh & Phân quyền |
| UC-03 | Khôi phục mật khẩu (Password Recovery) | Phân hệ 1: Quản lý Định danh & Phân quyền |
| UC-04 | Quản lý tài khoản nhân viên (Employee Account Management) | Phân hệ 1: Quản lý Định danh & Phân quyền |
| UC-05 | Cấu hình phân quyền RBAC (Role-Based Access Control) | Phân hệ 1: Quản lý Định danh & Phân quyền |
| UC-06 | Quản lý khóa API (API Keys Management) | Phân hệ 1: Quản lý Định danh & Phân quyền |
| UC-07 | Cấu hình chính sách hệ thống (System Settings Configuration) | Phân hệ 9: Giám sát, Cảnh báo & Vận hành Hệ thống |
| UC-08 | Tra cứu nhật ký hệ thống (Audit Log Lookup) | Phân hệ 9: Giám sát, Cảnh báo & Vận hành Hệ thống |
| UC-09 | Cấu hình liên kết Pancake (Pancake Connection Configuration) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-10 | Tiếp nhận tin nhắn đa kênh (Multi-channel Message Reception) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-11 | Tự động phân loại ý định (Automatic Intent Classification) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-12 | Xem hộp thư tập trung (View Unified Inbox) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-13 | Bộ lọc hội thoại nâng cao (Advanced Conversation Filter) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-14 | Phân bổ/Chuyển giao hội thoại (Conversation Assignment) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-15 | Gửi tin nhắn phản hồi (Send Reply Message) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-16 | Cập nhật trạng thái hội thoại (Change Conversation Status) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-17 | Tự động phản hồi ngoài giờ (Automatic Out-of-Hours Reply) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-18 | Nhận diện tin nhắn trùng lặp (Identify & Merge Conversations) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-19 | Quản lý nhóm tri thức (Knowledge Module Management) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-20 | Xem lịch sử phiên bản tri thức (KB Version Tracking) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-21 | Triển khai và đồng bộ tri thức (Deploy & Synchronize Vector) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-22 | Khôi phục phiên bản tri thức cũ (Restore Previous KB Version) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-23 | Quản lý kịch bản kiểm thử tri thức (KB Test-case Suite Management) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-24 | Chạy kiểm tra độ chính xác tri thức (Run Accuracy Test) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-25 | Chatbot tư vấn tri thức tự động (Automatic Consulting RAG Chatbot) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-26 | Cấu hình Model AI & Prompt (AI Model & Prompt Configuration) | Phân hệ 4: Quản trị Agent & Phân bổ tài nguyên AI |
| UC-27 | Quản lý vận hành Agent (AI Agent Operation Management) | Phân hệ 4: Quản trị Agent & Phân bổ tài nguyên AI |
| UC-28 | Giám sát nhật ký hoạt động Agent (Real-time Logs Monitoring) | Phân hệ 4: Quản trị Agent & Phân bổ tài nguyên AI |
| UC-29 | Quản lý số dư và hạn mức Token (Token Tracking & Management) | Phân hệ 4: Quản trị Agent & Phân bổ tài nguyên AI |
| UC-30 | Gợi ý soạn thảo phản hồi bằng AI (AI Reply Draft Suggestion) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-31 | Tóm tắt cuộc hội thoại bằng AI (AI Conversation Summary) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-32 | Xem thanh bên bối cảnh Lead (Context Sidebar Lead) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-33 | Gợi ý bán thêm sản phẩm bằng AI (AI Product Upsell Suggestion) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-34 | Thư viện mẫu câu trả lời nhanh (Quick Reply Template Library) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-35 | Cảnh báo quá hạn phản hồi SLA (SLA Wait Time Alert) | Phân hệ 9: Giám sát, Cảnh báo & Vận hành Hệ thống |
| UC-36 | Cấu hình quy tắc chấm điểm Lead (Scoring Rule Configuration) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-37 | Xem danh sách Lead dạng Kanban (View Kanban Lead List) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-38 | Quản lý chi tiết hồ sơ Lead (Detailed Lead Profile Management) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-39 | Gộp thông tin Lead trùng lặp (Merge Duplicate Lead Info) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-40 | Thiết lập chiến dịch Drip (Drip Campaign Setup) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-41 | Tự động gửi tin chăm sóc Drip (Automatic Drip Sequence Sending) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-42 | Tích hợp Widget Chat lên Website (Chat Widget Integration) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-43 | Tích hợp trang FAQ hỗ trợ (Support FAQ Page Integration) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-44 | Quét tìm xu hướng nội dung (Content Trend Scanning) | Phân hệ 7: Tiếp thị Nội dung & Chiến dịch Quảng cáo |
| UC-45 | Quản lý Brief nội dung (Content Brief Management) | Phân hệ 7: Tiếp thị Nội dung & Chiến dịch Quảng cáo |
| UC-46 | Tự động viết nháp bài viết bằng AI (Automatic Content Drafting) | Phân hệ 7: Tiếp thị Nội dung & Chiến dịch Quảng cáo |
| UC-47 | Xem lịch xuất bản nội dung (View Content Calendar) | Phân hệ 7: Tiếp thị Nội dung & Chiến dịch Quảng cáo |
| UC-48 | Tự động đăng bài theo lịch trình (Automatic Post Scheduling) | Phân hệ 7: Tiếp thị Nội dung & Chiến dịch Quảng cáo |
| UC-49 | Quản lý mẫu tài liệu (Doc Template Library Management) | Phân hệ 6: Tài liệu & PDF Tự động |
| UC-50 | Tự động sinh tài liệu PDF báo giá (Automatic PDF Quote Generation) | Phân hệ 6: Tài liệu & PDF Tự động |
| UC-51 | Xem và tải về tài liệu (View and Preview Document) | Phân hệ 6: Tài liệu & PDF Tự động |
| UC-52 | Gửi tài liệu và theo dõi lượt mở (Send Document & Track) | Phân hệ 6: Tài liệu & PDF Tự động |
| UC-53 | Báo cáo chỉ số KPI đa kênh (KPI Report Dashboard) | Phân hệ 8: Báo cáo Thống kê & Phân tích KPI |
| UC-54 | Báo cáo chuyển đổi phễu (Funnel Conversion Report) | Phân hệ 8: Báo cáo Thống kê & Phân tích KPI |
| UC-55 | Báo cáo hiệu suất hoạt động Sales & AI (Sale & AI Performance Report) | Phân hệ 8: Báo cáo Thống kê & Phân tích KPI |
| UC-56 | Cấu hình lịch gửi báo cáo (Report Sending Schedule Setup) | Phân hệ 8: Báo cáo Thống kê & Phân tích KPI |
| UC-57 | Xuất báo cáo dữ liệu CSV/Excel (Export PDF/Excel Report) | Phân hệ 8: Báo cáo Thống kê & Phân tích KPI |
| UC-58 | Cảnh báo biến động chi phí CPL (CPL Fluctuation Alert) | Phân hệ 8: Báo cáo Thống kê & Phân tích KPI |
| UC-59 | Phê duyệt kế hoạch hoạt động của Agent (AI Agent Plan Approval) | Phân hệ 4: Quản trị Agent & Phân bổ tài nguyên AI |
| UC-60 | Quản lý kịch bản hội thoại tự động (Chat Scenario/Segment Management) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-61 | Theo dõi kênh & bài viết đối thủ (Competitor Source & Post Tracking) | Phân hệ 7: Tiếp thị Nội dung & Chiến dịch Quảng cáo |
| UC-62 | Quản lý nhãn và ghi chú hội thoại (Conversation Label & Note Management) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-63 | Hệ thống thông báo đẩy trong ứng dụng (In-App Notification System) | Phân hệ 9: Giám sát, Cảnh báo & Vận hành Hệ thống |
| UC-64 | Phê duyệt tri thức AI tự học đề xuất (AI Self-Learning Knowledge Suggestion) | Phân hệ 3: Tri thức RAG & Chatbot tự động |
| UC-65 | Quản lý kho tệp kỹ năng của Agent (Skill File Library Management) | Phân hệ 4: Quản trị Agent & Phân bổ tài nguyên AI |
| UC-66 | Tích hợp cổng thông tin Meta Business (Meta Business Integration) | Phân hệ 7: Tiếp thị Nội dung & Chiến dịch Quảng cáo |
| UC-67 | Sinh mô tả hình ảnh bằng AI (Content Image Prompt Generation) | Phân hệ 7: Tiếp thị Nội dung & Chiến dịch Quảng cáo |
| UC-68 | Không gian giám sát văn phòng Pixel Agents (Pixel Agents Office Monitoring Space) | Phân hệ 4: Quản trị Agent & Phân bổ tài nguyên AI |
| UC-69 | Cấu hình nhà cung cấp LLM & Vector Embedding (LLM & Vector Embedding Provider Configuration) | Phân hệ 4: Quản trị Agent & Phân bổ tài nguyên AI |
| UC-70 | Phân bổ nhân viên Sales vào kênh giao tiếp (Assign Sales Agent to Communication Channel) | Phân hệ 2: Quản lý Hộp thư & Tương tác đa kênh |
| UC-71 | Dự báo cơ hội Lead (Lead Forecasting) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-72 | Chuyển trạng thái Lead sang Won/Lost (Lead Stage Transition to Won/Lost) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |
| UC-73 | Quản lý danh mục sản phẩm & dịch vụ (Product & Service Catalog Management) | Phân hệ 5: Trợ lý Bán hàng & Quản lý Khách hàng |

---

## Chuẩn hóa Tác nhân (Actor Standardization)

### 1. Primary Actors (Tác nhân chính - Con người)
*   **User**: Tác nhân người dùng chung trong hệ thống (nhân viên thuộc doanh nghiệp/tenant).
*   **Admin** (System Admin): Quản trị viên hệ thống, kế thừa từ `User`.
*   **SalesLead**: Trưởng nhóm kinh doanh, kế thừa từ `User`.
*   **Sale**: Nhân viên kinh doanh, kế thừa từ `User`.
*   **Marketer**: Nhân viên tiếp thị, kế thừa từ `User`.
*   **QA**: Nhân viên kiểm thử/đảm bảo chất lượng tri thức, kế thừa từ `User`.
*   **Viewer**: Người dùng chỉ có quyền xem báo cáo/thông tin, kế thừa từ `User`.
*   **Customer**: Khách hàng bên ngoài tương tác qua widget chat hoặc các kênh mạng xã hội (tác nhân độc lập, không kế thừa từ `User`).

> [!NOTE]
> **Quan hệ kế thừa của Actor (Generalization):**
> Admin, SalesLead, Sale, Marketer, QA, Viewer đều là các vai trò kế thừa (Generalization) từ Actor cha là **User**. Do đó, mọi Use Case có Actor kích hoạt là `User` (như Đăng nhập, Khôi phục mật khẩu, Quản lý hồ sơ cá nhân) thì các Actor con đều tự động thực hiện được mà không cần vẽ thêm đường nối trên sơ đồ.

### 2. Secondary Actors (Tác nhân phụ - Hệ thống bên ngoài)
*   **Pancake API**: Hệ thống cổng kết nối và đồng bộ tin nhắn từ Facebook/Zalo.
*   **Zalo OA**: Kênh Official Account của Zalo.
*   **Meta API**: API của nền tảng Meta (Facebook Graph API, Meta Ads API).
*   **LLM Provider API**: API cung cấp mô hình ngôn ngữ lớn (Anthropic Claude, OpenAI...).
*   **Embedding Provider API**: API sinh vector embeddings phục vụ RAG.
*   **Ads API**: API quản lý quảng cáo bên ngoài.
*   **Browser Web Push API**: API gửi thông báo đẩy của trình duyệt.
*   **Email Service**: Dịch vụ gửi email (SMTP/Amazon SES...).

### 3. Time/Scheduler Actors (Tác nhân thời gian)
*   **System Scheduler**: Bộ lập lịch hệ thống (Hangfire/Cron Job) kích hoạt tự động các tác vụ chạy nền định kỳ hoặc theo giờ hẹn sẵn.

---

---

## Danh sách Phân hệ và Use Cases Chi tiết

## 1. Phân hệ Quản lý Định danh & Phân quyền (Identity & Access Management - IAM)
**Mục đích:** Quản lý đăng nhập, bảo mật 2 lớp, khôi phục mật khẩu, phân quyền vai trò (RBAC) và thông tin tài khoản nhân viên.
**Primary Actors:** User, Admin, SalesLead | **Secondary/Time Actors:** Email Service

### Danh sách Use Cases:
| Mã UC | Tên Use Case | Actor kích hoạt (Primary) | Actor hỗ trợ (Secondary) | include | extend |
|---|---|---|---|---|---|
| UC-01 | Đăng nhập hệ thống (System Login) | User |  |  |  |
| UC-02 | Xác thực hai yếu tố (Two-Factor Authentication (2FA)) | User |  |  | UC-01 |
| UC-03 | Khôi phục mật khẩu (Password Recovery) | User | Email Service |  |  |
| UC-04 | Quản lý tài khoản nhân viên (Employee Account Management) | Admin, SalesLead | Email Service |  |  |
| UC-05 | Cấu hình phân quyền RBAC (Role-Based Access Control) | Admin |  |  |  |
| UC-06 | Quản lý khóa API (API Keys Management) | Admin |  |  |  |

### Chi tiết Đặc tả Use Cases:
*   **UC-01: Đăng nhập hệ thống**
    *   *Mô tả:* Cho phép nhân viên (User/Admin/SalesLead...) đăng nhập vào hệ thống bằng Email và Mật khẩu cá nhân qua API `/auth/login`. Hệ thống sẽ xác thực thông tin đăng nhập, nếu chính xác sẽ cấp mã bảo mật JWT Access Token để truy cập tài nguyên và lưu Refresh Token dưới dạng HTTP-only cookie để tự động gia hạn phiên làm việc.
    *   *Tiền điều kiện:* Người dùng có tài khoản đang hoạt động trong hệ thống.
    *   *Hậu điều kiện:* Hệ thống cấp mã JWT Access Token và lưu trữ Refresh Token trong HTTP-only cookie.
*   **UC-02: Xác thực hai yếu tố**
    *   *Mô tả:* Tăng cường bảo mật cho tài khoản bằng cách yêu cầu người dùng nhập thêm mã xác thực OTP dùng một lần (được sinh ra theo thời gian thực từ ứng dụng Authenticator như Google/Microsoft Authenticator) qua API `/auth/login/2fa` sau khi đã nhập đúng mật khẩu ở bước 1.
    *   *Tiền điều kiện:* Tài khoản đã đăng nhập bước 1 thành công và đã bật 2FA.
    *   *Hậu điều kiện:* Phiên làm việc được xác thực hoàn tất và cấp token truy cập chính thức.
*   **UC-03: Khôi phục mật khẩu**
    *   *Mô tả:* Hỗ trợ nhân viên tự thiết lập lại mật khẩu mới khi quên mật khẩu cũ. Người dùng gửi yêu cầu và nhận mã OTP xác nhận gửi qua email, sau đó nhập mã OTP này cùng mật khẩu mới để hệ thống cập nhật lại qua các API `/auth/reset/request` và `/auth/reset/confirm`.
    *   *Tiền điều kiện:* Người dùng cung cấp email đã đăng ký hoạt động trong hệ thống.
    *   *Hậu điều kiện:* Hệ thống gửi mã OTP đặt lại mật khẩu và cho phép cập nhật mật khẩu mới thành công.
*   **UC-04: Quản lý tài khoản nhân viên**
    *   *Mô tả:* Cho phép Admin hoặc Trưởng nhóm (SalesLead) thực hiện tạo mới tài khoản cho nhân viên tư vấn, chỉnh sửa thông tin cá nhân (họ tên, email, vai trò) hoặc tạm thời khóa/mở khóa tài khoản khi có sự thay đổi nhân sự trong Tenant qua API `/api/admin/users`.
    *   *Tiền điều kiện:* Người dùng đăng nhập với quyền Admin hoặc SalesLead.
    *   *Hậu điều kiện:* Tài khoản nhân viên được tạo mới, cập nhật thông tin hoặc thay đổi trạng thái kích hoạt.
*   **UC-05: Cấu hình phân quyền RBAC**
    *   *Mô tả:* Cung cấp công cụ cho Admin định nghĩa các vai trò khác nhau (như Sale, Marketer, QA...) và gắn chi tiết các quyền hạn cụ thể (ví dụ: đọc lead, ghi tài liệu...) cho từng vai trò đó. Hệ thống sẽ tự động áp dụng phân quyền và xóa bộ nhớ cache phân quyền cũ ngay lập tức qua API `/api/rbac/roles`.
    *   *Tiền điều kiện:* Người dùng có vai trò Admin của hệ thống.
    *   *Hậu điều kiện:* Vai trò mới được thiết lập, danh sách quyền được thay đổi và xóa bộ nhớ cache phân quyền ngay lập tức.
*   **UC-06: Quản lý khóa API**
    *   *Mô tả:* Cho phép Admin tạo mới hoặc thu hồi các khóa truy cập API (API Keys) dùng cho việc tích hợp, kết nối an toàn với các phần mềm bên thứ ba. Các khóa này được mã hóa một chiều trong cơ sở dữ liệu để bảo mật qua API `/api/api-keys`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có vai trò Admin.
    *   *Hậu điều kiện:* Một khóa API mới được cấp (mã hóa một chiều ở DB) hoặc khóa cũ bị thu hồi.

---

## 2. Phân hệ Quản lý Hộp thư & Tương tác đa kênh (Omnichannel Inbox & Interaction)
**Mục đích:** Kết nối các kênh bán hàng xã hội, tiếp nhận tin nhắn/bình luận đa kênh, lọc hội thoại, phân bổ/chuyển giao và tương tác trực tiếp với khách hàng.
**Primary Actors:** Sale, SalesLead, Admin, Customer | **Secondary/Time Actors:** Pancake API,

### Danh sách Use Cases:
| Mã UC | Tên Use Case | Actor kích hoạt (Primary) | Actor hỗ trợ (Secondary) | include | extend |
|---|---|---|---|---|---|
| UC-09 | Cấu hình liên kết Pancake (Pancake Connection Configuration) | Admin | Pancake API |  |  |
| UC-10 | Tiếp nhận tin nhắn đa kênh (Multi-channel Message Reception) | Customer | Pancake API |  |  |
| UC-11 | Tự động phân loại ý định (Automatic Intent Classification) | System Scheduler | LLM Provider API |  |  |
| UC-12 | Xem hộp thư tập trung (View Unified Inbox) | Sale, (SalesLead), (Admin) |  |  |  |
| UC-13 | Bộ lọc hội thoại nâng cao (Advanced Conversation Filter) | Sale, (SalesLead), (Admin) |  |  | UC-12 |
| UC-14 | Phân bổ/Chuyển giao hội thoại (Conversation Assignment) | Sale, (SalesLead), (Admin) |  | UC-63 |  |
| UC-15 | Gửi tin nhắn phản hồi (Send Reply Message) | Sale, (SalesLead), (Admin) | Pancake API |  |  |
| UC-16 | Cập nhật trạng thái hội thoại (Change Conversation Status) | Sale, (SalesLead), (Admin) |  |  |  |
| UC-17 | Tự động phản hồi ngoài giờ (Automatic Out-of-Hours Reply) | System Scheduler | Pancake API |  |  |
| UC-18 | Nhận diện tin nhắn trùng lặp (Identify & Merge Conversations) | System Scheduler |  |  |  |
| UC-60 | Quản lý kịch bản hội thoại tự động (Chat Scenario/Segment Management) | Marketer, QA, (SalesLead), (Admin) |  |  |  |
| UC-62 | Quản lý nhãn và ghi chú hội thoại (Conversation Label & Note Management) | Sale, (SalesLead) |  |  | UC-12 |
| UC-70 | Phân bổ nhân viên Sales vào kênh giao tiếp (Assign Sales Agent to Communication Channel) | Admin, SalesLead |  |  |  |

### Chi tiết Đặc tả Use Cases:
*   **UC-09: Cấu hình liên kết Pancake**
    *   *Mô tả:* Hỗ trợ Admin dán mã Access Token từ Pancake để hệ thống tự động quét và lấy danh sách các Fanpage Facebook/Zalo đang hoạt động, từ đó thiết lập kết nối đồng bộ và mã hóa khóa kết nối riêng của từng trang để đẩy tin nhắn tự động qua API `/api/admin/channels/pancake`.
    *   *Tiền điều kiện:* Người dùng có vai trò Admin.
    *   *Hậu điều kiện:* Hệ thống kết nối Pancake thành công và lưu trữ mã kết nối cho từng trang được chọn.
*   **UC-10: Tiếp nhận tin nhắn đa kênh**
    *   *Mô tả:* Hệ thống chạy ngầm tự động nhận dữ liệu tin nhắn hoặc bình luận mới của khách hàng từ Facebook/Zalo qua Webhook của Pancake. Hệ thống kiểm tra tính hợp lệ bằng chữ ký bảo mật HMAC, lưu thông tin cuộc trò chuyện vào DB và dùng SignalR để đẩy thông báo hiển thị thời gian thực lên màn hình chat của nhân viên Sale.
    *   *Tiền điều kiện:* Khách hàng nhắn tin hoặc bình luận trên trang Zalo/Facebook liên kết.
    *   *Hậu điều kiện:* Tin nhắn được kiểm tra chữ ký HMAC thành công, lưu vào DB và phát tín hiệu SignalR.
*   **UC-11: Tự động phân loại ý định**
    *   *Mô tả:* AI tự động phân tích nội dung tin nhắn mới của khách hàng để nhận diện ý định mua sắm hoặc hỗ trợ, tự động gán nhãn phân loại hội thoại để phục vụ bộ lọc và định tuyến qua API /api/conversations/{id}/intent.
    *   *Tiền điều kiện:* Có tin nhắn mới từ khách hàng được tiếp nhận vào hệ thống.
    *   *Hậu điều kiện:* Ý định của khách hàng được nhận diện, gắn nhãn hội thoại thành công.
*   **UC-12: Xem hộp thư tập trung**
    *   *Mô tả:* Giao diện màn hình làm việc chính của nhân viên tư vấn, gom tất cả các cuộc hội thoại từ Facebook Page, Zalo OA và Web Widget vào một màn hình duy nhất. Hội thoại được sắp xếp thông minh theo thời gian tin nhắn mới nhất đổ về để nhân viên không bỏ sót khách hàng qua API `/api/inbox/conversations`.
    *   *Tiền điều kiện:* Người dùng đã đăng nhập (sale thường chỉ được xem hộp thư mình làm thành viên).
    *   *Hậu điều kiện:* Giao diện hiển thị danh sách cuộc hội thoại được sắp xếp theo thời gian tin nhắn mới nhất.
*   **UC-13: Bộ lọc hội thoại nâng cao**
    *   *Mô tả:* Giúp nhân viên nhanh chóng tìm kiếm và phân loại hội thoại bằng các bộ lọc đa tiêu chí như: lọc theo nguồn kênh (Facebook/Zalo), trạng thái xử lý (Chưa giải quyết/Đã xử lý), theo nhãn phân loại (Label) hoặc theo nhân viên đang chịu trách nhiệm qua API `/api/inbox/conversations`.
    *   *Tiền điều kiện:* Người dùng đã đăng nhập vào hệ thống.
    *   *Hậu điều kiện:* Danh sách hội thoại được lọc hiển thị đúng theo tiêu chí lựa chọn.
*   **UC-14: Phân bổ/Chuyển giao hội thoại**
    *   *Mô tả:* Hỗ trợ nhân viên tư vấn tự nhận phụ trách một cuộc chat mới, hoặc chuyển giao (handover) quyền xử lý cuộc chat đó cho một sale khác phù hợp hơn. Khi chuyển giao thành công, hệ thống tự động ghi nhật ký hoạt động và đẩy thông báo tức thời cho nhân viên tiếp nhận mới qua API `/api/inbox/conversations/{id}/assign` hoặc `/handover`.
    *   *Tiền điều kiện:* Người dùng có vai trò thuộc nhóm hỗ trợ (có quyền `conversations:write`).
    *   *Hậu điều kiện:* Nhân viên được chỉ định quyền sở hữu hội thoại, hệ thống lưu vết hành động.
*   **UC-15: Gửi tin nhắn phản hồi**
    *   *Mô tả:* Cho phép nhân viên trực tiếp nhập văn bản, chọn ảnh/tài liệu và bấm gửi để trả lời khách hàng. Tin nhắn sẽ được chuyển tiếp tức thời qua Pancake API đến kênh tương tác gốc (Facebook/Zalo) của khách hàng và ghi nhận vào lịch sử chat qua API `/api/inbox/conversations/{id}/messages`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi hội thoại (`conversations:write`).
    *   *Hậu điều kiện:* Tin nhắn được chuyển tới Pancake API và ghi nhận vào cơ sở dữ liệu tin nhắn đi.
*   **UC-16: Cập nhật trạng thái hội thoại**
    *   *Mô tả:* Nhân viên cập nhật trạng thái của cuộc hội thoại để quản lý tiến độ: chuyển sang "Resolved" (Đã giải quyết) sau khi tư vấn xong, hoặc "Escalated" (Cần hỗ trợ kỹ thuật/quản lý) để bàn giao cấp cao hơn xử lý qua API `/api/inbox/conversations/{id}/status`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi hội thoại (`conversations:write`).
    *   *Hậu điều kiện:* Trạng thái hội thoại được cập nhật thành Open, Resolved hoặc Escalated.
*   **UC-17: Tự động phản hồi ngoài giờ**
    *   *Mô tả:* Hệ thống tự động phát hiện các tin nhắn đến ngoài giờ làm việc của doanh nghiệp và gửi phản hồi tự động theo kịch bản cấu hình sẵn để giữ chân khách hàng qua API /api/inbox/conversations/out-of-hours.
    *   *Tiền điều kiện:* Tin nhắn của khách hàng gửi đến ngoài khung giờ làm việc cấu hình của doanh nghiệp.
    *   *Hậu điều kiện:* Tin nhắn phản hồi tự động ngoài giờ được gửi đi thành công cho khách hàng qua Pancake API.
*   **UC-18: Nhận diện tin nhắn trùng lặp**
    *   *Mô tả:* Tiến trình chạy ngầm tự động kiểm tra và lọc bỏ các gói tin nhắn bị gửi lặp lại (deduplication) do lỗi đường truyền mạng hoặc sự cố trùng webhook từ Pancake trước khi đưa vào hàng đợi xử lý của hệ thống.
    *   *Tiền điều kiện:* Có tin nhắn mới được đẩy từ webhook Pancake.
    *   *Hậu điều kiện:* Hệ thống kiểm tra trùng lặp và loại bỏ các gói tin bị trùng do lỗi truyền phát.
*   **UC-60: Quản lý kịch bản hội thoại tự động**
    *   *Mô tả:* Cung cấp công cụ quản lý thư viện các kịch bản phản hồi tự động, bao gồm thiết lập từ khóa kích hoạt (trigger), mẫu tin nhắn trả lời và tinh chỉnh tông giọng (tone) phản hồi của AI phù hợp với từng phân khúc khách hàng qua API `/api/chat-scenarios`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền ghi kịch bản chat (`chat-scenarios:write`).
    *   *Hậu điều kiện:* Kịch bản chat được tạo mới, sửa đổi hoặc xóa thành công trong DB.
*   **UC-62: Quản lý nhãn và ghi chú hội thoại**
    *   *Mô tả:* Nhân viên Sale có thể đính kèm nhãn phân loại (ví dụ: "Đang cân nhắc", "Hẹn gọi lại") và viết ghi chú nhanh thông tin khách hàng ngay trên giao diện chat. Chức năng này chỉ dành cho Sale và Trưởng nhóm, ngăn chặn Admin can thiệp để bảo mật quy trình tư vấn thực tế qua API `/api/inbox/conversations/{id}/labels` và `/notes`.
    *   *Tiền điều kiện:* Nhân viên đăng nhập không thuộc nhóm quản trị tối cao (không có quyền `admin:inboxes`).
    *   *Hậu điều kiện:* Nhãn dán hoặc ghi chú cuộc hội thoại được đính kèm hoặc gỡ bỏ.
*   **UC-70: Phân bổ nhân viên Sales vào kênh giao tiếp**
    *   *Mô tả:* Cho phép Admin hoặc Trưởng nhóm phân bổ cụ thể nhân viên Sales phụ trách tiếp nhận tin nhắn từ một Fanpage Facebook, Zalo OA hoặc Web Widget cụ thể để đảm bảo phân chia công việc hợp lý qua API /api/admin/channels/{id}/assign-agents.
    *   *Tiền điều kiện:* Người dùng đăng nhập có vai trò Admin hoặc SalesLead.
    *   *Hậu điều kiện:* Danh sách nhân viên Sales được liên kết với kênh giao tiếp tương ứng.

---

## 3. Phân hệ Tri thức RAG & Chatbot tự động (RAG Knowledge Base & Automated Chatbot)
**Mục đích:** Quản lý tài liệu tri thức doanh nghiệp, đồng bộ vector, kiểm thử độ chính xác tri thức và cung cấp Chatbot/FAQ tự động hỗ trợ khách hàng.
**Primary Actors:** Customer, Admin, SalesLead, Marketer, QA | **Secondary/Time Actors:** LLM Provider API, Embedding Provider API

### Danh sách Use Cases:
| Mã UC | Tên Use Case | Actor kích hoạt (Primary) | Actor hỗ trợ (Secondary) | include | extend |
|---|---|---|---|---|---|
| UC-19 | Quản lý nhóm tri thức (Knowledge Module Management) | Admin, SalesLead, Marketer, QA |  |  |  |
| UC-20 | Xem lịch sử phiên bản tri thức (KB Version Tracking) | Marketer, QA, (SalesLead), (Admin) |  |  |  |
| UC-21 | Triển khai và đồng bộ tri thức (Deploy & Synchronize Vector) | Marketer, QA, (SalesLead), (Admin) | Embedding Provider API |  |  |
| UC-22 | Khôi phục phiên bản tri thức cũ (Restore Previous KB Version) | Marketer, QA, (SalesLead), (Admin) |  | UC-20 |  |
| UC-23 | Quản lý kịch bản kiểm thử tri thức (KB Test-case Suite Management) | QA, Marketer, (SalesLead), (Admin) | LLM Provider API |  |  |
| UC-24 | Chạy kiểm tra độ chính xác tri thức (Run Accuracy Test) | QA, Marketer, (SalesLead), (Admin) | LLM Provider API | UC-23 |  |
| UC-25 | Chatbot tư vấn tri thức tự động (Automatic Consulting RAG Chatbot) | Customer | LLM Provider API |  |  |
| UC-42 | Tích hợp Widget Chat lên Website (Chat Widget Integration) | Customer |  |  |  |
| UC-43 | Tích hợp trang FAQ hỗ trợ (Support FAQ Page Integration) | Customer |  |  |  |
| UC-64 | Phê duyệt tri thức AI tự học đề xuất (AI Self-Learning Knowledge Suggestion) | QA, Marketer, (SalesLead), (Admin) |  |  |  |

### Chi tiết Đặc tả Use Cases:
*   **UC-19: Quản lý nhóm tri thức**
    *   *Mô tả:* Giúp phân loại kho tri thức thành các nhóm chủ đề khác nhau (ví dụ: Chính sách bán hàng, Thông số sản phẩm) và cấu hình phân quyền kiểm duyệt cho từng nhóm để đảm bảo tài liệu được quản lý bởi đúng bộ phận qua API `/api/kb/modules`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi tri thức (`kb:write`).
    *   *Hậu điều kiện:* Nhóm tri thức mới được định nghĩa hoặc cập nhật thông tin trong DB.
*   **UC-20: Xem lịch sử phiên bản tri thức**
    *   *Mô tả:* Lưu lại toàn bộ lịch sử các lần thay đổi nội dung tri thức. Nhân viên có thể xem danh sách các phiên bản, thời gian sửa đổi, người thực hiện và so sánh sự khác biệt giữa phiên bản cũ và mới qua API `/api/kb/modules/{id}/versions`.
    *   *Tiền điều kiện:* Người dùng đã đăng nhập hệ thống.
    *   *Hậu điều kiện:* Danh sách lịch sử các phiên bản tri thức được hiển thị.
*   **UC-21: Triển khai và đồng bộ tri thức**
    *   *Mô tả:* Khi tài liệu tri thức mới được duyệt, hệ thống sẽ tự động chuyển đổi văn bản thành các vector embeddings thông qua API sinh vector và lưu lên Vector Database (Qdrant) để làm dữ liệu nền tảng cho Chatbot RAG qua API `/deploy`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi tri thức (`kb:write`).
    *   *Hậu điều kiện:* Phiên bản tri thức được sinh vector và đồng bộ thành công lên cơ sở dữ liệu Vector.
*   **UC-22: Khôi phục phiên bản tri thức cũ**
    *   *Mô tả:* Hỗ trợ quay về (rollback) phiên bản tri thức cũ trong quá khứ nếu phát hiện phiên bản mới cập nhật bị sai sót hoặc thiếu chính xác, hệ thống sẽ tự động đồng bộ lại cơ sở dữ liệu Vector tương ứng qua API `/rollback`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi tri thức (`kb:write`).
    *   *Hậu điều kiện:* Phiên bản tri thức hoạt động được quay về phiên bản cũ được chọn.
*   **UC-23: Quản lý kịch bản kiểm thử tri thức**
    *   *Mô tả:* Thiết lập bộ câu hỏi và câu trả lời mẫu (Test Suite) dùng để chạy đánh giá chất lượng kho tri thức. Hệ thống hỗ trợ gọi LLM tự động phân tích tài liệu để sinh ra bộ câu hỏi tự động qua API `/api/kb/modules/{id}/test-cases`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi tri thức (`kb:write`).
    *   *Hậu điều kiện:* Bộ câu hỏi kiểm thử được lưu trữ hoặc sinh tự động thành công.
*   **UC-24: Chạy kiểm tra độ chính xác tri thức**
    *   *Mô tả:* Tác vụ chạy ngầm gọi API mô hình ngôn ngữ (Claude/GPT) để tự động trả lời bộ câu hỏi mẫu dựa trên kho tri thức hiện tại, sau đó chấm điểm độ chính xác (Accuracy) và tính khớp thông tin (Grounding) để lưu lịch sử đánh giá qua API `/test`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi tri thức (`kb:write`).
    *   *Hậu điều kiện:* Tác vụ chạy ngầm đánh giá độ chính xác RAG và lưu điểm số vào DB.
*   **UC-25: Chatbot tư vấn tri thức tự động**
    *   *Mô tả:* AI tự động tiếp nhận tin nhắn của khách hàng từ widget chat, tìm kiếm các tài liệu liên quan nhất trong Vector Database, dùng mô hình ngôn ngữ sinh ra câu trả lời chi tiết và gửi lại cho khách hàng theo thời gian thực mà không cần con người can thiệp.
    *   *Tiền điều kiện:* Khách hàng gửi tin nhắn trên Widget chat và AI tự động trả lời đang bật.
    *   *Hậu điều kiện:* Hệ thống tìm kiếm tri thức phù hợp và tự động gửi tin phản hồi cho khách hàng.
*   **UC-42: Tích hợp Widget Chat lên Website**
    *   *Mô tả:* Cung cấp đoạn mã nhúng (script nhúng) để cài đặt khung chat trực tuyến (Widget) lên website của doanh nghiệp, giúp khách hàng truy cập website có thể tương tác trực tiếp với hệ thống qua API `/api/public/widget/{tenantSlug}/bootstrap`.
    *   *Tiền điều kiện:* Khách hàng truy cập trang web có nhúng widget chat của Tenant.
    *   *Hậu điều kiện:* Widget được hiển thị đúng định dạng và branding cấu hình.
*   **UC-43: Tích hợp trang FAQ hỗ trợ**
    *   *Mô tả:* Tự động hiển thị trang danh sách các câu hỏi thường gặp FAQ trên trang hỗ trợ công khai, lấy dữ liệu tự động từ các tài liệu được đánh dấu là FAQ trong kho tri thức của doanh nghiệp qua API `/api/public/widget/{tenantSlug}/faq`.
    *   *Tiền điều kiện:* Khách hàng mở trang FAQ công khai của Tenant.
    *   *Hậu điều kiện:* Danh sách câu hỏi thường gặp FAQ lấy từ KB tri thức được hiển thị thành công.
*   **UC-64: Phê duyệt tri thức AI tự học đề xuất**
    *   *Mô tả:* Hệ thống phát hiện các câu hỏi chatbot chưa trả lời tốt hoặc các bài học mới rút ra từ lịch sử hội thoại của Sale và tạo đề xuất tri thức. Quản trị viên kiểm tra, chỉnh sửa và phê duyệt các tri thức này trước khi chính thức đưa vào kho lưu trữ qua API `/api/kb/suggestions/approve` và `/reject`.
    *   *Tiền điều kiện:* Đề xuất tri thức do AI tự học sinh ra đang ở trạng thái chờ duyệt.
    *   *Hậu điều kiện:* Tri thức đề xuất được Admin duyệt (có thể sửa nội dung) hoặc bị từ chối kèm lý do.

---

## 4. Phân hệ Quản trị Agent & Phân bổ tài nguyên AI (AI Agents & Resource Administration)
**Mục đích:** Cấu hình AI models, system prompts, tệp kỹ năng của Agent, quản lý hạn mức Token và duyệt kế hoạch thực thi của Agent.
**Primary Actors:** Admin, SalesLead | **Secondary/Time Actors:** LLM Provider API, Embedding Provider API

### Danh sách Use Cases:
| Mã UC | Tên Use Case | Actor kích hoạt (Primary) | Actor hỗ trợ (Secondary) | include | extend |
|---|---|---|---|---|---|
| UC-26 | Cấu hình Model AI & Prompt (AI Model & Prompt Configuration) | Admin |  |  |  |
| UC-27 | Quản lý vận hành Agent (AI Agent Operation Management) | Admin |  |  |  |
| UC-28 | Giám sát nhật ký hoạt động Agent (Real-time Logs Monitoring) | Marketer, QA, Viewer, (SalesLead), (Admin) |  |  |  |
| UC-29 | Quản lý số dư và hạn mức Token (Token Tracking & Management) | Admin |  |  |  |
| UC-59 | Phê duyệt kế hoạch hoạt động của Agent (AI Agent Plan Approval) | Admin, SalesLead |  | UC-28 |  |
| UC-65 | Quản lý kho tệp kỹ năng của Agent (Skill File Library Management) | Admin |  |  |  |
| UC-68 | Không gian giám sát văn phòng Pixel Agents (Pixel Agents Office Monitoring Space) | Marketer, QA, Viewer, (SalesLead), (Admin) |  |  |  |
| UC-69 | Cấu hình nhà cung cấp LLM & Vector Embedding (LLM & Vector Embedding Provider Configuration) | Admin | LLM Provider API, Embedding Provider API |  |  |

### Chi tiết Đặc tả Use Cases:
*   **UC-26: Cấu hình Model AI & Prompt**
    *   *Mô tả:* Cho phép Admin cấu hình chi tiết cho từng AI Agent, bao gồm chọn mô hình ngôn ngữ sử dụng (Claude, GPT), điều chỉnh system prompt hướng dẫn hành vi và kích hoạt các công cụ (Tools) mà Agent được phép gọi qua API `/api/agents/{code}/settings`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có vai trò Admin.
    *   *Hậu điều kiện:* Cấu hình Model, System Prompt và công cụ của Agent được cập nhật.
*   **UC-27: Quản lý vận hành Agent**
    *   *Mô tả:* Cho phép Admin kích hoạt (enable) hoặc tạm dừng (disable) sự tham gia của một AI Agent cụ thể vào quy trình phân phối công việc của hệ thống qua API `/api/agents/{code}/enable` và `/disable`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có vai trò Admin.
    *   *Hậu điều kiện:* Trạng thái hoạt động của Agent được chuyển đổi sang kích hoạt hoặc dừng.
*   **UC-28: Giám sát nhật ký hoạt động Agent**
    *   *Mô tả:* Giao diện hiển thị trực quan các bước suy nghĩ (Reasoning), quá trình gọi công cụ và xử lý dữ liệu của Agent theo thời gian thực giúp Admin dễ dàng gỡ lỗi hoặc tinh chỉnh kịch bản qua các API `/api/agents/{code}/traces` và `/api/logs/task-runs`.
    *   *Tiền điều kiện:* Người dùng có quyền đọc thông tin agent (`agent.read`).
    *   *Hậu điều kiện:* Dấu vết suy nghĩ và gọi công cụ của Agent được hiển thị trực quan.
*   **UC-29: Quản lý số dư và hạn mức Token**
    *   *Mô tả:* Admin theo dõi chi phí sử dụng API của các mô hình ngôn ngữ lớn, thiết lập giới hạn lượng token tối đa mà mỗi Agent được tiêu thụ trong tháng và cấu hình ngưỡng cảnh báo khi số dư API sắp hết qua API `/api/tokens/settings`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có vai trò Admin.
    *   *Hậu điều kiện:* Hạn mức token của các agent và ngưỡng cảnh báo được thiết lập thành công.
*   **UC-59: Phê duyệt kế hoạch hoạt động của Agent**
    *   *Mô tả:* Trước khi AI Agent thực thi các hành động quan trọng (ví dụ: sửa dữ liệu, gửi email hàng loạt), nó sẽ đề xuất một kế hoạch hành động. Quản lý kiểm tra và phê duyệt hoặc từ chối kế hoạch này để kiểm soát an toàn hệ thống qua API `/api/orchestration/v2/runs/{id}/approve`.
    *   *Tiền điều kiện:* Trình điều phối AI lập ra một kế hoạch thực thi công việc yêu cầu duyệt tay.
    *   *Hậu điều kiện:* Kế hoạch được phê duyệt và Agent bắt đầu chạy các tác vụ liên kết.
*   **UC-65: Quản lý kho tệp kỹ năng của Agent**
    *   *Mô tả:* Cho phép Admin quản lý các file hướng dẫn (chứa chỉ dẫn vận hành viết bằng định dạng Markdown) để gán cho Agent, giúp huấn luyện nhanh các kỹ năng chuyên biệt cho Agent thông qua API `/api/skills`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có vai trò Admin.
    *   *Hậu điều kiện:* File kỹ năng dạng Markdown (.md) được tạo mới, chỉnh sửa hoặc xóa trong hệ thống.
*   **UC-68: Không gian giám sát văn phòng Pixel Agents**
    *   *Mô tả:* Cung cấp giao diện trực quan mô phỏng không gian văn phòng làm việc (Office Layout) hiển thị vị trí, trạng thái hoạt động (idle, busy, offline) và tiến trình suy nghĩ của các AI Agent (Pixel Agents) đang làm việc trong hệ thống qua API /api/agents/office-monitoring.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền đọc thông tin agent (agents:read).
    *   *Hậu điều kiện:* Trạng thái và hoạt động của các Pixel Agents được hiển thị trực quan theo thời gian thực.
*   **UC-69: Cấu hình nhà cung cấp LLM & Vector Embedding**
    *   *Mô tả:* Cho phép Admin cấu hình và quản lý thông tin kết nối (API Key, Endpoint, Model Name) tới các nhà cung cấp mô hình ngôn ngữ lớn (LLM Providers) và mô hình vector hóa (Vector Embedding Providers) trong cùng một giao diện tập trung qua API /api/providers/config.
    *   *Tiền điều kiện:* Người dùng đăng nhập có vai trò Admin.
    *   *Hậu điều kiện:* Thông tin API Key và Endpoint của các nhà cung cấp AI được mã hóa và lưu trữ thành công.

---

## 5. Phân hệ Trợ lý Bán hàng & Quản lý Khách hàng (Sales Assistant & CRM)
**Mục đích:** Hỗ trợ sale tư vấn (AI soạn nháp, tóm tắt, gợi ý bán thêm), xem thông tin lead trên Kanban, chấm điểm và phân bổ tự động lead nóng, chạy drip campaign.
**Primary Actors:** Sale, SalesLead, Admin, Customer | **Secondary/Time Actors:** LLM Provider API, Pancake API, System Scheduler

### Danh sách Use Cases:
| Mã UC | Tên Use Case | Actor kích hoạt (Primary) | Actor hỗ trợ (Secondary) | include | extend |
|---|---|---|---|---|---|
| UC-30 | Gợi ý soạn thảo phản hồi bằng AI (AI Reply Draft Suggestion) | Sale, (SalesLead), (Admin) | LLM Provider API |  | UC-15 |
| UC-31 | Tóm tắt cuộc hội thoại bằng AI (AI Conversation Summary) | Sale, (SalesLead), (Admin) | LLM Provider API |  | UC-12 |
| UC-32 | Xem thanh bên bối cảnh Lead (Context Sidebar Lead) | Sale, Marketer, QA, Viewer, (SalesLead), (Admin) |  |  | UC-12 |
| UC-33 | Gợi ý bán thêm sản phẩm bằng AI (AI Product Upsell Suggestion) | Sale, (SalesLead), (Admin) | LLM Provider API |  | UC-15 |
| UC-34 | Thư viện mẫu câu trả lời nhanh (Quick Reply Template Library) | Sale, (SalesLead), (Admin) |  |  | UC-15 |
| UC-36 | Cấu hình quy tắc chấm điểm Lead (Scoring Rule Configuration) | Admin, SalesLead, (Sale) |  |  |  |
| UC-37 | Xem danh sách Lead dạng Kanban (View Kanban Lead List) | Sale, Marketer, QA, Viewer, (SalesLead), (Admin) |  |  |  |
| UC-38 | Quản lý chi tiết hồ sơ Lead (Detailed Lead Profile Management) | Sale, (SalesLead), (Admin) |  |  | UC-37 |
| UC-39 | Gộp thông tin Lead trùng lặp (Merge Duplicate Lead Info) | Sale, (SalesLead), (Admin) |  |  |  |
| UC-40 | Thiết lập chiến dịch Drip (Drip Campaign Setup) | Marketer, (SalesLead), (Admin) |  |  |  |
| UC-41 | Tự động gửi tin chăm sóc Drip (Automatic Drip Sequence Sending) | System Scheduler | Pancake API |  |  |
| UC-71 | Dự báo cơ hội Lead (Lead Forecasting) | System Scheduler | LLM Provider API |  |  |
| UC-72 | Chuyển trạng thái Lead sang Won/Lost (Lead Stage Transition to Won/Lost) | Sale, (SalesLead), (Admin) |  |  |  |
| UC-73 | Quản lý danh mục sản phẩm & dịch vụ (Product & Service Catalog Management) | Admin, SalesLead |  |  |  |

### Chi tiết Đặc tả Use Cases:
*   **UC-30: Gợi ý soạn thảo phản hồi bằng AI**
    *   *Mô tả:* AI tự động phân tích nội dung cuộc trò chuyện hiện tại và tri thức sản phẩm để soạn thảo sẵn một câu trả lời gợi ý (Draft) hiển thị ngay trên khung chat của Sale, giúp Sale chỉ cần nhấn chọn gửi nhanh qua API `/api/sale-assist/draft`.
    *   *Tiền điều kiện:* Nhân viên có quyền sử dụng trợ lý sale (`sale-assist:use`).
    *   *Hậu điều kiện:* Bản nháp phản hồi do AI soạn thảo được hiển thị trên khung chat của sale.
*   **UC-31: Tóm tắt cuộc hội thoại bằng AI**
    *   *Mô tả:* AI tự động đọc hiểu toàn bộ lịch sử trò chuyện và xuất ra đoạn tóm tắt ngắn gọn về nhu cầu, bối cảnh và mong muốn của khách hàng, giúp Sale nắm bắt thông tin nhanh chóng khi tiếp nhận cuộc gọi/chat qua API `/api/sale-assist/summary`.
    *   *Tiền điều kiện:* Nhân viên có quyền sử dụng trợ lý sale (`sale-assist:use`).
    *   *Hậu điều kiện:* Đoạn tóm tắt nội dung lịch sử chat được hiển thị ở màn hình làm việc.
*   **UC-32: Xem thanh bên bối cảnh Lead**
    *   *Mô tả:* Khi Sale chat với khách, thanh bên (Sidebar) sẽ tự động hiển thị hồ sơ khách hàng tiềm năng bao gồm thông tin liên hệ, điểm số đánh giá độ nóng (Lead Score) và lịch sử tương tác gần nhất qua API `/api/leads/{id}/context`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền đọc thông tin lead (`leads:read`).
    *   *Hậu điều kiện:* Thông tin hồ sơ, hoạt động gần đây và điểm số của lead được hiển thị.
*   **UC-33: Gợi ý bán thêm sản phẩm bằng AI**
    *   *Mô tả:* AI phân tích hồ sơ và nhu cầu của khách hàng trong cuộc chat để gợi ý các sản phẩm/khóa học nâng cấp hoặc gói bán thêm phù hợp nhất kèm theo luận điểm thuyết phục để Sale chào hàng qua API `/api/sale-assist/upsell`.
    *   *Tiền điều kiện:* Nhân viên có quyền sử dụng trợ lý sale (`sale-assist:use`) và lead đạt stage "hot".
    *   *Hậu điều kiện:* Gợi ý sản phẩm/khóa học nâng cao phù hợp với lead được hiển thị.
*   **UC-34: Thư viện mẫu câu trả lời nhanh**
    *   *Mô tả:* Quản lý danh mục các mẫu tin nhắn soạn sẵn của doanh nghiệp (ví dụ: chào hỏi, báo giá nhanh). Sale có thể gõ phím tắt để chèn nhanh nội dung này vào khung chat giúp tăng tốc độ phản hồi qua API `/api/sale-assist/quick-replies`.
    *   *Tiền điều kiện:* Nhân viên có quyền sử dụng trợ lý sale (`sale-assist:use`).
    *   *Hậu điều kiện:* Các mẫu tin nhắn trả lời nhanh được quản lý CRUD thành công.
*   **UC-36: Cấu hình quy tắc chấm điểm Lead**
    *   *Mô tả:* Cho phép thiết lập các quy tắc và trọng số điểm cộng/trừ tự động dựa trên hành động của khách hàng (ví dụ: đặt lịch hẹn, cung cấp số điện thoại: +10 điểm, bỏ lỡ buổi hẹn: -5 điểm) qua API `/api/lead-scoring-rules`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi lead (`leads:write`).
    *   *Hậu điều kiện:* Quy tắc chấm điểm lead được cập nhật trọng số và hành động tương ứng.
*   **UC-37: Xem danh sách Lead dạng Kanban**
    *   *Mô tả:* Hiển thị trực quan danh sách khách hàng tiềm năng dưới dạng các thẻ (cards) trên bảng Kanban chia theo các giai đoạn phễu bán hàng (Lạnh, Ấm, Nóng) dựa trên điểm số qua API `/api/leads`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền đọc thông tin lead (`leads:read`).
    *   *Hậu điều kiện:* Danh sách Lead được phân nhóm theo cột phân khúc (cold, warm, hot).
*   **UC-38: Quản lý chi tiết hồ sơ Lead**
    *   *Mô tả:* Xem thông tin chi tiết, nguồn tiếp cận, lịch sử điểm số và chỉnh sửa thông tin hồ sơ của từng khách hàng tiềm năng qua `/api/leads/{id}`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi lead (`leads:write`).
    *   *Hậu điều kiện:* Hồ sơ lead được cập nhật chi tiết các thuộc tính và lịch sử hoạt động.
*   **UC-39: Gộp thông tin Lead trùng lặp**
    *   *Mô tả:* Cho phép nhân viên tư vấn hoặc trưởng nhóm kinh doanh rà soát, đối chiếu và thực hiện gộp các hồ sơ khách hàng tiềm năng bị trùng lặp thông tin (như trùng số điện thoại, email) thành một hồ sơ duy nhất để chuẩn hóa dữ liệu qua API /api/leads/merge.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền ghi lead (leads:write) và có các hồ sơ lead bị trùng lặp thông tin.
    *   *Hậu điều kiện:* Các hồ sơ lead trùng lặp được gộp thành một hồ sơ duy nhất, giữ lại lịch sử tương tác đầy đủ.
*   **UC-40: Thiết lập chiến dịch Drip**
    *   *Mô tả:* Cho phép Marketer hoặc Trưởng nhóm kinh doanh thiết lập các chiến dịch gửi tin nhắn tự động theo chuỗi thời gian (Drip Campaign), định nghĩa các bước gửi, thời gian chờ và mẫu tin nhắn chăm sóc qua API /api/drip-campaigns.
    *   *Tiền điều kiện:* Người dùng có quyền ghi chiến dịch (campaigns:write).
    *   *Hậu điều kiện:* Chiến dịch chăm sóc Drip được tạo mới và lên lịch gửi tin nhắn tự động thành công.
*   **UC-41: Tự động gửi tin chăm sóc Drip**
    *   *Mô tả:* Hệ thống tự động gửi các tin nhắn chăm sóc khách hàng theo chuỗi kịch bản đã lên lịch trước (ví dụ: gửi sau 1 ngày, gửi sau 3 ngày) nếu cuộc trò chuyện đang bật chế độ AI tự động phản hồi.
    *   *Tiền điều kiện:* Thời gian trễ của bước drip kết thúc và cuộc chat đang bật AI tự trả lời.
    *   *Hậu điều kiện:* Tin nhắn drip cá nhân hóa được gửi đi thành công thông qua Pancake API.
*   **UC-71: Dự báo cơ hội Lead**
    *   *Mô tả:* AI tự động phân tích dữ liệu lịch sử tương tác, điểm số và hành vi của Lead để đưa ra dự báo về khả năng chốt đơn thành công (Won/Lost Probability) của Lead đó trong tương lai qua API /api/leads/{id}/forecast.
    *   *Tiền điều kiện:* Dữ liệu tương tác và điểm số của Lead được cập nhật đầy đủ.
    *   *Hậu điều kiện:* Điểm số dự báo cơ hội và tỷ lệ chuyển đổi của Lead được lưu vào hồ sơ.
*   **UC-72: Chuyển trạng thái Lead sang Won/Lost**
    *   *Mô tả:* Cho phép nhân viên kinh doanh cập nhật kết quả tư vấn của Lead, chuyển trạng thái Lead sang "Won" (Chốt đơn thành công) hoặc "Lost" (Thất bại) kèm theo lý do cụ thể để đóng quy trình bán hàng qua API /api/leads/{id}/stage-transition.
    *   *Tiền điều kiện:* Người dùng có quyền ghi lead (leads:write).
    *   *Hậu điều kiện:* Trạng thái bán hàng của Lead được cập nhật và hệ thống tự động lưu vết để làm báo cáo chuyển đổi.
*   **UC-73: Quản lý danh mục sản phẩm & dịch vụ**
    *   *Mô tả:* Cho phép Admin hoặc Trưởng nhóm kinh doanh cập nhật thông tin, mô tả, giá cả và phân loại của các sản phẩm, dịch vụ của doanh nghiệp để làm nguồn tri thức cho AI tư vấn và sinh báo giá qua API /api/catalog/products.
    *   *Tiền điều kiện:* Người dùng có quyền ghi danh mục (catalog:write).
    *   *Hậu điều kiện:* Danh mục sản phẩm, dịch vụ được cập nhật thông tin chính xác.

---

## 6. Phân hệ Tài liệu & PDF Tự động (Document Generation & Delivery)
**Mục đích:** Thiết kế biểu mẫu tài liệu, tự động tạo PDF báo giá/brochure, gửi và theo dõi hành vi đọc tài liệu của khách hàng.
**Primary Actors:** Admin, SalesLead, Sale, Marketer | **Secondary/Time Actors:** Pancake API, Email Service

### Danh sách Use Cases:
| Mã UC | Tên Use Case | Actor kích hoạt (Primary) | Actor hỗ trợ (Secondary) | include | extend |
|---|---|---|---|---|---|
| UC-49 | Quản lý mẫu tài liệu (Doc Template Library Management) | Sale, Marketer, (SalesLead), (Admin) |  |  |  |
| UC-50 | Tự động sinh tài liệu PDF báo giá (Automatic PDF Quote Generation) | Sale, Marketer, (SalesLead), (Admin) |  | UC-49 |  |
| UC-51 | Xem và tải về tài liệu (View and Preview Document) | Sale, Marketer, QA, Viewer, (SalesLead), (Admin) |  |  |  |
| UC-52 | Gửi tài liệu và theo dõi lượt mở (Send Document & Track) | Sale, Marketer, (SalesLead), (Admin) | Pancake API, Email Service | UC-51 | UC-15 |

### Chi tiết Đặc tả Use Cases:
*   **UC-49: Quản lý mẫu tài liệu**
    *   *Mô tả:* Quản lý thư viện các mẫu tài liệu (báo giá, brochure sản phẩm, tài liệu kỹ thuật) dưới dạng cấu trúc HTML mẫu. Nhân viên có thể CRUD các mẫu này qua API `/api/docs/templates`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi tài liệu (`docs:write`).
    *   *Hậu điều kiện:* Thư viện mẫu tài liệu được thêm mới, sửa đổi hoặc xóa trong DB.
*   **UC-50: Tự động sinh tài liệu PDF báo giá**
    *   *Mô tả:* Tự động kết hợp dữ liệu khách hàng với mẫu tài liệu có sẵn để tạo ra tệp PDF báo giá hoặc hợp đồng cá nhân hóa chỉ trong vài giây qua API `/api/docs/generate`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi tài liệu (`docs:write`).
    *   *Hậu điều kiện:* File PDF được sinh thành công từ template và lưu vào hệ thống.
*   **UC-51: Xem và tải về tài liệu**
    *   *Mô tả:* Cho phép nhân viên hoặc khách hàng xem trước tài liệu trực tiếp trên trình duyệt hoặc tải tệp PDF về thiết bị cá nhân qua API `/api/docs/{id}/download`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền đọc tài liệu (`docs:read`).
    *   *Hậu điều kiện:* Tài liệu được tải về thiết bị và hệ thống cập nhật số lần mở file.
*   **UC-52: Gửi tài liệu và theo dõi lượt mở**
    *   *Mô tả:* Gửi liên kết tài liệu cho khách hàng qua email hoặc chat Zalo. Liên kết này được nhúng mã theo dõi ẩn (tracking beacon) để tự động ghi nhận thời điểm khách hàng click mở tài liệu và thông báo cho Sale.
    *   *Tiền điều kiện:* Tài liệu đã được sinh thành công trong hệ thống.
    *   *Hậu điều kiện:* Liên kết tài liệu kèm ảnh beacon theo dõi được gửi cho khách qua email/

---

## 7. Phân hệ Tiếp thị Nội dung & Chiến dịch Quảng cáo (Content Marketing & Ads Campaign)
**Mục đích:** Quét xu hướng/đối thủ, tạo brief và nháp bài viết bằng AI, lên lịch đăng bài Meta, lập quy tắc quảng cáo và tự động tối ưu hóa ngân sách.
**Primary Actors:** Admin, Marketer, System Scheduler, Meta API | **Secondary/Time Actors:** LLM Provider API, Meta API, Ads API

### Danh sách Use Cases:
| Mã UC | Tên Use Case | Actor kích hoạt (Primary) | Actor hỗ trợ (Secondary) | include | extend |
|---|---|---|---|---|---|
| UC-44 | Quét tìm xu hướng nội dung (Content Trend Scanning) | System Scheduler | LLM Provider API |  |  |
| UC-45 | Quản lý Brief nội dung (Content Brief Management) | Marketer, (Admin) |  |  |  |
| UC-46 | Tự động viết nháp bài viết bằng AI (Automatic Content Drafting) | Marketer, (Admin) | LLM Provider API | UC-45 |  |
| UC-47 | Xem lịch xuất bản nội dung (View Content Calendar) | Marketer, Sale, QA, Viewer, (SalesLead), (Admin) |  |  |  |
| UC-48 | Tự động đăng bài theo lịch trình (Automatic Post Scheduling) | System Scheduler | Meta API |  |  |
| UC-61 | Theo dõi kênh & bài viết đối thủ (Competitor Source & Post Tracking) | Marketer, (Admin) |  |  |  |
| UC-66 | Tích hợp cổng thông tin Meta Business (Meta Business Integration) | Admin | Meta API |  |  |
| UC-67 | Sinh mô tả hình ảnh bằng AI (Content Image Prompt Generation) | Marketer, (Admin) | LLM Provider API |  | UC-46 |

### Chi tiết Đặc tả Use Cases:
*   **UC-44: Quét tìm xu hướng nội dung**
    *   *Mô tả:* Tác vụ chạy ngầm định kỳ kích hoạt AI phân tích các chủ đề nóng, tin tức xu hướng trực tuyến để tổng hợp báo cáo gợi ý nội dung mới cho Marketer làm ý tưởng viết bài.
    *   *Tiền điều kiện:* Tác vụ quét xu hướng nội dung định kỳ được kích hoạt hằng tuần.
    *   *Hậu điều kiện:* Dữ liệu xu hướng trực tuyến được quét thành công và thông báo cho Marketer.
*   **UC-45: Quản lý Brief nội dung**
    *   *Mô tả:* Marketer thiết lập bản tóm tắt yêu cầu viết bài (bao gồm chủ đề, kênh đăng tải, từ khóa mục tiêu và tài liệu đính kèm) để làm tài liệu định hướng viết bài qua API `/api/content/briefs`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi nội dung (`content:write`).
    *   *Hậu điều kiện:* Yêu cầu Brief nội dung được tạo mới hoặc cập nhật thông tin trong DB.
*   **UC-46: Tự động viết nháp bài viết bằng AI**
    *   *Mô tả:* AI phân tích bản Brief yêu cầu và tự động soạn thảo bản nháp bài viết hoàn chỉnh (phù hợp với văn phong và độ dài cấu hình của kênh đăng tải) qua API `/api/content/items/generate`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi nội dung (`content:write`) và chọn một brief.
    *   *Hậu điều kiện:* Bản thảo bài viết nháp do AI sinh ra được ghi nhận vào hàng đợi.
*   **UC-47: Xem lịch xuất bản nội dung**
    *   *Mô tả:* Giao diện lịch biểu trực quan hiển thị toàn bộ các bài viết đã lên lịch xuất bản, bài viết nháp hoặc bài viết đã đăng trên các kênh truyền thông xã hội qua API `/api/content/calendar`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền đọc nội dung (`content:read`).
    *   *Hậu điều kiện:* Lịch hiển thị danh sách các bài viết đã lên lịch xuất bản trực quan.
*   **UC-48: Tự động đăng bài theo lịch trình**
    *   *Mô tả:* Job chạy ngầm định kỳ kiểm tra các bài viết đến giờ đăng và gọi API của Meta để đăng trực tiếp bài viết lên các Fanpage Facebook mà không cần thao tác thủ công qua `ContentPublishJob`.
    *   *Tiền điều kiện:* Bài viết có trạng thái "scheduled" đến thời điểm hẹn giờ đăng.
    *   *Hậu điều kiện:* Bài viết được xuất bản trực tiếp lên Facebook thành công qua Meta Graph API.
*   **UC-61: Theo dõi kênh & bài viết đối thủ**
    *   *Mô tả:* Marketer cấu hình nguồn theo dõi (website, RSS) của đối thủ. Hệ thống chạy ngầm sẽ tự động quét và cào dữ liệu các bài đăng mới nhất của đối thủ để hiển thị danh sách tham khảo qua API `/api/competitors`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền ghi nội dung (`content.write`).
    *   *Hậu điều kiện:* Các nguồn RSS đối thủ được quản lý, danh sách bài viết đối thủ được hiển thị.
*   **UC-66: Tích hợp cổng thông tin Meta Business**
    *   *Mô tả:* Cho phép Admin cấu hình tài khoản doanh nghiệp Meta (App ID/Secret), kích hoạt đăng nhập OAuth để hệ thống kết nối đồng bộ tài nguyên quảng cáo và Fanpage qua API `/api/admin/meta`.
    *   *Tiền điều kiện:* Người dùng có vai trò Admin.
    *   *Hậu điều kiện:* Cấu hình tích hợp Meta App được lưu trữ và token liên kết doanh nghiệp được thiết lập.
*   **UC-67: Sinh mô tả hình ảnh bằng AI**
    *   *Mô tả:* AI phân tích nội dung bài viết và tự động soạn thảo các đoạn prompt mô tả hình ảnh chi tiết dùng để đưa vào các mô hình vẽ tranh AI sinh ảnh minh họa phù hợp qua API `/api/content/image-prompts`.
    *   *Tiền điều kiện:* Người dùng có quyền ghi nội dung (`content:write`).
    *   *Hậu điều kiện:* Gợi ý prompt mô tả hình ảnh minh họa cho bài viết được sinh ra.

---

## 8. Phân hệ Báo cáo Thống kê & Phân tích KPI (KPI Analytics & Forecasting)
**Mục đích:** Tổng hợp KPI hoạt động đa kênh, phễu chuyển đổi lead, hiệu suất Sales vs AI, dự báo xu hướng và cảnh báo biến động chi phí.
**Primary Actors:** Admin, SalesLead, Sale, Marketer, QA, Viewer, System Scheduler | **Secondary/Time Actors:** LLM Provider API

### Danh sách Use Cases:
| Mã UC | Tên Use Case | Actor kích hoạt (Primary) | Actor hỗ trợ (Secondary) | include | extend |
|---|---|---|---|---|---|
| UC-53 | Báo cáo chỉ số KPI đa kênh (KPI Report Dashboard) | Sale, Marketer, QA, Viewer, (SalesLead), (Admin) |  |  |  |
| UC-54 | Báo cáo chuyển đổi phễu (Funnel Conversion Report) | Sale, Marketer, QA, Viewer, (SalesLead), (Admin) |  |  |  |
| UC-55 | Báo cáo hiệu suất hoạt động Sales & AI (Sale & AI Performance Report) | Sale, Marketer, QA, Viewer, (SalesLead), (Admin) |  |  |  |
| UC-56 | Cấu hình lịch gửi báo cáo (Report Sending Schedule Setup) | SalesLead, Admin | Email Service |  |  |
| UC-57 | Xuất báo cáo dữ liệu CSV/Excel (Export PDF/Excel Report) | Sale, Marketer, QA, Viewer, (SalesLead), (Admin) |  |  | UC-53, UC-54, UC-55 |
| UC-58 | Cảnh báo biến động chi phí CPL (CPL Fluctuation Alert) | System Scheduler | LLM Provider API | UC-63 |  |

### Chi tiết Đặc tả Use Cases:
*   **UC-53: Báo cáo chỉ số KPI đa kênh**
    *   *Mô tả:* Giao diện tổng hợp dữ liệu thời gian thực và trực quan hóa các chỉ số đo lường hiệu quả (KPI) đa kênh như lượng khách hàng, số lượng tin nhắn và tốc độ phản hồi qua API `/api/analytics/omnichannel`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền đọc báo cáo (`analytics:read`).
    *   *Hậu điều kiện:* Dashboard hiển thị đầy đủ biểu đồ số liệu KPI đa kênh của tenant.
*   **UC-54: Báo cáo chuyển đổi phễu**
    *   *Mô tả:* Xem báo cáo chi tiết về tỷ lệ chuyển đổi khách hàng tiềm năng qua từng phân khúc điểm số (từ cold sang warm, từ warm sang hot và sang customer) qua `/api/analytics/funnel`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền đọc báo cáo (`analytics:read`).
    *   *Hậu điều kiện:* Biểu đồ phễu chuyển đổi lead được hiển thị đầy đủ.
*   **UC-55: Báo cáo hiệu suất hoạt động Sales & AI**
    *   *Mô tả:* Bảng thống kê so sánh hiệu quả chốt đơn, thời gian phản hồi trung bình và chi phí sử dụng API AI giữa đội ngũ nhân viên Sale thực tế với các AI Agent tự động qua API `/api/analytics/agent-performance` và `/agent-cost`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền đọc báo cáo (`analytics:read`).
    *   *Hậu điều kiện:* Thống kê so sánh năng suất và chi phí hoạt động được hiển thị.
*   **UC-56: Cấu hình lịch gửi báo cáo**
    *   *Mô tả:* Cho phép người dùng thiết lập lịch biểu tự động gửi báo cáo hiệu suất kinh doanh hoặc hoạt động AI định kỳ (hàng ngày, hàng tuần) qua Email cho quản lý doanh nghiệp qua API /api/analytics/reports/schedules.
    *   *Tiền điều kiện:* Người dùng đăng nhập có vai trò Admin hoặc SalesLead.
    *   *Hậu điều kiện:* Lịch gửi báo cáo tự động được cấu hình thành công trong hệ thống.
*   **UC-57: Xuất báo cáo dữ liệu CSV/Excel**
    *   *Mô tả:* Trích xuất toàn bộ dữ liệu báo cáo phân tích hiệu suất và chuyển đổi ra file CSV để lưu trữ nội bộ hoặc xử lý ngoại tuyến qua `/api/analytics/export`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có quyền đọc báo cáo (`analytics:read`).
    *   *Hậu điều kiện:* Dữ liệu báo cáo được xuất ra định dạng file CSV tải về máy thành công.
*   **UC-58: Cảnh báo biến động chi phí CPL**
    *   *Mô tả:* Tác vụ chạy ngầm định kỳ quét dữ liệu chi tiêu quảng cáo, tự động phát hiện và gửi thông báo cảnh báo tức thời khi chi phí trên mỗi lead (CPL) tăng vọt đột biến vượt ngưỡng an toàn qua `AnomalyAlertJob`.
    *   *Tiền điều kiện:* Tác vụ chạy ngầm định kỳ đánh giá bất thường KPI được kích hoạt.
    *   *Hậu điều kiện:* Quá trình đánh giá hoàn tất và gửi cảnh báo in-app/SignalR khi phát hiện bất thường.

---

## 9. Phân hệ Giám sát, Cảnh báo & Vận hành Hệ thống (System Operations & Monitoring)
**Mục đích:** Cấu hình chính sách, tra cứu nhật ký hệ thống, cảnh báo quá hạn SLA, gửi mail nhắc nhở, dọn dẹp lưu trữ và quản lý Hangfire dashboard.
**Primary Actors:** Admin, User, System Scheduler | **Secondary/Time Actors:** Browser Web Push API, Email Service, Pancake API

### Danh sách Use Cases:
| Mã UC | Tên Use Case | Actor kích hoạt (Primary) | Actor hỗ trợ (Secondary) | include | extend |
|---|---|---|---|---|---|
| UC-07 | Cấu hình chính sách hệ thống (System Settings Configuration) | Admin |  |  |  |
| UC-08 | Tra cứu nhật ký hệ thống (Audit Log Lookup) | Sale, Marketer, QA, Viewer, (SalesLead), (Admin) |  |  |  |
| UC-35 | Cảnh báo quá hạn phản hồi SLA (SLA Wait Time Alert) | System Scheduler |  | UC-63 |  |
| UC-63 | Hệ thống thông báo đẩy trong ứng dụng (In-App Notification System) | User |  |  |  |

### Chi tiết Đặc tả Use Cases:
*   **UC-07: Cấu hình chính sách hệ thống**
    *   *Mô tả:* Admin thiết lập các chính sách vận hành chung của Tenant bao gồm: trần ngân sách chi phí AI hàng tháng, cấu hình bật/tắt duyệt bài viết tự động và thời gian nhường quyền khi Sale chat tay qua API `/api/admin/tenant/orchestration`.
    *   *Tiền điều kiện:* Người dùng đăng nhập có vai trò Admin.
    *   *Hậu điều kiện:* Cấu hình giới hạn chi phí AI, duyệt kịch bản, duyệt tin nhắn và thời gian nhường quyền được lưu lại.
*   **UC-08: Tra cứu nhật ký hệ thống**
    *   *Mô tả:* Cho phép quản lý tra cứu lịch sử chi tiết các hành động nhạy cảm của người dùng trong hệ thống (thay đổi cấu hình, xuất file, IP truy cập, thiết bị) để phục vụ kiểm toán an ninh qua API `/api/admin/audit-logs`.
    *   *Tiền điều kiện:* Người dùng đã đăng nhập thành công vào hệ thống.
    *   *Hậu điều kiện:* Danh sách nhật ký hoạt động được hiển thị tương ứng với bộ lọc.
*   **UC-35: Cảnh báo quá hạn phản hồi SLA**
    *   *Mô tả:* Tác vụ chạy ngầm định kỳ quét các cuộc trò chuyện chưa có phản hồi, nếu thời gian chờ của khách vượt quá cam kết (SLA), hệ thống sẽ gửi thông báo cảnh báo khẩn cấp tới Sale qua SignalR Hub.
    *   *Tiền điều kiện:* Hội thoại ở trạng thái Open chưa có phản hồi vượt quá thời gian cấu hình.
    *   *Hậu điều kiện:* Thông báo cảnh báo quá hạn được đẩy tức thời tới nhân viên qua SignalR.
*   **UC-63: Hệ thống thông báo đẩy trong ứng dụng**
    *   *Mô tả:* Tiếp nhận và hiển thị các thông báo đẩy (Pop-up) theo thời gian thực ngay trên giao diện web của nhân viên khi có các sự kiện quan trọng phát sinh (ví dụ: có lead nóng mới phân bổ) qua API `/api/notifications`.
    *   *Tiền điều kiện:* Sự kiện hệ thống phát sinh thông báo hướng tới người dùng cụ thể hoặc nhóm.
    *   *Hậu điều kiện:* Thông báo được lưu vào DB và phát trực tiếp tới trình duyệt người dùng.

---

