Mục lục
1. Tổng quan & mục tiêu thực tế
Mục tiêu cốt lõi: Xây dựng hệ thống bán hàng tự động đa nền tảng (Zalo, Facebook, TikTok,
Instagram, YouTube) với Knowledge Base tiếng Trung chuyên sâu, để Agent tư vấn chuẩn xác 24/7.
1 sale có thể chăm sóc gấp 3× khách hàng nhờ AI hỗ trợ soạn thảo và phân loại.
5 mục tiêu chính theo thứ tự ưu tiên
2. Kiến trúc Omnichannel Sales Automation
Tổng quan kiến trúc 4 tầng
Luồng xử lý đa nền tảng
Khách nhắn tin (bất kỳ kênh):
Tin nh n DM → Webhook n8n → Phân loại n n t ng → Agent-Chat đọc KB + SKILL → Claude tr lời → Reply đúng n n t ng
Comment hỏi mua:
Comment TikTok/FB/IG/YT → Phân loại intent → H i giá/lịch: reply template + mời DM → H i khác: reply thông thường
Lead từ chat → Marketing:
DM tư v n → Agent-Lead score → Nóng ≥70: assign sale + alert → m: drip sequence → Lạnh: remarketing 5 kênh
Sinh content hàng tuần:
Thứ 2 sáng: Agent-Research tìm trend → Claude sinh content list 5 kênh → Notion queue → Approve
→ Auto-schedule
Tạo tài liệu theo yêu cầu:
Sale nhập nhu c u khách → Agent-Docs đọc KB → Claude sinh báo giá PDF cá nhân hóa → G i qua
Zalo/email trong 30s
3. Knowledge Base tiếng Trung — Trung tâm hệ thống
Knowledge Base là tài sản quan trọng nhất dự án — quan trọng hơn cả code. Agent chỉ tư vấn tốt khi có KB chính xác và đầy đủ. Phải xây KB trước khi viết bất kỳ dòng code nào.
Cấu trúc Knowledge Base (6 module)
Quy trình xây Knowledge Base (4 bước, tuần 1–2)
4. 50 Kịch bản hội thoại bán hàng đa nền tảng
50 kịch bản được phân loại theo tình huống thực tế, mỗi kịch bản bao gồm: trigger (tình huống kích hoạt), tone phù hợp theo nền tảng, và các bước xử lý. Agent-Chat đọc kịch bản này kết hợp với KB để trả lời.
Nhóm 1: Tin nhắn đầu tiên — Chào hỏi & Khám phá nhu cầu (8 kịch bản)
Nhóm 2: Tư vấn lộ trình & Giáo trình (8 kịch bản)
Nhóm 3: Xử lý objection phổ biến (10 kịch bản)
Nhóm 4: Dẫn dắt đến hành động (8 kịch bản)
Nhóm 5: Xử lý đặc thù từng nền tảng (8 kịch bản)
Nhóm 6: Follow-up & Tái kích hoạt (8 kịch bản)
5. 8 AI Agent & Phân công team 5 người
5 người thật — vai trò tối ưu cho bán hàng đa nền tảng
8 AI Agent — thêm Agent-Docs và Agent-SaleAssist so với v1
6. Hệ thống SKILL.md
7. 240 Use Case đầy đủ
Tổng cộng 240 UC: 120 UC nghiệp vụ (mục 7.1) + 120 UC hệ thống phần mềm (mục 12). Phần này liệt kê 120 UC nghiệp vụ được tái cấu trúc theo target đa nền tảng.
Nhóm A: Omnichannel Inbox & Routing (10 UC)
Nhóm B: Knowledge Base & Tư vấn AI (12 UC)
Nhóm C: Sale Assist — 1 người chăm 3× khách (10 UC)
Nhóm D: Lead Scoring & Marketing Data (10 UC)
Nhóm E: Content Marketing — Trend & Lên danh sách (10 UC)
Nhóm F: Nurture Sequences đa nền tảng (8 UC)
Nhóm G: Chăm sóc học viên (8 UC)
Nhóm H: Ads & Paid Media (8 UC)
Nhóm I: Báo cáo tổng hợp 5 kênh (8 UC)
Nhóm J: Internal Operations (6 UC)
Nhóm K: Use Case phát sinh T8–T13 (10 UC)
8. Kiến trúc kỹ thuật & Tech Stack
9. Lộ trình 13 tuần — Knowledge Base trước, mở rộng kênh sau
10. Bot workflow chi tiết
Luồng 1: Khách nhắn DM → Tư vấn → Chốt (5 kênh)
Luồng 2: Comment → Inbox → Convert sang DM
Luồng 3: Agent-Research → Content List → 5 kênh
Luồng 4: Báo giá PDF trong 30 giây
11. Đặc tả yêu cầu hệ thống phần mềm
11.1 Yêu cầu chức năng
11.2 Yêu cầu phi chức năng
12. 120 Use Case hệ thống phần mềm
SW-01: Xác thực & Phân quyền (10 UC)
SW-02: Omnichannel Inbox Management (12 UC)
SW-03: Knowledge Base Management (12 UC)
SW-04: AI Agent Management (12 UC)
SW-05: Sale Assist Interface (10 UC)
SW-06: Lead & CRM Management (12 UC)
SW-07: Content & Document Management (10 UC)
SW-08: Analytics & Reporting (10 UC)
SW-09: Integrations (8 UC)
SW-10: Admin & System (10 UC)
SW-11: Document Generation Engine (8 UC)
SW-12: KB Accuracy & Quality (8 UC)
13. Thiết kế CSDL & API chính
Các bảng dữ liệu chính
API endpoints quan trọng nhất
14. Rủi ro & giảm thiểu
15. Chi phí & ROI dự kiến
Chi phí hạ tầng hàng tháng
KPI kỳ vọng cuối tuần 13
Tài sản dài hạn: Knowledge Base tiếng Trung là IP độc quyền của công ty. Càng hoạt động lâu, KB càng đầy đủ, agent càng chính xác. Đây là lợi thế cạnh tranh không thể sao chép nhanh.
Số | Nội dung | Trang
1. | Tổng quan & mục tiêu thực tế | 3
2. | Kiến trúc Omnichannel Sales Automation | 4
3. | Knowledge Base tiếng Trung — Trung tâm hệ thống | 5
4. | 50 Kịch bản hội thoại bán hàng đa nền tảng | 7
5. | 8 AI Agent & phân công team 5 người | 9
6. | Hệ thống SKILL.md | 10
7. | 240 Use Case đầy đủ | 11
8. | Kiến trúc kỹ thuật & Tech Stack | 17
9. | Lộ trình 13 tuần | 18
10. | Bot workflow chi tiết | 19
11. | Đặc tả yêu cầu hệ thống phần mềm | 20
12. | 120 Use Case hệ thống phần mềm | 21
13. | Thiết kế CSDL & API | 25
14. | Rủi ro & giảm thiểu | 26
15. | Chi phí & ROI | 27
16. | Phụ lục: Template SKILL.md & Prompt phỏng vấn | 28
Hạng mục | Chi tiết
Tên dự án | ClawBot Omnichannel AI — Bán hàng Đa nền tảng
Kênh bán hàng | Zalo OA, Facebook Messenger/Page, TikTok DM/Comment, Instagram DM/Comment, YouTube Comment
Phương án | 5 người thật + 8 AI Agent (ClawBot/CrewAI) + Knowledge Base chuyên sâu
Thời gian | 13 tuần (~3.5 tháng)
Use case | 240 UC: 120 nghiệp vụ + 120 hệ thống phần mềm
Core feature #1 | Knowledge Base tiếng Trung: giáo trình HSK, lộ trình, bảng giá, 100+ FAQ
Core feature #2 | 50 kịch bản hội thoại bán hàng đa nền tảng
Core feature #3 | Sale Assist: 1 sale chăm 3× khách, AI draft gợi ý, inbox ưu tiên
Core feature #4 | Content MKT: tìm trend tiếng Trung, sinh content list 5 kênh hàng tuần
Core feature #5 | Document automation: báo giá PDF, brochure, slide demo tự động
Ưu tiên | Mục tiêu | Kết quả kỳ vọng | Kênh chính
#1 | Chatbot bán hàng đa nền tảng 24/7 | Không miss tin nhắn. Tư vấn chuẩn xác theo knowledge base. | Zalo, FB, TikTok, IG, YT
#2 | Sale Assist — 1 người chăm 3× khách | Sale chỉ close deal, AI lo phần soạn thảo và phân loại. | Tất cả kênh
#3 | Phân loại lead & đẩy số về Marketing | Data từ 5 kênh tổng hợp real-time, phân loại tự động. | Unified dashboard
#4 | Content MKT: tìm trend, lên danh sách | Content calendar 5 nền tảng, trend tiếng Trung mỗi tuần. | TikTok, FB, IG, YT
#5 | Tạo tài liệu tự động | Báo giá PDF, brochure khóa học, slide demo trong 30 giây. | Tất cả kênh
Tầng | Thành phần | Chức năng
Tầng 0 Inbox Hub | n8n Webhook · Zalo OA API · Facebook Graph API · TikTok Business API · Instagram API · YouTube Data API | Tổng hợp tất cả DM + comment từ 5 nền tảng vào 1 luồng xử lý thống nhất. Phân loại: tin nhắn mới / comment hỏi mua / comment thường.
Tầng 1 Knowledge Base | knowledge-base-tieng-trung.md · Giáo trình HSK 1–6 · Lộ trình 6 mục tiêu · Bảng giá · 100+ FAQ | Kho tri thức trung tâm. Mọi agent đều đọc trước khi trả lời. Cập nhật thủ công khi có thay đổi giá/chương trình.
Tầng 2 Agent Brain | ClawBot SDK · CrewAI · Langflow · Claude API (Sonnet 4.6) · 8 AI Agents | 8 agent đọc Knowledge Base + SKILL tương ứng, ra quyết định và thực thi phù hợp từng nền tảng.
Tầng 3 Output | Sale Assist Dashboard · Omnichannel CRM · Metabase · Telegram Alert · Buffer/Later | Sale nhận lead nóng, nhìn thấy draft gợi ý, gửi báo giá PDF, xem KPI tổng hợp 5 kênh.
Module | Nội dung | Ai cung cấp | Cập nhật
KB-01 Giáo trình | HSK 1–6: số buổi, nội dung từng level, tốc độ học trung bình. TOCFL, YCT cho trẻ em. So sánh Hán ngữ tiêu chuẩn vs giáo trình riêng trung tâm. | Giám đốc học thuật | Khi có khóa mới
KB-02 Lộ trình học | 6 mục tiêu: Du lịch (3T) · Công việc (6T) · Thi HSK3 (4T) · Thi HSK5 (12T) · Học vui (linh hoạt) · Trẻ em (theo độ tuổi). Mỗi lộ trình: số buổi/tuần, milestone, chi phí tổng. | Trưởng bộ phận đào tạo | Theo quý
KB-03 Bảng giá | Giá từng gói (1 tháng/3 tháng/6 tháng/12 tháng). Giá học viên cũ, giá giới thiệu, combo gia đình. Chính sách ưu đãi hiện hành. Giá buổi học thử. | Giám đốc kinh doanh | Khi có thay đổi
KB-04 100+ FAQ | Thu thập từ log chat Zalo/FB thực tế: câu hỏi về học phí, lịch học, giáo viên, thiết bị, chứng chỉ, phương thức thanh toán, bảo lưu, chuyển lớp... | Team sale + QA | Hàng tháng
KB-05 Giáo viên | Profile từng GV: chuyên môn, phong cách dạy, lịch dạy, học viên phù hợp. Cách giới thiệu GV phù hợp với từng mục tiêu học viên. | HR + Học thuật | Khi GV mới/nghỉ
KB-06 Đặc thù nền tảng | Tone voice theo từng kênh: Zalo (thân thiện, tiếng Việt), TikTok (trẻ, có emoji), Instagram (visual, ngắn), Facebook (formal hơn), YouTube (informative). | Content team + QA | Khi rebranding
Bướ c | Hoạt động | Người thực hiện | Output
1 | Export toàn bộ log chat Zalo/FB 3 tháng qua. Tổng hợp 100 câu hỏi thực tế khách hay hỏi nhất. | P3 (Sales Lead) + P4 (QA) | 100-faq-raw.txt
2 | P3 + Giám đốc học thuật điền đầy đủ: giáo trình, lộ trình, bảng giá, thông tin GV vào template KB. | P3 + Học thuật + Kinh doanh | KB draft đầy đủ
3 | Claude format và chuẩn hóa thành knowledge-base.md với cấu trúc chuẩn để agent đọc hiệu quả. | P4 (QA) + Claude API | knowledge-base.md v1
4 | Test agent với 20 câu hỏi thực tế. So output với câu trả lời chuẩn của sale senior. Chỉnh sửa đến khi đạt ≥85% accuracy. | P4 + P3 review | KB v1 validated
Mã | Tình huống kích hoạt | Cách xử lý | Kênh
KB-001 | Khách nhắn 'hello/xin chào' không nói gì thêm | Hỏi mục tiêu học để định hướng đúng từ đầu | Tất cả
KB-002 | Khách hỏi 'học tiếng Trung ở đây như thế nào?' | Giới thiệu ngắn USP + hỏi mục tiêu để đề xuất lộ trình | Tất cả
KB-003 | Khách đến từ xem video TikTok/Reels | Kết nối với nội dung video vừa xem + chào đón tự nhiên | TikTok, IG
KB-004 | Khách đến từ click quảng cáo Facebook | Nhắc đến offer trong quảng cáo + tiếp nối tự nhiên | FB, Zalo
KB-005 | Khách nhắn 'cho hỏi học phí' ngay lập tức | KHÔNG đưa giá ngay — hỏi mục tiêu trước, sau đó đề xuất gói phù hợp + giá | Tất cả
KB-006 | Khách là phụ huynh hỏi cho con | Hỏi tuổi con, mục tiêu, giới thiệu chương trình YCT, lịch phù hợp trẻ | Tất cả
KB-007 | Người nước ngoài/Việt kiều nhắn bằng tiếng Anh | Switch sang English, giới thiệu chương trình phù hợp | FB, IG
KB-008 | Khách cũ quay lại sau thời gian dài | Nhận diện là khách cũ (nếu có data), chào đón đặc biệt, hỏi muốn tiếp tục từ đâu | Zalo, FB
Mã | Tình huống kích hoạt | Cách xử lý | Kênh
KB-009 | Khách muốn học để đi du lịch Trung Quốc | Lộ trình 3 tháng, focus giao tiếp, giới thiệu GV phù hợp | Tất cả
KB-010 | Khách muốn học để làm việc với đối tác TQ | Lộ trình 6 tháng business Chinese, focus từ vựng công việc | Tất cả
KB-011 | Khách muốn thi HSK3 | Lộ trình 4 tháng có timeline cụ thể, tỷ lệ đậu của học viên cũ | Tất cả
KB-012 | Khách muốn thi HSK5 hoặc cao hơn | Lộ trình 12+ tháng, yêu cầu đầu vào, cam kết thời gian | Tất cả
KB-013 | Khách đã học trước (tự học/trung tâm khác) | Hỏi level hiện tại, đề xuất test đầu vào miễn phí, vào thẳng level phù hợp | Tất cả
KB-014 | Khách không biết mình hợp level nào | Mời làm mini test miễn phí 5 câu ngay trong chat | Tất cả
KB-015 | Khách muốn học cho vui/sở thích | Lộ trình linh hoạt, nhấn mạnh vui học, không áp lực thi cử | Tất cả
KB-016 | Khách hỏi giáo trình có khác sách chuẩn không | Giải thích giáo trình riêng, so sánh ưu điểm, link video minh họa | Tất cả
Mã | Tình huống kích hoạt | Cách xử lý | Kênh
KB-017 | 'Học phí cao quá' | Chia nhỏ theo buổi, so sánh giá trị, hỏi ngân sách để đề xuất gói phù hợp | Tất cả
KB-018 | 'Bận quá không có thời gian' | Hỏi lịch cụ thể, giới thiệu lịch linh hoạt, buổi tối/cuối tuần | Tất cả
KB-019 | 'Học online không hiệu quả bằng offline' | Kể case học viên cụ thể, giải thích công nghệ, mời học thử để tự trải nghiệm | Tất cả
KB-020 | 'Để tôi suy nghĩ thêm' | Hỏi điều gì khiến còn băn khoăn, giải đáp cụ thể, đặt follow-up | Tất cả
KB-021 | So sánh với trung tâm X cụ thể | Không nói xấu đối thủ, làm nổi bật điểm khác biệt của mình, mời học thử so sánh | Tất cả
KB-022 | 'Tôi đã học ở chỗ khác thất bại rồi' | Đồng cảm, hỏi thất bại vì gì, giải thích cách dạy khác biệt của mình | Tất cả
KB-023 | 'Cho tôi xem học phí đã rồi tính' | Đưa range giá + giải thích tại sao cần biết mục tiêu để tư vấn đúng gói | Tất cả
KB-024 | 'Con tôi không thích học' | Giới thiệu phương pháp học qua game/video/bài hát cho trẻ em, mời học thử thử | Tất cả
KB-025 | Khách im lặng sau khi nhận giá | Đừng hỏi lại ngay — chờ 2h, gửi case study học viên tương tự | Tất cả
KB-026 | 'Chỗ kia rẻ hơn' | Hỏi rõ so sánh cái gì, giải thích chất lượng GV và kết quả học viên | Tất cả
Mã | Tình huống kích hoạt | Cách xử lý | Kênh
KB-027 | Mời học thử miễn phí | Đề xuất ngay sau khi biết mục tiêu, đặt lịch cụ thể, nhắc 24h trước | Tất cả
KB-028 | Khách sau buổi học thử chưa đăng ký | Gửi tóm tắt buổi học + lộ trình tiếp theo + offer deadline 48h | Tất cả
KB-029 | Đặt lịch học thử — thu thập thông tin | Tên, SĐT xác nhận, giờ học phù hợp, nền tảng muốn học (Zoom/app) | Tất cả
KB-030 | Khách sắp chốt nhưng còn 1 câu hỏi cuối | Giải đáp nhanh, đưa link thanh toán/form đăng ký ngay trong chat | Zalo, FB
KB-031 | Khách hỏi cách thanh toán | Liệt kê đầy đủ: chuyển khoản/ví điện tử/thẻ, gửi QR trong chat | Tất cả
KB-032 | Gửi báo giá PDF cá nhân hóa | Sau khi biết mục tiêu + lộ trình → Agent-Docs sinh PDF có tên khách + gói phù hợp | Zalo, FB, Email
KB-033 | Khách muốn đăng ký nhưng chưa có tiền ngay | Giới thiệu chính sách trả góp (nếu có), gói ngắn để bắt đầu | Tất cả
KB-034 | Upsell từ gói 1 tháng lên 3 tháng | Sau tuần 2: tính toán tiết kiệm khi mua dài hạn, đề xuất upgrade | Tất cả
Mã | Tình huống kích hoạt | Cách xử lý | Kênh
KB-035 | TikTok: Comment 'học phí bao nhiêu?' dưới video | Reply ngắn + mời DM: 'Học phí tùy lộ trình bạn ơi, bạn nhắn tin cho mình tư vấn cụ thể nhé!' | TikTok
Mã | Tình huống kích hoạt | Cách xử lý | Kênh
KB-036 | TikTok: Comment 'cho xin link đăng ký' | Reply kèm link bio hoặc mời DM để tư vấn trước khi đăng ký | TikTok
KB-037 | Instagram: DM từ xem Story/Reel | Kết nối với nội dung Story/Reel vừa xem, tự nhiên hơn | Instagram
KB-038 | Facebook: Khách comment vào livestream | Reply real-time trong comment, sau đó gửi DM để tư vấn sâu hơn | Facebook
KB-039 | YouTube: Comment 'học ở đâu vậy?' | Reply với info ngắn + link Zalo/FB để tư vấn chi tiết | YouTube
KB-040 | Zalo: Khách nhắn ngoài giờ hành chính | Reply tự động ngay lập tức, thông báo sale sẽ liên hệ sáng sớm | Zalo
KB-041 | Facebook Messenger: Khách từ quảng cáo click | Nhắc offer trong quảng cáo, cảm ơn quan tâm, tư vấn ngay | Facebook
KB-042 | Multi-channel: Khách nhắn cả Zalo lẫn Facebook | Nhận diện cùng 1 người, hợp nhất context, không tư vấn lại từ đầu | Tất cả
Mã | Tình huống kích hoạt | Cách xử lý | Kênh
KB-043 | Khách bỏ chat 1 ngày chưa reply | Gửi tin nhắn nhẹ nhàng với angle mới: video học thử, testimonial | Tất cả
KB-044 | Khách bỏ chat 3–7 ngày | Gửi content value (mẹo học tiếng Trung) + offer giới hạn thời gian | Tất cả
KB-045 | Lead lạnh sau 30 ngày im lặng | Re-activate với tin nhắn: 'Bạn có muốn bắt đầu trước Tết không?' (theo mùa) | Zalo, FB
KB-046 | Học viên cũ đã hoàn thành khóa | Upsell khóa nâng cao sau 2 tuần hoàn thành, kèm voucher 10% | Zalo
KB-047 | Học viên đang học bị vắng nhiều buổi | Hỏi thăm lý do, đề xuất bảo lưu hoặc đổi lịch | Zalo
KB-048 | Referral: Nhờ học viên giới thiệu bạn | Gửi link referral cá nhân + giải thích hoa hồng sau khi NPS ≥8 | Zalo
KB-049 | Mùa cao điểm: Tết Trung Quốc, mùa thi HSK | Broadcast đặc biệt với content phù hợp mùa + offer thời hạn | Tất cả
KB-050 | Học viên sau 3 tháng nghỉ muốn học lại | Chào đón như người thân, cập nhật những thay đổi mới, ưu đãi quay lại | Tất cả
Người | Vai trò | Trách nhiệm chính | Tải
P1 | AI Tech Lead | Setup ClawBot + CrewAI + Omnichannel Hub. Kết nối 5 API nền tảng. Orchestrate 8 agent. Review code. | 90%
P2 | Growth Strategist | Chiến lược content 5 kênh, ngưỡng KPI theo kênh, approve content agent tạo, trend research direction. | 80%
P3 | Sales Lead | Xây Knowledge Base (phỏng vấn chuyên gia, thu thập FAQ). Close deal lead nóng trên mọi kênh. Viết 50 kịch bản. | 85%
P4 | QA & Prompt Eng. | Test KB accuracy trên từng kịch bản. Tinh chỉnh prompt. Đảm bảo agent không trả lời sai thông tin. | 85%
P5 | PM / Data | Sprint planning, KPI dashboard 5 kênh, báo cáo agent tạo → ra quyết định điều chỉnh. | 75%
Agent | SKILL + KB | Chức năng chính | Kênh
Agent-Chat | 50 kịch bản + KB tiếng Trung + SKILL Lan | Tư vấn tự động 24/7, cá nhân hóa theo nền tảng, escalate khi cần | 5 kênh
Agent-SaleAssist | KB + kịch bản + lead history | Draft gợi ý tin nhắn cho sale, inbox ưu tiên, context panel, quick reply | 5 kênh
Agent-Lead | lead-scoring.md + 5-channel-rules.md | Score từ 5 kênh, phân loại, assign, drip, đẩy data về Marketing | 5 kênh
Agent-Content | content-copywriting.md + KB + platform-specs.md | Sinh content phù hợp từng nền tảng, caption TikTok/IG/FB/YT khác nhau | 5 kênh
Agent-Docs | KB + pricing.md + doc-templates.md | Báo giá PDF cá nhân hóa, brochure khóa học, slide demo, onboarding kit | Tất cả
Agent-Ads | ads-optimization.md + kpi.md | Tối ưu Meta Ads + TikTok Ads, pause/scale theo ngưỡng | Meta, TikTok
Agent-Report | kpi.md + reporting-omni.md | Báo cáo tổng hợp 5 kênh hàng ngày, alert anomaly, weekly summary | Tổng hợp
Agent-Research | trend-tieng-trung.md + competitor.md | Tìm trend tiếng Trung VN, gợi ý content idea list hàng tuần | TikTok, YT
File SKILL.md | Đúc kết từ | Nội dung cốt lõi | Tuần
knowledge-base-tieng-trung. md | Học thuật + Sales + BGĐ | Toàn bộ KB: giáo trình, lộ trình, bảng giá, FAQ, GV, đặc thù nền tảng | T1–2
50-chat-scenarios.md | P3 Sales Lead + log chat thực | 50 kịch bản hội thoại, phân loại theo nhóm, tone theo nền tảng | T2–3
zalo-sales-consultation.md | Sale senior giỏi nhất | Quy trình 5 bước, 5 objection chính, giờ vàng, dấu hiệu lead nóng | T2
platform-specs.md | Content team + P2 | Tone voice, format, limit ký tự, hashtag rule theo từng nền tảng | T2
ads-optimization.md | Ads specialist | Ngưỡng CPL, creative formula, scaling rule, dayparting | T3
content-copywriting.md | Content writer | Hook formula, tone voice thương hiệu, từ ngữ cấm | T3
doc-templates.md | P3 + BGĐ | Template báo giá, brochure, slide demo — cấu trúc và nội dung chuẩn | T3
lead-scoring.md | PM + Sales | Điểm theo hành vi từng kênh, ngưỡng nóng/ấm/lạnh, SLA phản hồi | T3
trend-tieng-trung.md | Content + Research | Trend tiếng Trung VN theo mùa, topic hot, hashtag trending | T4
ID | Use Case | Agent | Tần suất | Kênh
UC-A01 | Tổng hợp tất cả DM từ 5 nền tảng vào 1 inbox | n8n Hub | Realtime | Zalo, FB, TikTok, IG, YT
UC-A02 | Phân loại tin nhắn: tư vấn mua / hỗ trợ / spam | Agent-Chat | Realtime | Tất cả kênh
UC-A03 | Phân loại comment: hỏi mua / tương tác thường | Agent-Chat | Realtime | TikTok, FB, IG, YT
UC-A04 | Reply comment hỏi giá → mời DM để tư vấn | Agent-Chat | Realtime | TikTok, FB, IG
UC-A05 | Nhận diện khách nhắn từ nhiều kênh cùng lúc | Agent-Lead | Realtime | Cross-platform
UC-A06 | Priority queue: lead nóng lên đầu inbox | Agent-Lead | Realtime | Tất cả kênh
UC-A07 | Auto-reply ngoài giờ hành chính tất cả kênh | Agent-Chat | Realtime | Tất cả kênh
UC-A08 | Escalate sang sale khi agent không đủ thẩm quyền | Agent-Chat | Realtime | Tất cả kênh
UC-A09 | Gộp lịch sử conversation theo khách hàng | Agent-Lead | Realtime | Cross-platform
UC-A10 | SLA alert: tin chờ >10 phút chưa được reply | Agent-Report | Realtime | Tất cả kênh
ID | Use Case | Agent | Tần suất | Kênh
UC-B01 | Trả lời FAQ tiếng Trung từ Knowledge Base | Agent-Chat | Realtime | Tất cả kênh
UC-B02 | Đề xuất lộ trình học theo mục tiêu khách nêu | Agent-Chat | Realtime | Tất cả kênh
UC-B03 | Báo giá đúng gói theo lộ trình đề xuất | Agent-Chat | Realtime | Tất cả kênh
UC-B04 | Giới thiệu giáo viên phù hợp theo nhu cầu | Agent-Chat | Realtime | Tất cả kênh
UC-B05 | Mini test đầu vào 5 câu ngay trong chat | Agent-Chat | On request | Tất cả kênh
UC-B06 | Giải đáp câu hỏi về giáo trình chi tiết | Agent-Chat | Realtime | Tất cả kênh
UC-B07 | Xử lý 50 kịch bản hội thoại đã đặc tả | Agent-Chat | Realtime | Theo kênh
UC-B08 | Cập nhật KB khi giá/chương trình thay đổi | QA + Admin | Khi có TĐ | Tất cả
UC-B09 | Sinh báo giá PDF cá nhân hóa theo yêu cầu | Agent-Docs | On request | Zalo, FB, Email
ID | Use Case | Agent | Tần suất | Kênh
UC-B10 | Gửi brochure khóa học theo mục tiêu học | Agent-Docs | On request | Tất cả kênh
UC-B11 | Sinh slide demo 5 trang cho buổi học thử | Agent-Docs | On request | Zoom, Meet
UC-B12 | Tạo onboarding kit PDF cho học viên mới | Agent-Docs | Khi chốt | Email, Zalo
ID | Use Case | Agent | Tần suất | Kênh
UC-C01 | Inbox sale: tổng hợp tất cả chat theo ưu tiên | Agent-SaleAssist | Realtime | 5 kênh
UC-C02 | AI draft gợi ý tin nhắn phản hồi cho sale | Agent-SaleAssist | Realtime | 5 kênh
UC-C03 | Context panel: lịch sử + điểm + gợi ý bước tiếp | Agent-SaleAssist | Realtime | 5 kênh
UC-C04 | Quick reply templates 1-click theo tình huống | Agent-SaleAssist | On click | 5 kênh
UC-C05 | Alert: khách đang chat chờ >5 phút | Agent-SaleAssist | Realtime | 5 kênh
UC-C06 | Gợi ý giai đoạn tiếp theo trong pipeline | Agent-SaleAssist | Realtime | CRM
UC-C07 | Tóm tắt conversation dài trước khi sale đọc | Agent-SaleAssist | On request | 5 kênh
UC-C08 | Gợi ý upsell khi khách sắp chốt gói ngắn | Agent-SaleAssist | Realtime | 5 kênh
UC-C09 | Cảnh báo khi sale đang dùng từ ngữ không phù hợp | Agent-SaleAssist | Realtime | 5 kênh
UC-C10 | Daily summary: sale đã handle bao nhiêu, kết quả | Agent-Report | Cuối ngày | Dashboard
ID | Use Case | Agent | Tần suất | Kênh
UC-D01 | Scoring lead từ 5 kênh với trọng số khác nhau | Agent-Lead | Realtime | 5 kênh
UC-D02 | Phân loại Nóng ≥70 / Ấm 30–70 / Lạnh <30 | Agent-Lead | Realtime | 5 kênh
UC-D03 | Assign lead nóng + alert Telegram <2 phút | Agent-Lead | Realtime | 5 kênh
UC-D04 | Dedup lead nhắn từ nhiều kênh | Agent-Lead | Realtime | Cross-platform
UC-D05 | Đẩy data lead về Marketing dashboard tự động | Agent-Lead | Realtime | Dashboard
UC-D06 | Phân tích lead theo kênh: kênh nào chất lượng nhất | Agent-Report | Daily | Dashboard
UC-D07 | Drip sequence theo kênh (Zalo khác FB khác TikTok) | Agent-Lead | Auto | Theo kênh
UC-D08 | Remarketing lead lạnh trên đúng kênh họ đến | Agent-Lead | Weekly | Ads platforms
ID | Use Case | Agent | Tần suất | Kênh
UC-D09 | UTM tracking: source, medium, campaign từng kênh | Agent-Lead | Realtime | All channels
UC-D10 | Pipeline forecast tuần dựa trên lead 5 kênh | Agent-Report | Thứ 6 | Dashboard
ID | Use Case | Agent | Tần suất | Kênh
UC-E01 | Tìm trend tiếng Trung VN hàng tuần (TikTok, YT) | Agent-Research | Thứ 2 | TikTok, YT
UC-E02 | Lên content idea list 5 kênh mỗi tuần | Agent-Research | Thứ 2 | Tất cả kênh
UC-E03 | Sinh content cụ thể theo format từng nền tảng | Agent-Content | Daily | Từng kênh
UC-E04 | TikTok: hook 3 giây, caption ngắn, hashtag trend | Agent-Content | Daily | TikTok
UC-E05 | Instagram: caption visual, story text, Reels hook | Agent-Content | Daily | Instagram
UC-E06 | Facebook: caption dài hơn, CTA rõ ràng hơn | Agent-Content | Daily | Facebook
UC-E07 | YouTube: title SEO, description, chapter markers | Agent-Content | Tuần | YouTube
UC-E08 | Content calendar tổng hợp 5 kênh, 1 tháng | Agent-Content | Đầu tháng | Notion
UC-E09 | Repurpose 1 video TikTok → Reels + YT Shorts | Agent-Content | Theo video | IG, YT
UC-E10 | Trend alert: topic nóng về tiếng Trung xuất hiện | Agent-Research | Realtime | Telegram
ID | Use Case | Agent | Tần suất | Kênh
UC-F01 | Drip Zalo 7 ngày sau lead điền form | Agent-Lead | Auto | Zalo
UC-F02 | Drip Facebook Messenger sequence | Agent-Lead | Auto | Facebook
UC-F03 | Voucher trigger khi lead xem giá 2 lần/24h | Agent-Lead | Realtime | Tất cả
UC-F04 | Demo booking flow tự động qua Zalo/Messenger | Agent-Chat | Realtime | Zalo, FB
UC-F05 | No-show follow-up sau buổi học thử | Agent-Lead | 2h sau | Theo kênh
UC-F06 | Post-demo: tóm tắt + lộ trình + offer 48h | Agent-Chat | 30' sau | Theo kênh
UC-F07 | Seasonal campaign: Tết, mùa thi HSK, back-to-school | Agent-Lead | Theo mùa | Tất cả
UC-F08 | Re-engage lead lạnh với content value | Agent-Lead | 30 ngày | Theo kênh
ID | Use Case | Agent | Tần suất | Kênh
UC-G01 | Welcome sequence học viên mới qua Zalo 5 ngày | Agent-Chat | Khi chốt | Zalo
UC-G02 | Nhắc lịch học 2h và 30 phút trước buổi | Agent-Chat | Scheduled | Zalo, FB
UC-G03 | Feedback form sau mỗi 4 buổi học | Agent-Chat | Scheduled | Zalo
UC-G04 | Cảnh báo học viên vắng >2 buổi liên tiếp | Agent-Lead | Daily | Zalo
UC-G05 | Chứng chỉ hoàn thành khóa tự động | Agent-Docs | Khi HT | Email, Zalo
UC-G06 | Upsell khóa tiếp theo theo progress | Agent-Lead | Khi gần HT | Zalo
UC-G07 | Referral program: tạo link + track hoa hồng | Agent-Lead | Request | Zalo, FB
UC-G08 | Re-activation học viên cũ theo mùa cao điểm | Agent-Lead | Tháng | Tất cả
ID | Use Case | Agent | Tần suất | Kênh
UC-H01 | Meta Ads: auto-pause adset CPL vượt ngưỡng | Agent-Ads | 4h | Meta
UC-H02 | Meta Ads: auto-scale adset CPL tốt +20% | Agent-Ads | 4h | Meta
UC-H03 | TikTok Ads: trigger Spark Ads khi video >5000 view | Agent-Ads | Realtime | TikTok
UC-H04 | Creative rotation khi frequency > 2 | Agent-Ads | Daily | Meta, TikTok
UC-H05 | Budget alert khi chi 90% ngân sách ngày | Agent-Ads | Realtime | Telegram
UC-H06 | Lookalike audience từ học viên đã chốt | Agent-Ads | Tuần | Meta, TikTok
UC-H07 | Remarketing lead lạnh đúng kênh họ từng dùng | Agent-Ads | Daily | Meta, TikTok
UC-H08 | Weekly ads performance report 2 kênh | Agent-Report | Thứ 2 | Dashboard
ID | Use Case | Agent | Tần suất | Kênh
UC-I01 | Daily report 7h30: lead, CPL, tin nhắn từng kênh | Agent-Report | 7h30 | Telegram
UC-I02 | Weekly: kênh nào đang tốt/kém nhất | Agent-Report | Thứ 2 | Dashboard
UC-I03 | Conversion rate theo kênh: DM → học thử → chốt | Agent-Report | Weekly | Dashboard
UC-I04 | Agent performance: accuracy, escalation rate | Agent-Report | Weekly | Dashboard
UC-I05 | CPL spike alert bất kỳ kênh nào | Agent-Report | Realtime | Telegram
UC-I06 | Content performance: post nào drive lead nhiều nhất | Agent-Report | Weekly | Dashboard
UC-I07 | KB accuracy report: câu hỏi nào agent trả lời sai | Agent-Report | Weekly | Notion
ID | Use Case | Agent | Tần suất | Kênh
UC-I08 | Monthly P&L; tổng hợp 5 kênh | Agent-Report | Ngày 1 | Dashboard
ID | Use Case | Agent | Tần suất | Kênh
UC-J01 | Agent health check 5 agent chatbot mỗi giờ | Agent-Report | Giờ | Telegram
UC-J02 | KB version control: log thay đổi, ai sửa gì, khi nào | Admin | On update | Git
UC-J03 | Sprint report hàng tuần tự động | Agent-Report | Thứ 6 | Notion
UC-J04 | Telegram lệnh nội bộ: /report /pause /kb-update | Agent-Code | Command | Telegram
UC-J05 | Claude API cost monitor theo agent | Agent-Report | Daily | Dashboard
UC-J06 | Onboarding sale mới: guide dùng Sale Assist | Agent-Code | New hire | Notion
ID | Use Case | Agent | Tần suất | Kênh
UC-K01 | YouTube SEO: title + description tối ưu auto | Agent-Content | Khi upload | YouTube
UC-K02 | Instagram Story poll về chủ đề tiếng Trung | Agent-Content | Tuần | Instagram
UC-K03 | TikTok Duet/Stitch với học viên tiêu biểu | Agent-Content | Tuần | TikTok
UC-K04 | Live session reminder qua tất cả kênh | Agent-Chat | Trước live 1h | Tất cả
UC-K05 | Cross-platform: học viên từ TikTok → chuyển Zalo | Agent-Lead | Realtime | TikTok→Zalo
UC-K06 | Google Business review request sau hoàn thành | Agent-Chat | Khi HT | Email, Zalo
UC-K07 | Scholarship announcement đa nền tảng | Agent-Chat | On event | Tất cả
UC-K08 | Student progress highlight → TikTok/Reels content | Agent-Content | Tháng | TikTok, IG
UC-K09 | Year-end tổng kết: học viên, doanh thu, KPI | Agent-Report | Cuối năm | Dashboard
UC-K10 | A/B test kịch bản chat: version A vs B conversion | Agent-Chat | Ongoing | Tất cả
Layer | Tool | Vai trò | Chi phí/t háng
Inbox Hub | n8n (self-host) | Tổng hợp webhook từ 5 nền tảng, routing, trigger agents | $0
Chat APIs | Zalo OA · Meta Graph API · TikTok Business · Instagram · YouTube Data API | Kết nối 5 kênh DM và comment | $0
Agent engine | ClawBot (Claude Code SDK) + CrewAI + Langflow | 8 agent orchestration, SKILL.md integration | $0
AI brain | Claude Sonnet 4.6 API | Reasoning, tư vấn, content, doc generation | $100–180
Knowledge Base | Markdown files + Git versioning | KB tiếng Trung, SKILL.md, kịch bản chat — IP công ty | $0
Database | PostgreSQL + Redis | Lead DB, conversation history, agent state, queue | $0
CRM | HubSpot Free / Getfly | Pipeline lead 5 kênh, deal tracking | $0–25
Document gen | Python ReportLab + Jinja2 | Báo giá PDF, brochure, slide — branded template | $0
Content schedule | Buffer + Later | Auto-post TikTok, FB, IG, YT theo lịch | $15
Pixel dashboard | Pixel Agents / Outworked | Nhìn thấy 8 agent làm việc real-time trong pixel office | $0
BI | Metabase (self-host) | Dashboard KPI tổng hợp 5 kênh | $0
Alert | Telegram Bot API | Alert, lệnh nội bộ, báo cáo sáng | $0
Infrastructure | VPS 8GB RAM (Vultr/DO) + Docker Compose | Chạy tất cả service, handle 8 agent song song | $48
TỔNG |  | Chưa kể nhân sự và ads budget | $163–268/t háng
Tuầ n | Giai đoạn | Người thật | AI Agent | Delivera ble
1 | KB + Infra | P1: VPS, Docker, n8n Hub, CrewAI. P3: Bắt đầu xây KB (export log chat cũ) | Setup infrastructure | Stack live, KB draft bắt đầu
2 | KB hoàn thiện | P3+P4: Hoàn thiện KB (giáo trình, giá, FAQ). P1: Kết nối Zalo + FB API | Agent-Code: webhook đầu tiên | Knowledge -base.md v1
3 | 50 kịch bản | P3: Viết 50 kịch bản chat. P4: Validate KB với 20 câu hỏi test | Agent-Chat: test KB accuracy | KB ≥85% accuracy, 50 scenarios
4 | Agent-Chat live Zalo+FB | P3: Monitor agent Zalo+FB tuần đầu. P4: QA daily | Agent-Chat: Zalo+FB 24/7 | Chatbot Zalo+FB live
5 | Sale Assist tool | P3: Setup Sale Assist workflow. P4: Test draft gợi ý | Agent-SaleAssist: inbox + draft | Sale Assist live tất cả kênh
6 | TikTok + IG + YT | P1: Kết nối TikTok, IG, YT API. P2: Cấu hình comment routing | Agent-Chat: mở rộng 3 kênh còn lại | 5 kênh đều có chatbot
7 | Lead & Marketing | P5: Setup Marketing dashboard 5 kênh. P3: Cấu hình drip sequence | Agent-Lead: scoring 5 kênh, drip | Lead pipeline tự động 5 kênh
8 | Content MKT | P2: Direction trend, approve content. P4: QA content quality | Agent-Research: trend. Agent-Content: 5 kênh | Content tự động 5 nền tảng
9 | Document auto | P3: Design template báo giá, brochure. P4: QA PDF output | Agent-Docs: PDF, brochure, slide | Tài liệu tự động trong 30 giây
10 | Ads automation | P2: Cấu hình ngưỡng Meta+TikTok Ads. P4: QA actions | Agent-Ads: Meta+TikTok optimize | Ads tự tối ưu 24/7
11 | KB refinement | P3+P4: Update KB từ data 10 tuần. Viết thêm kịch bản còn thiếu | Tất cả agent + KB v2 | KB v2, kịch bản bổ sung
12 | QA tổng thể | P4: Test 240 UC. P5: Viết SOP đầy đủ | 240 UC chạy song song | QA report, SOP hoàn chỉnh
13 | Go-live | All: War room 3 ngày. P5: So sánh KPI trước/sau | Full system: 240 UC live | Go-live, KPI, Phase 2 plan
B. | Hành động
1 | Trigger: DM vào bất kỳ kênh nào → n8n Inbox Hub nhận, tag kênh nguồn (Zalo/FB/TikTok/IG/YT)
2 | Lookup: Đã có lịch sử khách này chưa? → Merge nếu có, tạo mới nếu chưa
3 | Agent-Chat đọc KB tiếng Trung + SKILL kịch bản + tone voice theo kênh
4 | Claude API sinh trả lời phù hợp ngữ cảnh, kênh, và lịch sử cuộc trò chuyện
5 | Gửi reply đúng nền tảng qua API tương ứng
6 | Log conversation → Agent-Lead cập nhật điểm lead theo hành vi trong chat
7 | Nếu lead đạt ≥70 điểm: Push sang Agent-SaleAssist, alert Telegram sale <2 phút
8 | Nếu khách yêu cầu báo giá: Agent-Docs sinh PDF trong 30s → gửi qua kênh đó
9 | Nếu chốt: Trigger webhook → welcome sequence + onboarding kit + ZNS xác nhận
B. | Hành động
1 | Trigger: Comment mới trên bất kỳ post/video nào (TikTok, FB, IG, YT)
2 | Agent-Chat phân loại: (A) hỏi mua/giá/lịch, (B) tương tác thường, (C) spam
3 | Loại A (hỏi mua): Reply template ngắn gọn + mời DM trong comment
4 | Loại A: Sau 1 phút, tự động gửi DM (nếu platform cho phép) để tiếp tục tư vấn
5 | Loại B: Reply tương tác friendly theo SKILL content của kênh đó
6 | Loại C: Skip hoặc hide comment nếu vi phạm
7 | Lead từ comment được tag nguồn đặc biệt (ví dụ: tiktok_comment_post_123)
8 | Analytics: track conversion rate từ comment → DM → học thử → chốt
B. | Hành động
1 | Trigger: Thứ 2 sáng 7h hàng tuần
2 | Agent-Research scrape TikTok trending sounds/hashtags tiếng Trung VN
3 | Agent-Research check YouTube trending topics về học tiếng Trung
4 | Claude tổng hợp: top 5 trend tuần này + 20 content idea phân theo kênh
5 | Sinh content list Notion: TikTok (5 idea) + IG Reels (5) + FB (4) + YT (3) + Zalo (3)
6 | P2 review Notion list, approve/reject/chỉnh sửa (10–15 phút)
7 | Với idea approved: Agent-Content sinh caption/script đầy đủ cho từng kênh
8 | Push sang Buffer/Later schedule theo giờ vàng từng platform
B. | Hành động
1 | Trigger: Sale click 'Tạo báo giá' trong Sale Assist hoặc agent tự trigger
2 | Agent-Docs đọc KB: lấy thông tin mục tiêu khách, lộ trình đã tư vấn, bảng giá
B. | Hành động
3 | Claude điền vào template PDF: tên khách, lộ trình phù hợp, bảng giá chi tiết, ưu đãi
4 | Python ReportLab render PDF branded với logo, màu sắc trung tâm
5 | PDF upload lên storage, tạo link có thời hạn 7 ngày
6 | Gửi link PDF qua kênh đang tư vấn (Zalo: gửi file, FB: link, Email: đính kèm)
7 | Log: sale nào tạo, cho khách nào, kênh nào, lúc nào
Mã | Nhóm | Mô tả
FR-01 | Omnichannel Inbox | Tổng hợp DM + comment từ 5 nền tảng. Routing tự động. Priority queue theo urgency.
FR-02 | Knowledge Base | CRUD KB. Version control. Test accuracy. Deploy realtime cho agents.
FR-03 | AI Agent Management | Tạo, cấu hình, start/stop, monitor, test 8 agents. SKILL assignment.
FR-04 | Sale Assist | Unified inbox, AI draft gợi ý, context panel, quick reply, alert >5 phút chờ.
FR-05 | Lead & CRM | Scoring 5 kênh, phân loại, pipeline Kanban, assign, drip, Marketing dashboard.
FR-06 | Content Management | Brief → AI gen → approve → schedule. Content calendar 5 kênh.
FR-07 | Document Generation | Báo giá PDF, brochure, slide: template, điền tự động, gửi qua kênh.
FR-08 | Analytics | KPI 5 kênh, funnel conversion, KB accuracy, agent performance.
FR-09 | Ads Automation | Monitor Meta + TikTok Ads, auto pause/scale, creative rotation.
FR-10 | Admin & Security | RBAC, 2FA, audit log, backup, API key management.
NFR | Loại | Yêu cầu
NFR-01 | Hiệu năng | API response <200ms (p95). Agent chat reply <3s. PDF gen <30s.
NFR-02 | Độ tin cậy | Uptime ≥99.5%. Auto-restart khi crash. 5 kênh chat không được offline cùng lúc.
NFR-03 | Bảo mật | HTTPS/TLS 1.3. Mã hóa dữ liệu khách. Rate limiting. Không lưu conversation thô >30 ngày.
NFR-04 | Khả năng mở rộng | Handle ≥500 cuộc chat song song. Scale horizontal khi cần.
NFR-05 | KB Accuracy | Agent trả lời đúng ≥85% câu hỏi test set. Cảnh báo khi accuracy drop.
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-001 | Đăng nhập email/password + JWT | Tất cả User | Auth Service
SW-002 | Xác thực 2 yếu tố (2FA) OTP | Tất cả User | Auth Service
SW-003 | Quản lý session & auto-logout | Hệ thống | Session Manager
SW-004 | Phân quyền RBAC theo role | Admin | RBAC Service
SW-005 | Tạo & quản lý API key | Admin, QA | API Key Service
SW-006 | Audit log toàn bộ hành động | Admin | Audit Service
SW-007 | Reset mật khẩu qua email | Tất cả | Email Service
SW-008 | Khóa tài khoản sau 5 lần sai | Hệ thống | Security
SW-009 | Tạo tài khoản nhân viên mới | Admin | User Service
SW-010 | Xem lịch sử đăng nhập | Admin | Audit Viewer
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-011 | Xem unified inbox 5 kênh theo priority | Sale, Agent | Inbox UI
SW-012 | Filter inbox theo kênh/status/agent | Sale, PM | Inbox Filter
SW-013 | Xem chi tiết conversation với context panel | Sale | Conversation View
SW-014 | Assign conversation cho sale cụ thể | Admin, Agent | Assignment
SW-015 | Mark conversation: resolved/pending/escalated | Sale | Status Manager
SW-016 | Xem lịch sử conversation theo khách hàng | Sale, PM | History View
SW-017 | Merge conversation trùng cùng 1 khách | Admin | Dedup Service
SW-018 | Tìm kiếm trong conversation (full-text) | Tất cả | Search
SW-019 | Cấu hình webhook từng nền tảng | Admin | Webhook Manager
SW-020 | Platform connection health check | Admin | Health Monitor
SW-021 | Xem conversation analytics (volume/giờ) | PM | Analytics
SW-022 | Export conversation log | Admin | Export Service
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-023 | Tạo/chỉnh sửa KB module trong editor | QA, Admin | KB Editor
SW-024 | Xem version history KB | QA, Admin | Git Integration
SW-025 | Deploy KB mới lên agents (zero-downtime) | QA, Admin | KB Deployer
SW-026 | Rollback KB về version cũ | Admin | Rollback Service
SW-027 | Test KB: 20 câu hỏi chuẩn + accuracy score | QA | KB Tester
SW-028 | Xem câu hỏi nào agent đang trả lời sai | QA | Gap Analyzer
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-029 | Import FAQ từ log chat export | QA, P3 | FAQ Importer
SW-030 | So sánh diff 2 versions KB | QA | Diff Viewer
SW-031 | Cấu hình KB module nào dùng cho agent nào | Admin | KB Assignment
SW-032 | Alert khi KB accuracy drop dưới ngưỡng | Hệ thống | Quality Monitor
SW-033 | Archive KB cũ không còn dùng | Admin | KB Archive
SW-034 | Export KB ra file markdown | Admin, QA | Export Service
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-035 | Tạo agent mới với model và config | Admin, QA | Agent Registry
SW-036 | Xem danh sách 8 agents và trạng thái | Tất cả | Agent Dashboard
SW-037 | Gán SKILL.md và KB modules cho agent | Admin, QA | Skill Assignment
SW-038 | Start/Pause/Stop agent | Admin, QA | Agent Controller
SW-039 | Xem logs real-time từng agent | Admin, QA | Log Viewer
SW-040 | Test agent với tình huống mẫu | QA | Agent Tester
SW-041 | Xem agent performance: accuracy, latency, cost | PM, Admin | Metrics Dashboard
SW-042 | Restart agent tự động khi lỗi | Hệ thống | Auto-Recovery
SW-043 | Pixel art office: nhìn agent làm việc real-time | Tất cả | Pixel Agents UI
SW-044 | Clone agent với config tương tự | Admin | Agent Registry
SW-045 | Xem conversation history của agent | Admin, QA | Conversation Log
SW-046 | Cấu hình escalation rules cho agent | Admin, QA | Escalation Config
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-047 | Unified inbox với AI priority sorting | Sale | Sale Assist UI
SW-048 | Hiển thị AI draft gợi ý phản hồi | Sale | Draft Engine
SW-049 | Edit và gửi draft trong 1 click | Sale | Send Service
SW-050 | Context sidebar: lịch sử + điểm + gợi ý | Sale | Context Panel
SW-051 | Quick reply template library | Sale | Template Manager
SW-052 | Alert khách chờ >5 phút | Hệ thống | Alert Service
SW-053 | Tóm tắt conversation dài trước khi đọc | Sale | Summary Service
SW-054 | Gợi ý upsell khi lead sắp chốt | Agent | Upsell Engine
SW-055 | Daily summary cho sale cuối ngày | Agent-Report | Report Service
SW-056 | Cấu hình thông báo cho sale | Sale | Notification Config
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-057 | Xem lead list với filter 5 kênh | Sale, PM | Lead List
SW-058 | Chi tiết lead: timeline tất cả touchpoints | Sale, PM | Lead Detail
SW-059 | Chuyển trạng thái lead trong pipeline | Sale, Agent | Pipeline
SW-060 | Assign lead cho sale | Admin, Agent | Assignment
SW-061 | Ghi chú call log hoạt động tư vấn | Sale | Activity Logger
SW-062 | Xem pipeline Kanban 5 kênh | Sale, PM | Kanban Board
SW-063 | Marketing dashboard: data 5 kênh real-time | PM, Mktg | Dashboard
SW-064 | Export lead CSV/Excel | Admin, PM | Export Service
SW-065 | Import lead từ file | Admin | Import Service
SW-066 | Cấu hình scoring rules theo kênh | Admin, QA | Score Config
SW-067 | Funnel report: conversion từng kênh | PM | Funnel Report
SW-068 | Merge lead trùng cross-platform | Admin | Dedup Service
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-069 | Tạo content brief cho Agent-Content | Mktg | Brief Editor
SW-070 | Xem và approve content queue 5 kênh | Mktg | Content Queue
SW-071 | Content calendar tổng hợp 5 kênh | Mktg, PM | Calendar View
SW-072 | Quản lý document template (báo giá, brochure) | Admin, P3 | Template Manager
SW-073 | Tạo báo giá PDF từ conversation context | Sale, Agent | Doc Generator
SW-074 | Xem tất cả document đã tạo theo khách | Sale | Doc Library
SW-075 | Gửi document qua kênh đang chat | Sale | Send Service
SW-076 | Cấu hình brand: logo, màu, font cho PDF | Admin | Brand Config
SW-077 | Content performance analytics | PM, Mktg | Analytics
SW-078 | Platform schedule configuration | Admin | Schedule Config
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-079 | KPI dashboard tổng quan 5 kênh | PM, Admin | Main Dashboard
SW-080 | Báo cáo lead theo kênh, ngày, tuần, tháng | PM, Mktg | Channel Report
SW-081 | Conversion funnel 5 kênh | PM | Funnel Report
SW-082 | KB accuracy report: câu hỏi nào đang sai | QA | KB Analytics
SW-083 | Agent performance report | Admin, PM | Agent Metrics
SW-084 | Sale assist efficiency: draft used rate | PM | Sale Analytics
SW-085 | Content performance: post nào drive lead | Mktg | Content Analytics
SW-086 | Custom report builder | PM, Admin | Report Builder
SW-087 | Export báo cáo PDF/Excel | Tất cả | Export Service
SW-088 | Schedule báo cáo tự động | Admin, PM | Report Scheduler
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-089 | Cấu hình kết nối Zalo OA API | Admin | Integration Manager
SW-090 | Cấu hình kết nối Facebook Graph API | Admin | Integration Manager
SW-091 | Cấu hình kết nối TikTok Business API | Admin | Integration Manager
SW-092 | Cấu hình kết nối Instagram API | Admin | Integration Manager
SW-093 | Cấu hình kết nối YouTube Data API | Admin | Integration Manager
SW-094 | Cấu hình kết nối Meta Ads API | Admin | Integration Manager
SW-095 | Integration health dashboard 5 kênh | Admin | Health Dashboard
SW-096 | Monitor API rate limit 5 kênh | Admin | Rate Monitor
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-097 | System config: env, feature flags | Admin | Config Manager
SW-098 | System health: CPU, RAM, disk | Admin | System Monitor
SW-099 | Backup & restore database | Admin | Backup Manager
SW-100 | Centralized log viewer | Admin | Log Manager
SW-101 | Notification channels config | Admin | Notification Config
SW-102 | Security policies: password, IP whitelist | Admin | Security Config
SW-103 | Claude API cost & quota management | Admin | Cost Manager
SW-104 | Environment management: dev/staging/prod | Admin | Env Manager
SW-105 | Cache management (Redis) | Admin | Cache Manager
SW-106 | Audit trail: who did what, when | Admin | Audit Viewer
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-107 | Render báo giá PDF từ template + context | Agent-Docs | PDF Engine
SW-108 | Render brochure khóa học theo mục tiêu | Agent-Docs | PDF Engine
SW-109 | Render slide demo 5 trang (PPTX/PDF) | Agent-Docs | Slide Engine
SW-110 | Render onboarding kit học viên mới | Agent-Docs | PDF Engine
SW-111 | Quản lý template library (PDF, PPTX) | Admin | Template Manager
SW-112 | Upload logo/ảnh GV vào template | Admin | Asset Manager
SW-113 | Preview document trước khi gửi | Sale | Preview Service
SW-114 | Track document: ai mở, khi nào (read receipt) | Agent-Lead | Doc Tracker
Mã UC | Tên Use Case |  | Actor | Hệ thống con
SW-115 | Test set manager: quản lý câu hỏi test | QA |  | Test Manager
Mã UC | Tên Use Case | Actor | Hệ thống con
SW-116 | Chạy accuracy test tự động hàng ngày | Hệ thống | Auto Tester
SW-117 | So sánh accuracy trước/sau update KB | QA | Accuracy Tracker
SW-118 | Flag câu trả lời cần review từ sale | Sale | Flag Service
SW-119 | A/B test 2 phiên bản KB khác nhau | QA | A/B Tester
SW-120 | Alert khi accuracy drop >5% so hôm qua | Hệ thống | Quality Alert
Bảng | Mô tả | Cột chính
contacts | Khách hàng cross-platform | id, name, phone, email, zalo_id, fb_id, tiktok_id, ig_id, yt_id, score, status
conversations | Lịch sử chat từng kênh | id, contact_id, platform, thread_id, status, last_msg_at, agent_id, sale_id
messages | Tin nhắn từng cuộc chat | id, conversation_id, direction, content, msg_type, created_at, read_at
kb_modules | Knowledge Base modules | id, name, file_path, version, accuracy_score, deployed_at, status
chat_scenarios | 50 kịch bản hội thoại | id, code, trigger, platform, response_template, success_rate
agents | 8 AI agents config | id, name, type, model, status, skill_files[], kb_modules[], config_json
documents | Tài liệu đã tạo | id, type, contact_id, template_id, file_url, sent_via, opened_at, created_by
content_queue | Content chờ approve | id, platform, brief, content, status, approved_by, scheduled_at
kpi_daily | KPI theo ngày từng kênh | id, date, platform, leads, dms, replies, conversions, avg_response_time
Metho d | Endpoint | Mô tả
POST | /api/webhook/{platform} | Nhận DM/comment từ Zalo, FB, TikTok, IG, YT
POST | /api/agent/chat | Gọi Agent-Chat với context, trả lời theo KB + SKILL
POST | /api/docs/generate | Sinh báo giá PDF / brochure từ contact_id + doc_type
GET | /api/inbox | Unified inbox: tất cả conversation ưu tiên theo urgency
GET | /api/kb/accuracy | Báo cáo accuracy KB hiện tại so với test set
PUT | /api/kb/{module_id} | Update nội dung KB module, trigger reload agents
GET | /api/analytics/omnichannel | KPI tổng hợp 5 kênh với date range
POST | /api/leads/{id}/assign | Assign lead cho sale + notify qua Telegram
GET | /api/sale-assist/draft | Lấy AI draft gợi ý cho conversation hiện tại
GET | /api/content/trend-list | Content idea list tuần này từ Agent-Research
Rủi ro | KN | TD | Giảm thiểu
KB sai thông tin → agent tư vấn sai giá/lịch | Cao | Rất cao | Test set 20 câu hỏi chuẩn trước deploy. Sale review output 100% trong 2 tuần đầu. Alert khi accuracy drop.
API 1 kênh bị block → miss tin nhắn | TB | Cao | Monitor health 5 kênh mỗi 15 phút. Alert ngay khi 1 kênh down. Fallback: thông báo sale check manual.
Agent hiểu sai kịch bản → reply không phù hợp kênh | TB | Cao | Test từng kịch bản trên từng nền tảng. QA monitor 1 tuần sau deploy. Khách luôn có thể yêu cầu nói chuyện người thật.
Chi phí Claude API tăng đột biến | TB | TB | Hard cap $200/tháng trong Anthropic console. Alert 80%. Cache response cho câu hỏi giống nhau.
Data khách bị rò rỉ (GDPR/privacy) | Thấp | Rất cao | Không lưu nội dung chat thô >30 ngày. Mã hóa số điện thoại/email. RBAC: sale chỉ thấy lead mình phụ trách.
Sale không dùng Sale Assist (adoption thấp) | TB | Cao | Onboarding 30 phút trực tiếp. Đo: tỷ lệ dùng draft vs tự gõ. Tuần 2 demo kết quả cụ thể.
KB không được cập nhật khi giá thay đổi | TB | Cao | Checklist cập nhật KB là bắt buộc trước khi thay đổi giá/chương trình có hiệu lực.
Hạng mục | Chi tiết | $/tháng
VPS 8GB RAM | Vultr/DigitalOcean | $48
Claude Sonnet API | 8 agent × volume 5 kênh | $100–180
Buffer + Later | Schedule content 5 kênh | $15
HubSpot/Getfly CRM | Free tier hoặc starter | $0–25
Domain + SSL | Namecheap + Let's Encrypt | ~$2
TỔNG | Không kể nhân sự + ads | $165–270
Chỉ số | Hiện tại | Mục tiêu | Cơ chế
Thời gian phản hồi DM | 15–60 phút | <2 phút | Agent-Chat 24/7 tất cả kênh
Tỷ lệ miss tin nhắn | ~20% | <1% | Unified inbox không bỏ sót
Khách/sale/ngày | 20–30 | 60–90 | Sale Assist × 3 hiệu suất
Conversion DM → học thử | Baseline | +25–40% | KB chính xác, 24/7, no miss
Thời gian tạo báo giá | 10–20 phút | <30 giây | Agent-Docs tự động
Content output/tuần | 10–15 bài | 40+ bài 5 kênh | Agent-Content + Agent-Research
