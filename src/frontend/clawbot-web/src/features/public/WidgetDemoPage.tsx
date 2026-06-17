import { type CSSProperties, useMemo, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useParams } from "react-router-dom";
import {
  captureWidgetLead,
  getWidgetBootstrap,
  sendWidgetMessage,
  type WidgetBootstrap,
} from "@/shared/api/publicWidget";

type ChatMessage = {
  readonly id: string;
  readonly side: "visitor" | "bot";
  readonly text: string;
  readonly time: string;
};

const DEFAULT_BOOTSTRAP: WidgetBootstrap = {
  tenantSlug: "default",
  tenantName: "Học Bá Education",
  supportName: "Học Bá Support",
  online: true,
  greeting: "Chào bạn, Học Bá có thể hỗ trợ tư vấn lộ trình học và lịch kiểm tra đầu vào.",
  suggestedQuestions: ["Tư vấn khóa HSK phù hợp", "Đặt lịch kiểm tra đầu vào", "Nhận học phí và ưu đãi mới nhất"],
  branding: {
    brandName: "Học Bá Education",
    logoUrl: null,
    primaryColor: "#d32f2f",
    accentColor: "#f59e0b",
    supportName: "Học Bá Support",
    widgetGreeting: "Chào bạn, Học Bá có thể hỗ trợ tư vấn lộ trình học và lịch kiểm tra đầu vào.",
  },
};

const featureCards = [
  {
    icon: "book",
    title: "Advanced Curriculum",
    body: "Rigorous academic programs structured to build profound foundational knowledge.",
    tone: "bg-primary-fixed text-primary-container",
  },
  {
    icon: "school",
    title: "Expert Tutors",
    body: "Learn directly from industry professionals and experienced academic mentors.",
    tone: "bg-secondary-fixed text-secondary",
  },
  {
    icon: "insights",
    title: "Data-Driven Progress",
    body: "Track your learning journey with advanced analytics and personalized feedback.",
    tone: "bg-tertiary-fixed text-tertiary",
  },
] as const;

function nowLabel(): string {
  return new Intl.DateTimeFormat("vi-VN", { hour: "2-digit", minute: "2-digit" }).format(new Date());
}

function ChatBubble({ message }: { readonly message: ChatMessage }) {
  const visitor = message.side === "visitor";
  return (
    <div className={visitor ? "self-end max-w-[85%]" : "self-start max-w-[90%]"}>
      <div
        className={[
          "rounded-xl p-3 text-body-md shadow-sm",
          visitor
            ? "rounded-tr-sm bg-primary-container text-on-primary"
            : "rounded-tl-sm border border-primary-fixed-dim/30 bg-primary-fixed text-primary-container",
        ].join(" ")}
      >
        {message.text}
      </div>
      <div className={`mt-1 text-[10px] text-on-surface-variant/60 ${visitor ? "text-right" : "text-left"}`}>{message.time}</div>
    </div>
  );
}

function TypingIndicator() {
  return (
    <div className="mt-auto flex items-center gap-2 pt-2 text-on-surface-variant">
      <div className="flex size-6 items-center justify-center rounded-full bg-primary-fixed text-primary-container">
        <span className="material-symbols-outlined text-[14px]">smart_toy</span>
      </div>
      <div className="flex items-center gap-2 rounded-lg rounded-tl-sm border border-outline-variant/30 bg-surface-container p-2">
        <span className="text-label-sm italic text-on-surface-variant">AI đang soạn phản hồi</span>
        <div className="flex items-center gap-1">
          {[0, 1, 2].map((item) => (
            <span
              className="size-1.5 animate-pulse rounded-full bg-primary-container"
              key={item}
              style={{ animationDelay: `${item * 130}ms` }}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

function LeadForm({
  disabled,
  error,
  onSubmit,
}: {
  readonly disabled: boolean;
  readonly error: string | null;
  readonly onSubmit: (payload: { phone: string; displayName: string; email: string }) => void;
}) {
  const [phone, setPhone] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");

  return (
    <form
      className="mt-2 flex flex-col gap-3 rounded-lg border border-outline-variant bg-surface-container-lowest p-3 shadow-sm"
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit({ phone, displayName, email });
      }}
    >
      <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
        Số điện thoại *
        <input
          className="w-full rounded-md border border-outline-variant bg-surface px-3 py-2 text-body-md text-on-surface outline-none transition-shadow focus:border-primary-container focus:ring-1 focus:ring-primary-container"
          disabled={disabled}
          inputMode="tel"
          onChange={(event) => setPhone(event.target.value)}
          placeholder="Nhập số điện thoại của bạn..."
          type="tel"
          value={phone}
        />
      </label>
      <div className="grid gap-2 sm:grid-cols-2">
        <input
          className="rounded-md border border-outline-variant bg-surface px-3 py-2 text-body-md outline-none focus:border-primary-container focus:ring-1 focus:ring-primary-container"
          disabled={disabled}
          onChange={(event) => setDisplayName(event.target.value)}
          placeholder="Tên của bạn"
          type="text"
          value={displayName}
        />
        <input
          className="rounded-md border border-outline-variant bg-surface px-3 py-2 text-body-md outline-none focus:border-primary-container focus:ring-1 focus:ring-primary-container"
          disabled={disabled}
          onChange={(event) => setEmail(event.target.value)}
          placeholder="Email nếu có"
          type="email"
          value={email}
        />
      </div>
      {error ? <p className="text-body-md text-error">{error}</p> : null}
      <button
        className="flex w-full items-center justify-center gap-2 rounded-md bg-primary px-4 py-2.5 text-label-md font-bold text-on-primary shadow-sm transition-colors hover:bg-primary-hover disabled:cursor-not-allowed disabled:opacity-60"
        disabled={disabled}
        type="submit"
      >
        <span className="material-symbols-outlined text-[18px]">phone_in_talk</span>
        {disabled ? "Đang gửi..." : "Nhận tư vấn"}
      </button>
    </form>
  );
}

function ChatWidget({ bootstrap, tenantSlug }: { readonly bootstrap: WidgetBootstrap; readonly tenantSlug: string }) {
  const branding = bootstrap.branding ?? DEFAULT_BOOTSTRAP.branding;
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [composer, setComposer] = useState("");
  const [formError, setFormError] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);

  const captureMutation = useMutation({
    mutationFn: (payload: { phone: string; displayName: string; email: string }) =>
      captureWidgetLead(tenantSlug, {
        phone: payload.phone,
        displayName: payload.displayName || null,
        email: payload.email || null,
        message: "Khách gửi số điện thoại từ Web Chat Widget.",
      }),
    onSuccess: (response) => {
      setConversationId(response.conversationId);
      setFormError(null);
      setMessages((current) => [
        ...current,
        { id: `visitor-${response.contactId}`, side: "visitor", text: "Mình đã gửi số điện thoại, nhờ Học Bá tư vấn.", time: nowLabel() },
        { id: `bot-${response.leadId}`, side: "bot", text: response.reply, time: nowLabel() },
      ]);
    },
    onError: () => setFormError("Không gửi được thông tin. Vui lòng kiểm tra số điện thoại và thử lại."),
  });

  const messageMutation = useMutation({
    mutationFn: (content: string) => sendWidgetMessage(tenantSlug, conversationId ?? "", content),
    onSuccess: (response) => {
      setMessages((current) => [...current, { id: response.messageId, side: "bot", text: response.reply, time: nowLabel() }]);
    },
  });

  function submitLead(payload: { phone: string; displayName: string; email: string }) {
    if (!payload.phone.trim()) {
      setFormError("Vui lòng nhập số điện thoại.");
      return;
    }
    captureMutation.mutate(payload);
  }

  function sendMessage() {
    const content = composer.trim();
    if (!content) return;
    setMessages((current) => [...current, { id: `local-${Date.now()}`, side: "visitor", text: content, time: nowLabel() }]);
    setComposer("");
    if (conversationId) {
      messageMutation.mutate(content);
    } else {
      setMessages((current) => [
        ...current,
        { id: `bot-need-phone-${Date.now()}`, side: "bot", text: "Bạn để lại số điện thoại trước để Học Bá tạo hồ sơ tư vấn nhé.", time: nowLabel() },
      ]);
    }
  }

  return (
    <aside className="fixed bottom-4 right-4 z-50 flex h-[500px] w-[calc(100vw-2rem)] max-w-[400px] flex-col overflow-hidden rounded-xl border border-outline-variant/20 bg-surface-container-lowest shadow-[0_10px_25px_rgba(0,0,0,0.15)] md:bottom-8 md:right-8">
      <header className="flex items-center justify-between p-3 text-white shadow-sm" style={{ backgroundColor: branding.primaryColor }}>
        <div className="flex items-center gap-2">
          {branding.logoUrl ? (
            <img alt="" className="size-8 rounded-full bg-white object-contain p-1" src={branding.logoUrl} />
          ) : (
            <div className="flex size-8 items-center justify-center rounded-full bg-white text-lg font-bold" style={{ color: branding.primaryColor }}>
              <span className="material-symbols-outlined text-[20px]">support_agent</span>
            </div>
          )}
          <div>
            <h2 className="text-[16px] font-bold leading-tight">{bootstrap.supportName}</h2>
            <p className="text-label-sm font-normal text-white/80">{bootstrap.online ? "Trực tuyến" : "Đang nhận tin nhắn"}</p>
          </div>
        </div>
        <button className="flex size-8 items-center justify-center rounded-full text-white transition-colors hover:bg-white/10" type="button">
          <span className="material-symbols-outlined">close</span>
        </button>
      </header>

      <div className="flex flex-1 flex-col gap-4 overflow-y-auto bg-surface-bright p-4">
        <ChatBubble message={{ id: "seed-visitor", side: "visitor", text: "Mình muốn tìm khóa tiếng Trung phù hợp.", time: "10:42" }} />
        <ChatBubble message={{ id: "seed-bot", side: "bot", text: bootstrap.greeting, time: "10:43" }} />
        {messages.map((message) => <ChatBubble key={message.id} message={message} />)}
        {!conversationId ? (
          <div className="self-start max-w-[90%]">
            <div className="rounded-xl rounded-tl-sm border border-primary-fixed-dim/30 bg-primary-fixed p-3 text-body-md text-primary-container shadow-sm">
              Bạn để lại số điện thoại để đội tư vấn Học Bá gọi lại trong hôm nay nhé.
              <LeadForm disabled={captureMutation.isPending} error={formError} onSubmit={submitLead} />
            </div>
          </div>
        ) : null}
        <TypingIndicator />
      </div>

      <footer className="flex items-end gap-2 border-t border-outline-variant bg-surface-container-lowest p-3 shadow-[0_-2px_10px_rgba(0,0,0,0.02)]">
        <textarea
          className="max-h-[100px] min-h-[44px] flex-1 resize-none rounded-lg border border-outline-variant bg-surface px-3 py-2.5 text-body-md text-on-surface outline-none transition-shadow placeholder:text-on-surface-variant/50 focus:border-primary-container focus:ring-1 focus:ring-primary-container"
          onChange={(event) => setComposer(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              sendMessage();
            }
          }}
          placeholder="Ask Học Bá anything..."
          rows={1}
          value={composer}
        />
        <button
          className="flex size-11 shrink-0 items-center justify-center rounded-full text-white transition-colors disabled:cursor-not-allowed disabled:opacity-60"
          disabled={messageMutation.isPending}
          onClick={sendMessage}
          style={{ backgroundColor: branding.primaryColor }}
          type="button"
        >
          <span className="material-symbols-outlined">send</span>
        </button>
      </footer>
    </aside>
  );
}

export default function WidgetDemoPage() {
  const params = useParams();
  const tenantSlug = params.tenantSlug ?? import.meta.env.VITE_WIDGET_TENANT_SLUG ?? "default";
  const bootstrapQuery = useQuery({
    queryKey: ["public-widget", tenantSlug],
    queryFn: () => getWidgetBootstrap(tenantSlug),
    retry: false,
  });
  const bootstrap = bootstrapQuery.data ?? DEFAULT_BOOTSTRAP;
  const branding = bootstrap.branding ?? DEFAULT_BOOTSTRAP.branding;
  const brandStyle = { color: branding.primaryColor } satisfies CSSProperties;

  const navItems = useMemo(() => ["Courses", "Events", "Admissions"], []);

  return (
    <div className="min-h-screen bg-background text-on-background antialiased">
      <header className="fixed top-0 z-40 w-full border-b border-outline-variant/30 bg-white/80 shadow-sm backdrop-blur-md">
        <div className="mx-auto flex max-w-[1160px] items-center justify-between px-6 py-4">
          <div className="flex items-center gap-3 text-display-sm font-bold tracking-tight" style={brandStyle}>
            {branding.logoUrl ? <img alt="" className="size-9 rounded object-contain" src={branding.logoUrl} /> : null}
            {bootstrap.tenantName}
          </div>
          <nav className="hidden gap-6 text-label-md md:flex">
            {navItems.map((item, index) => (
              <a
                className={index === 0 ? "border-b-2 pb-1 font-bold" : "pb-1 text-on-surface-variant transition-colors"}
                href="#"
                key={item}
                style={index === 0 ? { ...brandStyle, borderColor: branding.primaryColor } : undefined}
              >
                {item}
              </a>
            ))}
          </nav>
          <button className="rounded-full border px-4 py-2 text-label-md transition-colors" style={{ borderColor: branding.primaryColor, color: branding.primaryColor }} type="button">
            Liên hệ
          </button>
        </div>
      </header>

      <main className="flex min-h-screen flex-col items-center justify-center px-6 pb-40 pt-[112px]">
        <section className="mb-12 max-w-[800px] text-center">
          <h1 className="mb-4 text-headline-lg text-on-background">Empowering the Next Generation of Scholars</h1>
          <p className="text-body-md text-on-surface-variant">
            Explore our comprehensive educational ecosystem designed for academic excellence and modern learning methodologies.
          </p>
          {bootstrapQuery.isError ? (
            <p className="mt-4 rounded border border-warning/30 bg-warning/10 px-4 py-2 text-body-md text-warning">
              Không tải được cấu hình tenant, đang hiển thị bản mặc định.
            </p>
          ) : null}
        </section>

        <section className="grid w-full max-w-[1160px] grid-cols-1 gap-4 md:grid-cols-3">
          {featureCards.map((card) => (
            <article className="rounded-xl border border-outline-variant/50 bg-surface-container-lowest p-6 shadow-sm" key={card.title}>
              <div className={`mb-4 flex size-12 items-center justify-center rounded-full ${card.tone}`}>
                <span className="material-symbols-outlined">{card.icon}</span>
              </div>
              <h2 className="mb-2 text-headline-sm text-on-surface">{card.title}</h2>
              <p className="text-body-md text-on-surface-variant">{card.body}</p>
            </article>
          ))}
        </section>
      </main>

      <footer className="border-t border-outline-variant bg-surface-container-lowest py-4 opacity-80 transition-opacity hover:opacity-100">
        <div className="mx-auto flex max-w-[1160px] flex-col items-center justify-between gap-4 px-6 md:flex-row">
          <div className="text-headline-sm" style={brandStyle}>{bootstrap.tenantName}</div>
          <div className="flex gap-4 text-label-sm text-on-surface-variant">
            <a className="underline hover:text-primary" href="#">Privacy Policy</a>
            <a className="underline hover:text-primary" href="#">Terms of Service</a>
            <a className="font-bold underline" href="#" style={brandStyle}>Support</a>
          </div>
          <div className="text-label-sm text-on-surface-variant">© 2024 Học Bá Education. All rights reserved.</div>
        </div>
      </footer>

      <ChatWidget bootstrap={bootstrap} tenantSlug={tenantSlug} />
    </div>
  );
}
