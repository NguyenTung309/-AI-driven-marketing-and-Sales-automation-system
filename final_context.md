# BẢN THIẾT KẾ SƠ ĐỒ NGỮ CẢNH (CONTEXT DIAGRAM BLUEPRINT)
## HỆ THỐNG: CLAWBOT SYSTEM

Tài liệu này đóng vai trò là Bản thiết kế kỹ thuật (Blueprint) chuẩn hóa cho Sơ đồ ngữ cảnh (Context Diagram) của hệ thống **Clawbot System**. Tài liệu áp dụng cơ chế khử trùng lặp dữ liệu (De-duplication) bằng nguyên tắc **"Thực thể Đại diện" (Domain Ownership)** để tinh gọn sơ đồ ngữ cảnh Level 0, đảm bảo không có luồng dữ liệu nào bị lặp lại giữa các thực thể con người và phân bổ chuẩn nghiệp vụ thực tế cho 5 thực thể con người.

---

### PHẦN 1: HỆ THỐNG TRUNG TÂM

*   **Tên hệ thống trung tâm:** `Clawbot System`
*   **Vị trí:** Nằm ở vị trí trung tâm của sơ đồ. Mọi luồng dữ liệu trao đổi đều đi từ thực thể ngoại vi vào hệ thống trung tâm (Inbound) hoặc đi từ hệ thống trung tâm ra thực thể ngoại vi (Outbound).

---

### PHẦN 2: DANH SÁCH THỰC THỂ NGOẠI VI (EXTERNAL ENTITIES)

#### 1. Thực thể Con người (Human Entities - Tương ứng với Primary Actors)
*   **Customer (Khách hàng):** Người dùng bên ngoài tương tác qua các kênh chat, nhận tin nhắn tự động, tra cứu trang FAQ hỗ trợ công khai và xem/tải về các tài liệu bán hàng.
*   **Admin (Admin / Ban giám đốc - Đại diện mảng Hệ thống, AI & Tri thức):** Gánh toàn bộ các luồng liên quan đến: Cấu hình hệ thống, phân quyền RBAC, API Keys, cấu hình AI Agent, cấu hình hạ tầng Token, tra cứu logs kiểm toán và toàn bộ các luồng Quản lý Tri thức (Knowledge Base) & Chạy Test RAG.
*   **Sale (Nhân viên kinh doanh - Đại diện mảng Bán hàng):** Gánh toàn bộ các luồng liên quan đến: Chat đa kênh (Inbox), cập nhật thông tin và trạng thái Lead, gộp lead trùng, nhận các đề xuất soạn thảo/bán thêm từ AI, tạo biểu mẫu và gửi tài liệu bán hàng kèm beacon theo dõi.
*   **SalesLead (Trưởng nhóm kinh doanh - Đại diện mảng Quản trị kinh doanh & Báo cáo):** Gánh toàn bộ các luồng liên quan đến: Quản lý nhân sự bán hàng, phân bổ kênh giao tiếp cho sales, cấu hình luật chấm điểm lead, cập nhật catalog sản phẩm/báo giá, phê duyệt kế hoạch kinh doanh của Agent, xem báo cáo KPI Dashboards (đa kênh, phễu lead, hiệu suất AI), xuất tệp dữ liệu CSV/Excel, cấu hình lịch gửi báo cáo tự động và nhận các báo cáo dự báo cơ hội (Forecast).
*   **Marketer (Nhân viên tiếp thị - Đại diện mảng Tiếp thị & Nội dung):** Gánh toàn bộ các luồng liên quan đến: Lên lịch xuất bản bài đăng, thiết lập chiến dịch Drip, quét xu hướng nội dung, theo dõi kênh/bài viết của đối thủ, nhận cảnh báo biến động chi phí quảng cáo (CPL) và sinh prompt hình ảnh.

#### 2. Thực thể Hệ thống ngoài (External System Entities - Tương ứng với Secondary Actors)
*   **Pancake API:** Nền tảng trung gian đồng bộ tin nhắn và bình luận đa kênh (Facebook, Zalo).
*   **Meta API:** API kết nối Fanpage, Meta Business để đăng bài viết và tích hợp tài nguyên.
*   **Zalo OA:** Cổng kết nối trực tiếp với Zalo Official Account.
*   **Email Service:** Dịch vụ gửi email tự động (SMTP, AWS SES) cho báo cáo, OTP và gửi tài liệu.
*   **LLM & Embedding Provider API:** Dịch vụ AI cung cấp mô hình sinh ngôn ngữ lớn và sinh vector hóa.
*   **Ads API:** API quảng cáo ngoài cung cấp dữ liệu chi tiêu để tính toán chỉ số chi phí CPL.
*   **Browser Web Push API:** Hệ thống gửi thông báo đẩy trực tiếp tới trình duyệt của người dùng.

*(Lưu ý: Tác nhân thời gian `System Scheduler` không được đưa vào sơ đồ vì thời gian không tự sinh ra dữ liệu vật lý trao đổi với hệ thống).*

---

### PHẦN 3: BẢNG LUỒNG DỮ LIỆU (DATA FLOWS)

Dưới đây là chi tiết các luồng dữ liệu (Data Flows) trao đổi giữa Clawbot System và từng Thực thể ngoại vi sau khi đã được khử trùng lặp và phân chia chuẩn nghiệp vụ.

#### 1. Thực thể: Customer (Khách hàng)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `Incoming Chat Messages` | Gửi tin nhắn/bình luận/câu hỏi qua Widget website hoặc Zalo/Facebook (UC-10, UC-25). |
| **Inbound** | `Document Access Requests` | Yêu cầu xem trước trực tuyến hoặc tải file PDF tài liệu về máy (UC-51). |
| **Inbound** | `FAQ Page Requests` | Gửi truy vấn tra cứu danh sách FAQ trên trang hỗ trợ công khai (UC-43). |
| **Inbound** | `Widget Bootstrap Requests` | Gửi yêu cầu khởi động và lấy tài nguyên giao diện khung chat (UC-42). |
| **Outbound** | `Outgoing Chat Replies` | Nhận phản hồi chat từ Sale (UC-15) hoặc câu trả lời tự động của RAG Chatbot (UC-25). |
| **Outbound** | `Auto Outbound Messages` | Nhận tin nhắn tự động ngoài giờ (UC-17) hoặc tin nhắn chăm sóc Drip (UC-41). |
| **Outbound** | `Widget Interface Assets` | Tải về cấu trúc giao diện và branding của Widget Chat (UC-42). |
| **Outbound** | `FAQ Content List` | Danh sách các câu hỏi thường gặp FAQ kết xuất từ kho tri thức (UC-43). |
| **Outbound** | `Generated Document Views` | Trực quan hóa nội dung tài liệu PDF trên trình duyệt (UC-51, UC-52). |

---

#### 2. Thực thể: Admin (Đại diện Hệ thống, AI & Tri thức)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `RBAC Role Configurations` | Định nghĩa vai trò mới và cấu hình phân quyền hạn chi tiết (UC-05). |
| **Inbound** | `API Key Management Requests` | Tạo mới hoặc thu hồi khóa API tích hợp với bên thứ ba (UC-06). |
| **Inbound** | `Pancake Connection Credentials` | Cung cấp Pancake Token kết nối và đồng bộ Fanpage (UC-09). |
| **Inbound** | `AI Provider Configuration` | Nhập API Key, Endpoint của LLM và Vector Database (UC-69). |
| **Inbound** | `AI Agent Settings` | Chọn Model AI, điều chỉnh System Prompt, bật/tắt kích hoạt Agent (UC-26, UC-27). |
| **Inbound** | `LLM Token Setting Updates` | Điều chỉnh giới hạn token tối đa và ngưỡng cảnh báo chi phí của agent (UC-29). |
| **Inbound** | `Skill File Configuration` | Tạo và tải lên các file kỹ năng Markdown (.md) cho Agent (UC-65). |
| **Inbound** | `Meta Integration Credentials` | Cung cấp Meta App ID/Secret và OAuth token để tích hợp cổng Meta Business (UC-66). |
| **Inbound** | `System Settings Configuration` | Cài đặt cấu hình chính sách hệ thống, trần ngân sách AI và thời gian nhường quyền (UC-07). |
| **Inbound** | `Knowledge Module Configuration` | Định nghĩa nhóm tri thức và phân quyền kiểm duyệt (UC-19). |
| **Inbound** | `Knowledge Version Control Commands` | Lệnh triển khai đồng bộ Vector hoặc rollback phiên bản tri thức cũ (UC-21, UC-22). |
| **Inbound** | `RAG Test Suite Configurations` | Xây dựng bộ câu hỏi/câu trả lời mẫu kiểm thử độ chính xác (UC-23). |
| **Inbound** | `Accuracy Test Run Triggers` | Ra lệnh kích hoạt tiến trình chạy đánh giá độ chính xác (UC-24). |
| **Inbound** | `AI Self-Learning Approvals` | Duyệt/Từ chối đề xuất tri thức tự học do AI đúc rút từ hội thoại (UC-64). |
| **Outbound** | `Agent Activity Traces` | Dấu vết suy nghĩ (Reasoning) và quá trình gọi Tool của Agent (UC-28). |
| **Outbound** | `Pixel Agents Office Layout View` | Giao diện trực quan hóa trạng thái và tiến trình Pixel Agents (UC-68). |
| **Outbound** | `Knowledge Base History` | Lịch sử phiên bản, thông tin thay đổi nội dung tri thức (UC-20). |
| **Outbound** | `RAG Accuracy Test Reports` | Kết quả điểm số Accuracy và Grounding của các lần test RAG (UC-24). |
| **Outbound** | `AI Self-Learning Recommendations` | Đề xuất tri thức AI tự học đang ở trạng thái chờ duyệt (UC-64). |
| **Outbound** | `LLM Token Usage Reports` | Báo cáo chi tiết số dư và chi tiêu API thực tế (UC-29). |
| **Outbound** | `Audit Logs` | Nhật ký hệ thống kiểm toán toàn bộ hoạt động nhạy cảm (UC-08). |
| **Outbound** | `In-App Notifications` | Nhận thông báo cấu hình lỗi hoặc cảnh báo ngân sách hệ thống (UC-63). |

---

#### 3. Thực thể: Sale (Đại diện Bán hàng)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `Conversation Control Commands` | Gửi lệnh nhận việc, chuyển giao hội thoại, cập nhật trạng thái (Resolved/Escalated) (UC-14, UC-16, UC-62). |
| **Inbound** | `Outbound Message Content` | Nhập nội dung tin nhắn, đính kèm file để gửi cho khách hàng (UC-15). |
| **Inbound** | `AI Draft Generation Requests` | Yêu cầu AI sinh bản thảo phản hồi dựa trên bối cảnh chat (UC-30). |
| **Inbound** | `Lead Profile Updates` | Cập nhật thông tin chi tiết, ghi chú, nhãn dán cho Lead (UC-38, UC-62). |
| **Inbound** | `Lead Stage Transition Commands` | Chuyển trạng thái Lead sang Won/Lost kèm theo lý do cụ thể (UC-72). |
| **Inbound** | `Lead Duplicate Merge Commands` | Yêu cầu gộp các hồ sơ Lead bị trùng số điện thoại/email (UC-39). |
| **Inbound** | `PDF Quote Generation Requests` | Yêu cầu hệ thống tự động xuất tệp PDF báo giá từ template (UC-50). |
| **Inbound** | `Document Send Commands` | Gửi tài liệu kèm mã tracking beacon theo dõi lượt mở (UC-52). |
| **Inbound** | `Quick Reply Template Configuration` | Quản lý, thêm/sửa/xóa các mẫu câu trả lời nhanh soạn sẵn (UC-34). |
| **Outbound** | `Unified Inbox Interface` | Giao diện danh sách cuộc chat đa kênh đã lọc và sắp xếp (UC-12, UC-13). |
| **Outbound** | `Lead Kanban Pipeline View` | Giao diện bảng Kanban trực quan hóa các giai đoạn phễu Lead (UC-37). |
| **Outbound** | `Lead Context Sidebar` | Bảng hiển thị thông tin bối cảnh, điểm số Lead khi đang chat (UC-32). |
| **Outbound** | `AI Draft Suggestions` | Gợi ý soạn thảo văn bản trả lời do AI đề xuất (UC-30). |
| **Outbound** | `AI Product Upsell Suggestions` | Danh sách sản phẩm/khóa học gợi ý bán thêm kèm luận điểm chốt đơn (UC-33). |
| **Outbound** | `Generated Documents` | File PDF tài liệu/báo giá được tạo ra từ hệ thống (UC-51). |
| **Outbound** | `Quick Reply List` | Danh sách các mẫu câu trả lời nhanh để Sale chọn lựa (UC-34). |
| **Outbound** | `In-App Notifications` | Thông báo đẩy trong ứng dụng khi có tin nhắn mới hoặc sự kiện quá hạn SLA (UC-63, UC-35). |

---

#### 4. Thực thể: SalesLead (Đại diện Quản trị kinh doanh & Báo cáo)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `Employee Account Management Requests` | Tạo mới, khóa hoặc cập nhật tài khoản nhân viên tư vấn (UC-04). |
| **Inbound** | `Conversation Channel Assignment Config` | Chỉ định nhân viên sales phụ trách các kênh fanpage cụ thể (UC-70). |
| **Inbound** | `Lead Scoring Rule Updates` | Điều chỉnh trọng số, quy tắc chấm điểm lead nóng/ấm/lạnh (UC-36). |
| **Inbound** | `Product Catalog Updates` | Cập nhật thông tin danh mục, mô tả, giá sản phẩm/dịch vụ (UC-73). |
| **Inbound** | `AI Agent Plan Approvals` | Duyệt hoặc từ chối kế hoạch thực thi công việc quan trọng của Agent (UC-59). |
| **Inbound** | `Report Export Requests` | Gửi yêu cầu trích xuất dữ liệu ra file Excel/CSV (UC-57). |
| **Inbound** | `Report Schedule Configuration` | Thiết lập lịch tự động gửi báo cáo hiệu suất qua email cho quản lý (UC-56). |
| **Outbound** | `KPI Dashboard Reports` | Báo cáo chuyển đổi phễu, KPI đa kênh và hiệu suất Sales vs AI (UC-53, UC-54, UC-55). |
| **Outbound** | `Exported Data Files` | Tệp dữ liệu báo cáo dạng CSV/Excel tải về máy (UC-57). |
| **Outbound** | `Forecast Reports` | Báo cáo phân tích dự báo cơ hội chốt đơn và chuyển đổi của Lead (UC-71). |
| **Outbound** | `In-App Notifications` | Cảnh báo quá hạn phản hồi SLA (UC-63, UC-35). |

---

#### 5. Thực thể: Marketer (Đại diện Tiếp thị & Nội dung)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `Content Brief Specifications` | Tạo tóm tắt yêu cầu viết bài (chủ đề, từ khóa, kênh đăng) (UC-45). |
| **Inbound** | `AI Content Generation Requests` | Yêu cầu AI viết nháp bài viết dựa trên brief được chọn (UC-46). |
| **Inbound** | `Image Prompt Generation Requests` | Yêu cầu AI soạn Prompt mô tả để sinh ảnh minh họa (UC-67). |
| **Inbound** | `Competitor Tracking Configurations` | Cấu hình nguồn theo dõi (website, RSS) của đối thủ (UC-61). |
| **Inbound** | `Drip Campaign Configurations` | Thiết lập chuỗi thời gian gửi tin nhắn chăm sóc Drip (UC-40). |
| **Outbound** | `AI Generated Content Drafts` | Bài thảo bản thảo do AI soạn thảo đưa vào hàng đợi (UC-46). |
| **Outbound** | `AI Generated Image Prompts` | Danh sách mô tả hình ảnh do AI đề xuất (UC-67). |
| **Outbound** | `Content Calendars` | Lịch trực quan hiển thị trạng thái các bài viết nháp/lên lịch/đã đăng (UC-47). |
| **Outbound** | `Competitor Post Updates` | Danh sách các bài đăng mới cào từ RSS đối thủ (UC-61). |
| **Outbound** | `In-App Notifications` | Cảnh báo biến động chi phí CPL, thông báo quét xong xu hướng hoặc gửi tin Drip thành công (UC-63, UC-58). |

---

#### 6. Thực thể: Pancake API (Hệ thống ngoài)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `Webhook Event Payloads` | Pancake đẩy tin nhắn chat, bình luận mới của khách hàng vào hệ thống (UC-10, UC-18). |
| **Outbound** | `Pancake Reply Payloads` | Hệ thống chuyển dữ liệu tin nhắn phản hồi đi để Pancake gửi lại kênh chat gốc (UC-15, UC-17, UC-41, UC-52). |

---

#### 7. Thực thể: Meta API (Hệ thống ngoài)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `Meta OAuth Tokens` | Nhận token xác thực quyền quản trị fanpage từ Facebook OAuth (UC-66). |
| **Inbound** | `Post Publishing Confirmations` | Meta xác nhận bài viết đã đăng xuất bản thành công trên Facebook (UC-48). |
| **Outbound** | `Meta Business Connection Configurations` | Gửi thông tin App ID/Secret để cấu hình kết nối (UC-66). |
| **Outbound** | `Scheduled Post Payload` | Gửi bài viết (văn bản, link ảnh) lên trang khi đến giờ đăng lịch trình (UC-48). |

---

#### 8. Thực thể: Zalo OA (Hệ thống ngoài)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `Zalo Webhook Message Payload` | Nhận tin nhắn chat mới trực tiếp từ webhook Zalo Official Account (UC-10). |
| **Outbound** | `Zalo Reply Messages` | Gửi phản hồi tin nhắn chat hoặc link tài liệu qua cổng Zalo OA (UC-15, UC-52). |

---

#### 9. Thực thể: Email Service (Hệ thống ngoài)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Outbound** | `OTP Delivery Payloads` | Gửi mã OTP xác nhận khôi phục mật khẩu tài khoản (UC-03). |
| **Outbound** | `Document Attachment Messages` | Gửi email đính kèm liên kết tài liệu theo dõi lượt mở cho khách hàng (UC-52). |
| **Outbound** | `Scheduled Report Emails` | Tự động gửi email đính kèm file báo cáo KPI định kỳ cho quản lý (UC-56). |

---

#### 10. Thực thể: LLM & Embedding Provider API (Hệ thống ngoài)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `AI Generated Responses` | Nhận kết quả text/intent/summary/forecast do mô hình ngôn ngữ lớn (LLM) trả về (UC-11, UC-23, UC-24, UC-25, UC-30, UC-31, UC-33, UC-46, UC-67, UC-71). |
| **Inbound** | `Vector Embedding Outputs` | Nhận chuỗi vector đại diện của văn bản tri thức để lưu Vector DB (UC-21). |
| **Outbound** | `LLM Generation Requests` | Gửi prompts và ngữ cảnh tri thức để sinh văn bản/câu trả lời (UC-11, UC-23, UC-24, UC-25, UC-30, UC-31, UC-33, UC-46, UC-67, UC-71). |
| **Outbound** | `Vector Embedding Requests` | Gửi đoạn văn bản tri thức thô để yêu cầu chuyển đổi sang dạng vector (UC-21). |

---

#### 11. Thực thể: Ads API (Hệ thống ngoài)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Inbound** | `Ad Spend Metrics` | Kéo dữ liệu chi tiêu quảng cáo thực tế để tính chỉ số biến động CPL (UC-58). |

---

#### 12. Thực thể: Browser Web Push API (Hệ thống ngoài)

| Chiều giao tiếp | Tên luồng dữ liệu (Data Label) | Mục đích / Tương ứng với Use Case |
| :--- | :--- | :--- |
| **Outbound** | `Web Push Notifications` | Đẩy gói thông báo SignalR / Web Push về sự kiện phát sinh tới trình duyệt nhân viên (UC-63). |
