import { type CSSProperties, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { getPublicFaq, type PublicFaqItem, type TenantBranding } from "@/shared/api/publicWidget";

const DEFAULT_TENANT_SLUG = import.meta.env.VITE_WIDGET_TENANT_SLUG ?? "default";
const DEFAULT_BRANDING: TenantBranding = {
  brandName: "Học Bá AI",
  logoUrl: null,
  primaryColor: "#d32f2f",
  accentColor: "#f59e0b",
  supportName: "Hỗ trợ Học Bá",
  widgetGreeting: "Chào bạn, Học Bá AI có thể hỗ trợ tư vấn lộ trình học và lịch kiểm tra đầu vào.",
};

const DEFAULT_FAQ_ITEMS: readonly PublicFaqItem[] = [
  {
    id: "default-tuition",
    moduleCode: "hoc-phi",
    moduleName: "Học phí & Khuyến mãi",
    question: "Học phí & Khuyến mãi",
    answer: [
      "Chính sách hoàn học phí:",
      "- Học viên được hoàn 100% học phí nếu rút trước ngày khai giảng 7 ngày.",
      "- Hoàn 50% học phí nếu rút trong tuần đầu tiên của khóa học.",
      "- Sau tuần đầu tiên, học phí sẽ không được hoàn lại dưới bất kỳ hình thức nào.",
      "",
      "Mọi thắc mắc chi tiết hơn, vui lòng liên hệ trực tiếp với bộ phận tư vấn của Học Bá AI để được giải đáp cụ thể theo từng trường hợp.",
    ].join("\n"),
  },
  {
    id: "default-hsk",
    moduleCode: "lo-trinh-hsk",
    moduleName: "Lộ trình học HSK",
    question: "Lộ trình học HSK",
    answer:
      "Học viên được kiểm tra đầu vào để xác định cấp độ phù hợp. Đội học thuật sẽ tư vấn lộ trình HSK theo mục tiêu du học, công việc hoặc giao tiếp thực tế.",
  },
  {
    id: "default-policy",
    moduleCode: "chinh-sach-hoc-vien",
    moduleName: "Chính sách học viên",
    question: "Chính sách học viên",
    answer:
      "Học viên có thể bảo lưu khóa học theo chính sách từng chương trình. Các thay đổi lịch học cần được thông báo với bộ phận tư vấn để được hỗ trợ kịp thời.",
  },
];

function splitAnswer(answer: string) {
  return answer
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

function readText(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function normalizeFaqItems(value: unknown): readonly PublicFaqItem[] {
  if (!Array.isArray(value)) return [];

  return value.flatMap((item, index) => {
    if (!item || typeof item !== "object") return [];

    const candidate = item as Record<string, unknown>;
    const question = readText(candidate.question);
    const answer = readText(candidate.answer);

    if (!question || !answer) return [];

    return [
      {
        id: readText(candidate.id) || `public-faq-${index}`,
        moduleCode: readText(candidate.moduleCode) || "public-faq",
        moduleName: readText(candidate.moduleName) || "FAQ",
        question,
        answer,
      },
    ];
  });
}

function AnswerContent({ answer }: { readonly answer: string }) {
  const lines = splitAnswer(answer);
  const titleLine = lines.find((line) => line.endsWith(":"));
  const bulletLines = lines.filter((line) => line.startsWith("- ") || line.startsWith("• "));
  const paragraphLines = lines.filter((line) => line !== titleLine && !bulletLines.includes(line));

  return (
    <div className="space-y-3 text-body-md leading-6 text-on-surface-variant">
      {titleLine ? <p className="font-semibold text-on-surface">{titleLine}</p> : null}
      {bulletLines.length > 0 ? (
        <ul className="list-disc space-y-2 pl-5">
          {bulletLines.map((line) => (
            <li key={line}>{line.replace(/^[-•]\s*/, "")}</li>
          ))}
        </ul>
      ) : null}
      {paragraphLines.map((line) => (
        <p key={line}>{line}</p>
      ))}
    </div>
  );
}

export default function SupportFaqPage() {
  const params = useParams();
  const tenantSlug = params.tenantSlug ?? DEFAULT_TENANT_SLUG;
  const [search, setSearch] = useState("");
  const [openId, setOpenId] = useState<string | null>(DEFAULT_FAQ_ITEMS[0]?.id ?? null);

  const faqQuery = useQuery({
    queryKey: ["public-faq", tenantSlug],
    queryFn: () => getPublicFaq(tenantSlug),
    retry: false,
  });

  const apiItems = normalizeFaqItems(faqQuery.data?.items);
  const items = apiItems.length > 0 ? apiItems : DEFAULT_FAQ_ITEMS;
  const branding = faqQuery.data?.branding ?? DEFAULT_BRANDING;
  const tenantName = typeof faqQuery.data?.tenantName === "string" && faqQuery.data.tenantName.trim() ? faqQuery.data.tenantName : branding.brandName;
  const brandStyle = { color: branding.primaryColor } satisfies CSSProperties;
  const brandBorderStyle = { borderColor: branding.primaryColor, color: branding.primaryColor } satisfies CSSProperties;
  const searchBorderStyle = { borderColor: branding.accentColor } satisfies CSSProperties;
  const chatHref = tenantSlug === DEFAULT_TENANT_SLUG ? "/chat-widget" : `/chat-widget/${tenantSlug}`;
  const normalizedSearch = search.trim().toLocaleLowerCase("vi-VN");

  const filteredItems = useMemo(() => {
    if (!normalizedSearch) return items;
    return items.filter((item) => {
      const searchable = `${item.question} ${item.answer} ${item.moduleName}`.toLocaleLowerCase("vi-VN");
      return searchable.includes(normalizedSearch);
    });
  }, [items, normalizedSearch]);

  const visibleOpenId = useMemo(() => {
    if (filteredItems.length === 0 || openId === null) return null;
    return filteredItems.some((item) => item.id === openId) ? openId : filteredItems[0].id;
  }, [filteredItems, openId]);

  return (
    <div className="min-h-screen bg-white text-on-surface antialiased">
      <header className="fixed top-0 z-40 w-full border-b border-[#f1d9d6] bg-white/80 shadow-sm backdrop-blur-md">
        <div className="mx-auto flex h-[76px] max-w-[1160px] items-center justify-between px-6">
          <Link
            className="inline-flex min-w-0 items-center gap-3 text-[20px] font-bold leading-7 tracking-[0] text-primary"
            style={brandStyle}
            to={tenantSlug === DEFAULT_TENANT_SLUG ? "/support" : `/support/${tenantSlug}`}
          >
            {branding.logoUrl ? <img alt="" className="size-9 rounded object-cover" src={branding.logoUrl} /> : null}
            <span className="truncate">{tenantName}</span>
          </Link>
          <nav className="hidden items-center gap-8 text-[14px] font-medium leading-5 text-on-surface-variant md:flex">
            <a className="transition-colors hover:text-primary" href="#courses">
              Khóa học
            </a>
            <a className="transition-colors hover:text-primary" href="#events">
              Sự kiện
            </a>
            <a className="transition-colors hover:text-primary" href="#admissions">
              Tuyển sinh
            </a>
          </nav>
          <Link
            className="rounded-lg border border-primary px-4 py-2 text-[14px] font-semibold leading-5 text-primary transition-colors hover:bg-primary hover:text-on-primary"
            style={brandBorderStyle}
            to={chatHref}
          >
            Liên hệ
          </Link>
        </div>
      </header>

      <main className="mx-auto min-h-screen max-w-[800px] px-6 pb-20 pt-[120px]">
        <h1 className="mb-8 text-[32px] font-bold leading-10 tracking-[0] text-primary md:text-[40px] md:leading-[48px]" style={brandStyle}>
          Hỏi đáp & Hỗ trợ - {tenantName}
        </h1>

        <label
          className="mb-8 flex h-14 items-center gap-3 rounded-full border border-[#e9c8c4] bg-white px-5 shadow-sm transition-shadow focus-within:ring-2 focus-within:ring-primary/10"
          style={searchBorderStyle}
        >
          <span aria-hidden="true" className="material-symbols-outlined text-[24px] text-on-surface-variant">search</span>
          <input
            aria-label="Tìm kiếm câu hỏi hỗ trợ"
            className="h-full min-w-0 flex-1 bg-transparent text-[16px] leading-6 text-on-surface outline-none placeholder:text-on-surface-variant/60"
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Bạn cần tìm hiểu về vấn đề gì?"
            type="search"
            value={search}
          />
        </label>

        <section aria-busy={faqQuery.isFetching} className="divide-y divide-[#f0d8d5] border-t border-b border-[#f0d8d5]">
          {filteredItems.length > 0 ? (
            filteredItems.map((item) => {
              const isOpen = item.id === visibleOpenId;
              return (
                <article className="bg-white" key={item.id}>
                  <button
                    aria-expanded={isOpen}
                    className="flex w-full items-center justify-between gap-4 py-6 text-left text-[18px] font-semibold leading-7 text-on-surface transition-colors hover:text-primary"
                    onClick={() => setOpenId(isOpen ? null : item.id)}
                    type="button"
                  >
                    <span>{item.question}</span>
                    <span
                      aria-hidden="true"
                      className={`material-symbols-outlined text-[24px] text-primary transition-transform ${isOpen ? "rotate-180" : ""}`}
                      style={brandStyle}
                    >
                      keyboard_arrow_down
                    </span>
                  </button>
                  {isOpen ? (
                    <div className="max-w-[720px] pb-7">
                      <AnswerContent answer={item.answer} />
                    </div>
                  ) : null}
                </article>
              );
            })
          ) : (
            <div className="py-10 text-body-md text-on-surface-variant">Không tìm thấy câu hỏi phù hợp.</div>
          )}
        </section>
      </main>
    </div>
  );
}
