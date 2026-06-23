# ClawBot — Kịch bản demo luồng mới nhất

> Tài liệu này dùng để demo ClawBot theo đúng trạng thái code mới nhất đã triển khai. Nội dung viết cho ban giám đốc, nhà đầu tư và người ra quyết định. Mục tiêu là kể một câu chuyện sản phẩm rõ ràng, dễ hiểu, có giá trị kinh doanh, nhưng vẫn trung thực về phần nào đã có thể demo và phần nào cần credential hoặc dữ liệu thật.

---

## 1. Mục tiêu demo

Buổi demo cần chứng minh ClawBot không chỉ là một chatbot đơn lẻ, mà là một hệ thống vận hành bán hàng và marketing có AI hỗ trợ từ đầu đến cuối:

1. Khách hàng có thể đi vào hệ thống từ nhiều điểm chạm.
2. Hội thoại được gom về một inbox thống nhất.
3. Sale được AI hỗ trợ đọc nhanh ngữ cảnh, soạn phản hồi, dùng quick reply và nhận gợi ý bước tiếp theo.
4. Lead được chấm điểm, phân loại, giao cho người phụ trách và theo dõi trong pipeline.
5. Hệ thống có thể tạo báo giá, brochure, onboarding kit và nội dung marketing.
6. Ban quản lý xem được KPI, chi phí AI, hiệu quả agent, cảnh báo và trace vận hành.
7. Admin kiểm soát người dùng, quyền, tích hợp, prompt, model, token quota và orchestration.

Thông điệp chính cho người nghe:

> “ClawBot giúp trung tâm gom khách từ nhiều kênh, giảm bỏ sót hội thoại, tăng tốc phản hồi của sale, ưu tiên lead nóng, tạo tài liệu nhanh hơn, đo được hiệu quả vận hành và kiểm soát chi phí AI.”

---

## 2. Audience và cách trình bày

Audience chính: **ban giám đốc / nhà đầu tư**.

Cách trình bày nên theo thứ tự:

1. Nói vấn đề kinh doanh trước.
2. Mở màn hình chứng minh sau.
3. Không đọc endpoint hoặc code nếu người nghe không hỏi.
4. Với phần cần điều kiện bên ngoài, nói rõ là “đường code và UI đã có, cần credential hoặc dữ liệu thật để chứng minh live”.
5. Không nói quá phần chưa verify live.

Không nên trình bày theo kiểu:

- “Đây là endpoint A, đây là endpoint B.”
- “Module này có class X, service Y.”
- “Tất cả vendor đều đã chạy live” nếu chưa có credential/payload thật.

Nên trình bày theo kiểu:

- “Đây là cách sale nhìn thấy khách đang cần phản hồi.”
- “Đây là cách AI giúp sale soạn câu trả lời trong vài giây.”
- “Đây là cách hệ thống biết khách nào nóng để ưu tiên.”
- “Đây là cách ban quản lý kiểm soát chi phí và chất lượng AI.”

---

## 3. Checklist chuẩn bị trước demo

### 3.1. Tài khoản và quyền

- Có một tài khoản admin hoặc sales lead.
- Tài khoản có quyền xem dashboard, inbox, sale assist, leads, documents, content, analytics, notifications, agents, prompts, tokens, system admin và orchestration.
- Nếu demo 2FA, chuẩn bị sẵn mã hoặc flow fallback.

### 3.2. Dữ liệu mẫu

Chuẩn bị dữ liệu giả, không dùng dữ liệu khách thật:

- 3 hội thoại mẫu:
  - Khách hỏi học phí.
  - Phụ huynh hỏi khóa học cho con.
  - Khách đã để lại số điện thoại và muốn học thử.
- 5 lead mẫu:
  - 1 cold lead.
  - 2 warm lead.
  - 1 hot lead.
  - 1 lead đã chuyển sâu hơn trong pipeline nếu có dữ liệu.
- 3 quick reply mẫu:
  - Hỏi mục tiêu học.
  - Mời học thử.
  - Gửi thông tin học phí hoặc lộ trình.
- 2–3 document mẫu:
  - Báo giá.
  - Brochure HSK.
  - Onboarding kit hoặc slide demo.
- 3 content item mẫu:
  - Draft.
  - Approved.
  - Scheduled.
- Một vài notification mẫu:
  - Hot lead.
  - Idle conversation.
  - Anomaly hoặc system.
- Agent trace và token usage mẫu.

### 3.3. Dịch vụ và credential

Nếu demo live, cần kiểm tra:

- API và frontend chạy được.
- AgentService chạy được.
- Database có dữ liệu demo.
- LLM provider config có API key hợp lệ nếu muốn gọi AI live.
- Pancake credential hoặc webhook sample nếu muốn demo omnichannel live.
- SMTP/MinIO nếu muốn demo gửi tài liệu hoặc mở link production-like.
- Qdrant/embedder nếu muốn nói sâu về RAG/KB accuracy.

Nếu thiếu credential, vẫn có thể demo bằng dữ liệu đã chuẩn bị. Khi nói, dùng câu:

> “Phần này đường code và màn hình đã có. Trong môi trường demo hôm nay, chúng ta dùng dữ liệu mẫu vì credential live của vendor chưa được cấu hình.”

---

## 4. Kịch bản demo 12–15 phút

### Cảnh 1 — Mở đầu: bài toán kinh doanh (1 phút)

**Mục tiêu:** đặt ngữ cảnh trước khi mở sản phẩm.

Lời thoại gợi ý:

> “Trung tâm có nhiều nguồn khách: website, chat widget, Zalo, Facebook, TikTok, Instagram hoặc các kênh khác thông qua Pancake. Vấn đề là sale dễ bỏ sót tin nhắn, phản hồi không đều, lead nóng không được ưu tiên, còn ban quản lý khó biết kênh nào đang hiệu quả. ClawBot giải quyết bài toán này bằng cách gom hội thoại, hỗ trợ sale bằng AI, chấm điểm lead, tạo tài liệu bán hàng và đo KPI vận hành.”

Sau câu mở đầu, nói rõ:

> “Em sẽ demo theo một vòng khép kín: khách đi vào hệ thống, sale xử lý bằng AI, lead được theo dõi, tài liệu và content được tạo, cuối cùng ban quản lý kiểm soát agent, prompt, model và chi phí.”

---

### Cảnh 2 — Login và bảo mật nền tảng (1 phút)

**Màn hình:** Login, `/auth/login`, profile hoặc user menu.

**Điểm cần chứng minh:** hệ thống không phải demo vô danh; có login, JWT, RBAC, 2FA và phân quyền.

Lời thoại gợi ý:

> “Đầu tiên, người dùng đăng nhập bằng tài khoản nội bộ. ClawBot có phân quyền theo role và permission, hỗ trợ 2FA, khóa tài khoản khi đăng nhập sai nhiều lần và có audit/log cho hoạt động quản trị. Đây là nền tảng để mỗi sale, admin hoặc quản lý chỉ thấy đúng phần mình được phép xử lý.”

Nếu mở được profile:

> “Ở phần hồ sơ, người dùng có thể quản lý thông tin cá nhân, đổi mật khẩu, bật/tắt 2FA và xem lịch sử bảo mật.”

**Ghi chú kỹ thuật nếu bị hỏi:** chi tiết nằm trong [login-flow.md](./login-flow.md). Hiện access token lưu phía frontend; production hardening có thể cải thiện bằng httpOnly cookie/refresh token.

---

### Cảnh 3 — Dashboard tổng quan: bắt đầu bằng KPI (1–2 phút)

**Màn hình:** Dashboard, Analytics overview.

**Điểm cần chứng minh:** ban quản lý nhìn thấy sức khỏe vận hành trước khi đi vào từng hội thoại.

Lời thoại gợi ý:

> “Sau khi đăng nhập, quản lý nhìn thấy bức tranh tổng quan: lượng lead, tin nhắn, phản hồi, conversion, chi phí agent và các chỉ số theo kênh. Đây là phần giúp ban giám đốc không cần hỏi từng nhân viên mà vẫn nắm được hôm nay hệ thống đang vận hành thế nào.”

Nêu các nhóm dữ liệu:

- KPI đa kênh.
- Funnel hoặc conversion.
- Agent performance.
- Agent cost.
- Forecast và anomaly nếu có dữ liệu.

Nếu dữ liệu còn mẫu:

> “Dữ liệu ở đây là dữ liệu demo, nhưng màn hình và API đã được wire theo backend hiện tại.”

---

### Cảnh 4 — Public widget / Support page: khách hàng đi vào hệ thống (1 phút)

**Màn hình:** `/chat-widget/{tenantSlug}` hoặc `/support/{tenantSlug}`.

**Điểm cần chứng minh:** ClawBot có bề mặt public cho khách ngoài hệ thống, không chỉ là dashboard nội bộ.

Lời thoại gợi ý:

> “Đây là điểm chạm public. Khách có thể vào widget hoặc trang hỗ trợ theo thương hiệu của tenant. Khi khách để lại thông tin hoặc nhắn câu hỏi, hệ thống tạo contact, mở conversation, tạo lead warm và đẩy về inbox nội bộ. Như vậy vòng bán hàng bắt đầu từ khách thật, không phải nhập tay trong CRM.”

Nếu demo support page:

> “Trang support lấy FAQ từ Knowledge Base đã active, nên cùng một nguồn tri thức có thể phục vụ cả khách hàng bên ngoài và agent bên trong.”

**Điều kiện:** cần tenant slug và branding. Nếu chưa có dữ liệu KB, chỉ demo bootstrap/branding và nói FAQ cần bộ KB thật.

---

### Cảnh 5 — Unified Inbox: sale không bỏ sót hội thoại (2 phút)

**Màn hình:** Inbox, conversation detail.

**Điểm cần chứng minh:** hội thoại được gom, lọc, sắp xếp ưu tiên và cập nhật realtime.

Lời thoại gợi ý:

> “Đây là inbox thống nhất. Sale không phải mở nhiều nền tảng rời rạc. Hội thoại từ widget hoặc kênh tích hợp được đưa về một nơi, có trạng thái, nền tảng, người phụ trách và độ ưu tiên. Những lead có điểm cao được ưu tiên lên trước để sale xử lý ngay.”

Mở một conversation mẫu:

> “Khi mở hội thoại, sale thấy lịch sử tin nhắn, thông tin khách, trạng thái xử lý và context liên quan. Đây là phần giúp sale không phải hỏi lại từ đầu nếu khách đã tương tác trước đó.”

Nếu có SignalR/realtime:

> “Khi có tin mới hoặc trạng thái thay đổi, hệ thống có thể đẩy realtime xuống giao diện.”

**Điều kiện:** Pancake live payload cần credential và mẫu thật. Nếu chưa có, dùng conversation mẫu.

---

### Cảnh 6 — Sale Assist: AI hỗ trợ sale phản hồi nhanh hơn (2 phút)

**Màn hình:** Conversation view + Sale Assist panel.

**Điểm cần chứng minh:** AI không thay sale hoàn toàn; AI giúp sale đọc nhanh, soạn nháp và chuẩn hóa phản hồi.

Lời thoại gợi ý:

> “Ở đây AI hỗ trợ sale theo đúng ngữ cảnh hội thoại. Sale có thể bấm tạo draft, tóm tắt hội thoại dài, dùng quick reply và xem gợi ý bước tiếp theo. Điểm quan trọng là sale vẫn là người quyết định gửi, còn AI giảm thời gian đọc và soạn.”

Thao tác nên demo:

1. Bấm tạo draft.
2. Xem draft trả lời.
3. Bấm summary nếu hội thoại dài.
4. Chọn quick reply.
5. Nói về upsell hoặc daily summary nếu có dữ liệu.

Lời thoại khi draft xuất hiện:

> “Câu trả lời được tạo dựa trên lịch sử hội thoại và Knowledge Base. Với dữ liệu thật, phần này giúp sale phản hồi nhất quán hơn, tránh quên thông tin và giảm thời gian gõ lặp lại.”

Nếu LLM key chưa cấu hình:

> “Trong môi trường này, phần draft cần LLM provider config hợp lệ. Nếu chưa có key live, chúng ta có thể dùng draft mẫu để trình bày cùng luồng.”

---

### Cảnh 7 — Lead pipeline: từ hội thoại thành cơ hội bán hàng (2 phút)

**Màn hình:** Leads list, Kanban, lead detail.

**Điểm cần chứng minh:** hệ thống không chỉ chat, mà chuyển dữ liệu chat thành pipeline bán hàng.

Lời thoại gợi ý:

> “Sau khi khách tương tác, hệ thống theo dõi lead trong pipeline. Lead có điểm, stage, người phụ trách và lịch sử activity. Các hành vi như hỏi giá, để lại số điện thoại hoặc đặt lịch học thử có thể làm tăng điểm. Khi đạt ngưỡng hot, lead được ưu tiên và có cảnh báo nội bộ.”

Thao tác nên demo:

1. Mở danh sách lead.
2. Lọc hoặc chỉ vào hot/warm/cold.
3. Mở lead detail.
4. Xem timeline/context.
5. Nếu an toàn, tạo activity mẫu để thấy score/stage đổi.
6. Xem assign hoặc forecast.

Lời thoại thêm:

> “Điểm mạnh ở đây là sale không phải tự nhớ khách nào quan trọng. Hệ thống biến tín hiệu từ hội thoại thành dữ liệu quản lý được.”

**Lưu ý:** hot-lead alert hiện đi qua SignalR / in-app notification theo hướng sản phẩm hiện tại, không trình bày Telegram là kênh chính.

---

### Cảnh 8 — Document automation: tạo báo giá và bộ tài liệu nhanh (1–2 phút)

**Màn hình:** Documents.

**Điểm cần chứng minh:** sale không phải tự soạn báo giá thủ công mỗi lần.

Lời thoại gợi ý:

> “Khi khách đã có nhu cầu rõ hơn, sale có thể tạo báo giá hoặc bộ tài liệu như brochure, onboarding kit hoặc slide demo. Hệ thống dùng template, dữ liệu khách và branding của tenant để tạo tài liệu nhất quán.”

Thao tác nên demo:

1. Mở document library.
2. Chọn generated document mẫu.
3. Nếu môi trường ổn, tạo báo giá hoặc generate kit.
4. Mở preview/link nếu có.

Lời thoại:

> “Giá trị ở đây là giảm thời gian tạo tài liệu từ nhiều phút xuống rất nhanh, đồng thời đảm bảo tài liệu đúng thương hiệu và có thể truy vết ai tạo, tạo cho khách nào, gửi lúc nào.”

**Điều kiện:** gửi email/Zalo hoặc storage link cần SMTP/MinIO/Pancake config nếu muốn demo live.

---

### Cảnh 9 — Content và Research: marketing không bị tách khỏi sale (1 phút)

**Màn hình:** Content brief editor, queue, calendar.

**Điểm cần chứng minh:** hệ thống hỗ trợ cả marketing pipeline, không chỉ sale inbox.

Lời thoại gợi ý:

> “Bên cạnh sale, ClawBot có phần content pipeline. Marketing có thể tạo brief, scan trend, sinh nội dung theo nền tảng, approve, schedule và theo dõi calendar. Như vậy dữ liệu bán hàng và hoạt động marketing nằm trong cùng một hệ thống vận hành.”

Nếu có item mẫu:

> “Ở đây ta thấy các trạng thái từ draft đến approved và scheduled. Với credential publisher thật, luồng này có thể nối sang công cụ đăng bài hoặc vendor tương ứng.”

**Điều kiện:** native publishing hoặc vendor publisher cần credential thật. Nếu chưa có, demo queue/calendar là đủ.

---

### Cảnh 10 — Analytics và Notifications: quản lý bằng dữ liệu (1 phút)

**Màn hình:** Analytics, notification center.

**Điểm cần chứng minh:** ban quản lý không chỉ xem hoạt động, mà còn nhận cảnh báo và đo hiệu quả.

Lời thoại gợi ý:

> “Phần analytics giúp quản lý xem kênh nào tạo lead, conversion ra sao, agent tốn bao nhiêu chi phí, có bất thường nào về KPI hoặc chi phí không. Notification center gom các cảnh báo như hot lead, hội thoại chờ lâu, anomaly, budget hoặc system event.”

Nói rõ:

> “Thiết kế hiện tại dùng SignalR và in-app notification làm kênh cảnh báo chính, thay vì phụ thuộc Telegram.”

---

### Cảnh 11 — Agent Dashboard, Prompt Config, Token Quota, Logs (2 phút)

**Màn hình:** Agents, Prompts, Tokens, Logs.

**Điểm cần chứng minh:** ClawBot có lớp vận hành AI, không phải gọi model kiểu hộp đen.

Lời thoại gợi ý:

> “Một điểm quan trọng với hệ thống AI là phải vận hành được. Ở đây admin có thể xem agent nào đang bật, agent chạy ra sao, trace từng task, prompt gốc đang dùng gì, test prompt trong sandbox và kiểm soát token quota theo agent hoặc tenant.”

Thao tác nên demo:

1. Mở Agent dashboard.
2. Chọn một agent, xem settings/traces.
3. Mở Prompts, xem config và sandbox.
4. Mở Tokens, xem usage/quota/router tier.
5. Mở Logs, xem task runs/audit/trace.

Lời thoại:

> “Điều này giúp ban quản lý kiểm soát chất lượng AI, chi phí AI và có bằng chứng khi cần audit. Nếu một phản hồi sai hoặc tốn chi phí bất thường, hệ thống có trace để lần lại.”

---

### Cảnh 12 — LLM Provider Config và Dynamic Orchestration (1–2 phút)

**Màn hình:** LLM provider settings, Orchestration nếu đã có UI/route phù hợp.

**Điểm cần chứng minh:** hệ thống tiến tới cấu hình model theo tenant/agent và điều phối nhiều agent theo mục tiêu.

Lời thoại gợi ý cho LLM config:

> “ClawBot không nên bị khóa cứng vào một API key hoặc một model trong file cấu hình. Phần LLM provider config cho phép tenant admin thêm provider, chọn model, cấu hình base URL, test connection, xoay key và bind provider cho từng agent. API key được lưu mã hóa và không trả plaintext về UI.”

Lời thoại gợi ý cho orchestration:

> “Bước tiếp theo của vận hành AI là không chỉ bấm từng agent riêng lẻ. Dynamic orchestration cho phép người dùng nhập mục tiêu, hệ thống lập kế hoạch thành các task cho nhiều agent, chạy theo dependency và ghi trace. Đây là nền cho các chiến dịch phức tạp như ra mắt khóa học mới: research, content, ads, report cùng phối hợp.”

Nếu orchestration cần LLM config hoặc dữ liệu:

> “Phần này cần LLM provider config hợp lệ cho orchestrator. Nếu chưa có key live, ta demo bằng plan/trace mẫu hoặc trình bày UI và dữ liệu đã ghi.”

---

### Cảnh 13 — System Admin: vận hành tenant và tích hợp (1 phút)

**Màn hình:** System admin.

**Điểm cần chứng minh:** sản phẩm có lớp quản trị, không phải hardcode vận hành.

Lời thoại gợi ý:

> “Ở lớp quản trị, admin có thể quản lý user, role, permission, API key, cấu hình Pancake, branding tenant và audit logs. Đây là phần biến ClawBot từ một demo kỹ thuật thành một sản phẩm có thể vận hành trong tổ chức.”

Nêu nhanh:

- User management.
- RBAC.
- API keys.
- Pancake integration.
- Tenant branding.
- Audit logs.

---

### Cảnh 14 — Kết luận: giá trị tổng thể (1 phút)

Lời thoại kết thúc:

> “Tóm lại, ClawBot tạo một vòng vận hành khép kín: khách vào từ nhiều điểm chạm, hội thoại về một inbox, sale được AI hỗ trợ, lead được chấm điểm và ưu tiên, tài liệu và content được tự động hóa, ban quản lý xem được KPI và chi phí, còn admin kiểm soát agent, model, prompt, quyền và trace. Giá trị không nằm ở một chatbot riêng lẻ, mà ở việc biến toàn bộ quy trình bán hàng và marketing thành một hệ thống có dữ liệu, có AI hỗ trợ và có kiểm soát vận hành.”

Nếu muốn nêu roadmap:

> “Các phần cần credential hoặc dữ liệu thật như Pancake live payload, real LLM/embedder, SMTP/MinIO, publisher hoặc ads vendor sẽ là checklist go-live vận hành, không phải thay đổi kiến trúc lớn.”

---

## 5. Bảng đối chiếu live path và fallback path theo từng cảnh

| Cảnh demo | Live path khi staging đủ credential/dữ liệu | Fallback path khi thiếu credential/dữ liệu | Điều kiện / lưu ý |
|---|---|---|---|
| Login / RBAC / 2FA | Đăng nhập thật bằng user demo staging. | Dùng user đã đăng nhập sẵn nếu 2FA hoặc lockout gây gián đoạn. | Cần user có quyền đủ rộng. |
| Dashboard / Analytics | Load KPI từ dữ liệu staging. | Dùng KPI seed/demo rollup đã chuẩn bị. | Cần dữ liệu `kpi_daily` hoặc API analytics có sample. |
| Public widget / support page | Mở widget theo tenant slug và gửi message mới. | Mở widget/support với dữ liệu FAQ/branding mẫu, không gửi live. | Cần tenant branding và FAQ nếu demo support. |
| Inbox | Nhận conversation mới từ widget hoặc Pancake live. | Dùng conversation mẫu đã seed. | Pancake live cần credential/payload thật. |
| Sale Assist | Gọi draft/summary live qua LLM provider config. | Dùng draft mẫu hoặc conversation đã có trace/draft trước đó. | Nếu thiếu LLM key, không gọi live. |
| Lead scoring / pipeline | Tạo activity thật để score/stage đổi. | Dùng lead đã seed sẵn ở cold/warm/hot. | Cần scoring rules và lead mẫu. |
| Hot lead / idle / notification | Cho job/consumer tạo notification thật. | Dùng notification mẫu trong notification center. | Không trình bày Telegram là kênh chính. |
| Documents | Generate quote/kit live. | Mở generated document mẫu. | Gửi thật cần SMTP/MinIO/Pancake config. |
| Content / Research | Generate/scan/schedule live nếu publisher/LLM sẵn sàng. | Demo queue/calendar với item mẫu. | Publish/vendor cần credential thật. |
| Agent dashboard / traces | Chạy sandbox hoặc agent task để tạo trace mới. | Dùng agent session/trace mẫu. | Nên seed trace để demo ổn định. |
| Prompt configs | Chạy prompt sandbox live. | Mở config và giải thích sandbox cần LLM key. | Sandbox cần LLM provider hợp lệ. |
| Token quota | Hiển thị usage từ ledger thật. | Dùng token ledger mẫu. | Cần sample usage đủ rõ. |
| LLM provider config | Test connection live bằng key demo. | Demo masked config và trạng thái not configured/configured. | Không lộ plaintext key. |
| Dynamic orchestration | Nhập goal nhỏ và chạy plan live. | Mở plan/trace mẫu đã seed. | Cần LLM config cho orchestrator nếu chạy live. |
| Admin system | Demo user/RBAC/API key/Pancake/branding live. | Chỉ mở cấu hình mẫu, không sửa nếu sợ ảnh hưởng môi trường. | Cần admin role. |

Cách nói khi chuyển sang fallback:

> “Phần này có hai chế độ demo. Nếu staging đang có credential live thì em chạy trực tiếp. Nếu chưa có credential hoặc vendor chưa verify, em dùng dữ liệu mẫu đã seed để trình bày cùng một luồng nghiệp vụ, và ghi rõ điều kiện cần có để chạy live.”

---

## 6. Câu hỏi thường gặp khi demo

### Hỏi: Hệ thống có thay thế sale không?

Trả lời gợi ý:

> “Không. Thiết kế hiện tại là AI hỗ trợ sale, không thay sale hoàn toàn. AI giúp đọc nhanh ngữ cảnh, soạn draft, gợi ý bước tiếp theo và ưu tiên lead. Sale vẫn là người quyết định gửi, chốt và xử lý tình huống nhạy cảm.”

### Hỏi: ClawBot có thật sự đa kênh chưa?

Trả lời gợi ý:

> “Kiến trúc hiện tại dùng Pancake unified adapter để gom nhiều kênh về một đường tích hợp thay vì tự tích hợp từng vendor riêng lẻ. Code path cho webhook, ingestor, inbox và outbound đã có. Để chứng minh live với từng kênh cụ thể, cần credential và payload thật từ tenant Pancake.”

### Hỏi: AI trả lời dựa vào đâu?

Trả lời gợi ý:

> “AI dùng lịch sử hội thoại, prompt hệ thống và Knowledge Base/RAG khi có dữ liệu KB đã deploy. Với môi trường production, chất lượng phụ thuộc mạnh vào KB thật, test set và embedder/model thật.”

### Hỏi: Làm sao kiểm soát chi phí AI?

Trả lời gợi ý:

> “Hệ thống có token usage, cost ledger, quota settings, router tier và cảnh báo. Admin có thể xem agent nào tiêu tốn bao nhiêu và đặt ngưỡng kiểm soát.”

### Hỏi: Có dùng được model khác ngoài Claude không?

Trả lời gợi ý:

> “Có hướng cấu hình LLM provider theo tenant/agent. Admin có thể cấu hình provider Anthropic hoặc OpenAI-compatible, chọn model, base URL, test connection và bind cho từng agent. API key được lưu mã hóa, không trả plaintext về UI.”

### Hỏi: Nếu agent làm sai thì truy vết thế nào?

Trả lời gợi ý:

> “Có agent sessions, agent traces, task runs, audit logs và prompt sandbox. Khi có phản hồi sai hoặc chi phí bất thường, đội vận hành có thể xem lại agent nào chạy, input/output nào, prompt nào và trace nào liên quan.”

### Hỏi: Phần nào còn cần chuẩn bị để go-live?

Trả lời gợi ý:

> “Các phần code chính đã có nhiều, nhưng go-live cần checklist vận hành: credential live cho Pancake/LLM/SMTP/MinIO/publisher/vendor, KB tiếng Trung thật, test set accuracy, dữ liệu demo/production sạch, và smoke test môi trường.”

---

## 7. Checklist sau demo

Sau mỗi buổi demo, ghi lại:

- Người nghe hỏi gì nhiều nhất?
- Cảnh nào gây ấn tượng tốt?
- Cảnh nào bị dài hoặc khó hiểu?
- Có phần nào nói chưa đúng code thật không?
- Có màn nào thiếu dữ liệu đẹp không?
- Có credential/service nào làm demo bị gián đoạn không?
- Có cần tạo bản demo 30 phút cho đào tạo vận hành không?

Cập nhật tài liệu này sau mỗi lần rehearsal hoặc demo thật nếu phát hiện điểm chưa rõ.

---

## 8. Nguồn đối chiếu

- [docs/plan.md](./plan.md) — checklist frontend/backend mới nhất.
- [docs/module-checklist.md](./module-checklist.md) — trạng thái module theo P0/P1/P2.
- [docs/sale-flow.md](./sale-flow.md) — chi tiết sale/inbox/lead/Sale Assist.
- [docs/login-flow.md](./login-flow.md) — chi tiết login/auth.
- [docs/ai/requirements/2026-06-19-feature-llm-provider-config.md](./ai/requirements/2026-06-19-feature-llm-provider-config.md) — yêu cầu LLM provider config.
- [docs/ai/design/2026-06-19-feature-llm-provider-config.md](./ai/design/2026-06-19-feature-llm-provider-config.md) — thiết kế LLM provider config.
- [docs/ai/requirements/2026-06-20-feature-dynamic-agent-orchestration.md](./ai/requirements/2026-06-20-feature-dynamic-agent-orchestration.md) — yêu cầu dynamic orchestration.
- [docs/ai/design/2026-06-20-feature-dynamic-agent-orchestration.md](./ai/design/2026-06-20-feature-dynamic-agent-orchestration.md) — thiết kế dynamic orchestration.
