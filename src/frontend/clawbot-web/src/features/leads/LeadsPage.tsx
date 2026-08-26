import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "react-router-dom";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert } from "@/shared/ui/Alert";
import { Card } from "@/shared/ui/Card";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { useDebounce } from "@/shared/ui";
import {
  getLead,
  getLeadContext,
  getLeadForecast,
  listLeads,
  recordLeadActivity,
  updateLeadStage,
  type LeadContext,
  type LeadContextActivity,
  type LeadListItem,
  type LeadStage,
  type LeadStageAction,
} from "@/shared/api/leads";
import { useAuthStore } from "@/shared/auth/authStore";
import { toUserFriendlyError } from "@/shared/utils/userText";

const PAGE_SIZE = 20;

function generatePageNumbers(current: number, total: number): (number | "...")[] {
  if (total <= 7) {
    return Array.from({ length: total }, (_, i) => i + 1);
  }
  if (current <= 4) {
    return [1, 2, 3, 4, 5, "...", total];
  }
  if (current >= total - 3) {
    return [1, "...", total - 4, total - 3, total - 2, total - 1, total];
  }
  return [1, "...", current - 1, current, current + 1, "...", total];
}

type OwnerFilter = "all" | "assigned" | "unassigned";
type DrawerTab = "timeline" | "context";

const STAGES: readonly { value: LeadStage; label: string; tone: StatusTone; icon: string }[] = [
  { value: "hot", label: "Nóng", tone: "error", icon: "local_fire_department" },
  { value: "warm", label: "Ấm", tone: "warning", icon: "trending_up" },
  { value: "cold", label: "Lạnh", tone: "neutral", icon: "ac_unit" },
  { value: "customer", label: "Khách hàng", tone: "success", icon: "verified" },
  { value: "lost", label: "Đã mất", tone: "neutral", icon: "do_not_disturb_on" },
];

const ACTIVITY_EVENTS = [
  { value: "lead_price_view", label: "Xem bảng giá" },
  { value: "lead_phone_call", label: "Gọi tư vấn" },
  { value: "lead_demo_booked", label: "Đặt lịch học thử" },
  { value: "lead_chat_reply", label: "Phản hồi chat" },
] as const;

const EMPTY_LEADS: readonly LeadListItem[] = [];
const EMPTY_ACTIVITIES: readonly LeadContextActivity[] = [];

function normalize(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function errorMessage(error: unknown): string {
  return toUserFriendlyError(error, "Không thể xử lý yêu cầu.");
}

function stageConfig(stage: LeadStage) {
  return STAGES.find((item) => normalize(item.value) === normalize(stage)) ?? STAGES[2];
}

function stageLabel(stage: LeadStage): string {
  const config = stageConfig(stage);
  if (normalize(config.value) === normalize(stage)) return config.label;
  return stage || "Chưa rõ";
}

function stageTone(stage: LeadStage): StatusTone {
  return stageConfig(stage).tone;
}

function sourceLabel(source: string | null): string {
  const value = normalize(source);
  if (!value) return "Không rõ nguồn";
  if (value.includes("zalo")) return "Zalo";
  if (value.includes("facebook") || value === "fb") return "FB Page";
  if (value.includes("website") || value.includes("web")) return "Website";
  return source ?? "Không rõ nguồn";
}

function ownerLabel(lead: LeadListItem): string {
  if (lead.ownerDisplayName?.trim()) return lead.ownerDisplayName.trim();
  if (!lead.ownerUserId) return "Chưa phân công";
  return `Sale ${lead.ownerUserId.slice(0, 8)}`;
}

function contactLabel(lead: LeadListItem, context?: LeadContext | null): string {
  const name = lead.contactName?.trim() || context?.contact?.name?.trim();
  if (name) return name;
  return `Lead ${lead.id.slice(0, 8)}`;
}

function contactMeta(lead: LeadListItem, context?: LeadContext | null): string {
  if (lead.contactPhone?.trim()) return lead.contactPhone.trim();
  const contact = context?.contact;
  if (contact?.phone) return contact.phone;
  if (contact?.email) return contact.email;
  if (lead.contactId) return `Khách ${lead.contactId.slice(0, 8)}`;
  return "Chưa có khách hàng";
}

function formatDateTime(value: string | null): string {
  if (!value) return "Chưa có";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function exportLeadCsv(leads: readonly LeadListItem[]) {
  const rows = [
    ["id", "contact_id", "owner_user_id", "score", "stage", "source_platform", "last_activity_at", "created_at"],
    ...leads.map((lead) => [
      lead.id,
      lead.contactId ?? "",
      lead.ownerUserId ?? "",
      String(lead.score),
      lead.stage,
      lead.sourcePlatform ?? "",
      lead.lastActivityAt ?? "",
      lead.createdAt,
    ]),
  ];
  const csv = rows.map((row) => row.map((cell) => `"${cell.replaceAll('"', '""')}"`).join(",")).join("\n");
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = "leads.csv";
  link.click();
  URL.revokeObjectURL(url);
}

function formatRelative(value: string | null): string {
  if (!value) return "Chưa có hoạt động";
  const at = new Date(value).getTime();
  if (Number.isNaN(at)) return formatDateTime(value);
  const diff = Date.now() - at;
  const minutes = Math.max(0, Math.round(diff / 60000));
  if (minutes < 1) return "Vừa xong";
  if (minutes < 60) return `${minutes} phút trước`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} giờ trước`;
  const days = Math.round(hours / 24);
  if (days < 7) return `${days} ngày trước`;
  return formatDateTime(value);
}

function scoreBarColor(score: number): string {
  if (score >= 70) return "bg-error";
  if (score >= 30) return "bg-warning";
  return "bg-blue-500";
}

function scoreWidth(score: number): string {
  return `${Math.min(100, Math.max(0, score))}%`;
}

function activityLabel(type: string): string {
  const value = normalize(type);
  if (value.includes("score")) return "Chấm điểm";
  if (value.includes("call")) return "Cuộc gọi";
  if (value.includes("demo")) return "Lịch học thử";
  if (value.includes("chat") || value.includes("reply")) return "Tin nhắn";
  if (value.includes("price")) return "Xem bảng giá";
  return type || "Hoạt động";
}

function LeadScore({ score }: { readonly score: number }) {
  return (
    <div className="min-w-[120px]">
      <div className="mb-1 flex items-center gap-2">
        <span className="font-mono text-mono-status font-bold text-secondary">{score}</span>
        <span className="text-[11px] font-bold uppercase text-on-surface-variant">điểm</span>
      </div>
      <div className="h-1.5 overflow-hidden rounded-full bg-surface-variant">
        <div className={`h-full rounded-full ${scoreBarColor(score)}`} style={{ width: scoreWidth(score) }} />
      </div>
    </div>
  );
}

function LeadTable({
  leads,
  selectedId,
  onSelect,
}: {
  readonly leads: readonly LeadListItem[];
  readonly selectedId: string | null;
  readonly onSelect: (lead: LeadListItem) => void;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="min-w-[780px] w-full border-collapse text-left">
        <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
          <tr>
            <th className="px-4 py-3 font-bold">Khách hàng</th>
            <th className="px-4 py-3 font-bold">Điểm AI & Nhãn</th>
            <th className="px-4 py-3 font-bold">Nguồn / Thời gian</th>
            <th className="px-4 py-3 font-bold">Phụ trách</th>
            <th className="px-4 py-3 text-right font-bold">Thao tác</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-outline bg-white">
          {leads.map((lead) => (
            <tr className={selectedId === lead.id ? "bg-red-50/70" : "hover:bg-surface-container-low"} key={lead.id}>
              <td className="px-4 py-4 align-top">
                <button className="block max-w-[240px] text-left" onClick={() => onSelect(lead)} type="button">
                  <span className="block truncate text-body-md font-bold text-secondary">{contactLabel(lead)}</span>
                  <span className="mt-1 block text-label-sm text-on-surface-variant">{contactMeta(lead)}</span>
                </button>
              </td>
              <td className="px-4 py-4 align-top">
                <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
                  <LeadScore score={lead.score} />
                  <StatusPill tone={stageTone(lead.stage)}>{stageLabel(lead.stage)}</StatusPill>
                </div>
              </td>
              <td className="px-4 py-4 align-top">
                <p className="text-body-md font-semibold text-secondary">{sourceLabel(lead.sourcePlatform)}</p>
                <p className="mt-1 text-label-sm text-on-surface-variant">{formatRelative(lead.lastActivityAt ?? lead.createdAt)}</p>
              </td>
              <td className="px-4 py-4 align-top">
                <p className={lead.ownerUserId ? "text-body-md font-semibold text-secondary" : "text-body-md text-on-surface-variant"}>
                  {ownerLabel(lead)}
                </p>
              </td>
              <td className="px-4 py-4 align-top">
                <div className="flex justify-end gap-2">
                  <button
                    className="rounded bg-primary px-3 py-2 text-label-sm font-bold text-on-primary hover:bg-primary-hover"
                    onClick={() => onSelect(lead)}
                    type="button"
                  >
                    Chi tiết
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function KanbanBoard({ leads, onSelect }: { readonly leads: readonly LeadListItem[]; readonly onSelect: (lead: LeadListItem) => void }) {
  return (
    <section className="grid grid-cols-1 gap-gutter xl:grid-cols-5">
      {STAGES.map((stage) => {
        const items = leads.filter((lead) => normalize(lead.stage) === normalize(stage.value));
        const avgScore = items.length ? Math.round(items.reduce((sum, item) => sum + item.score, 0) / items.length) : 0;
        return (
          <Card className="flex min-h-[280px] flex-col p-0" key={stage.value}>
            <div className="flex items-center justify-between border-b border-outline px-4 py-3">
              <div className="flex items-center gap-2">
                <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-primary">{stage.icon}</span>
                <h3 className="text-body-md font-bold text-secondary">{stage.label}</h3>
              </div>
              <span className="rounded-full bg-surface-container px-2 py-0.5 font-mono text-mono-status text-on-surface-variant">
                {items.length}
              </span>
            </div>
            <div className="border-b border-outline px-4 py-2 text-label-sm text-on-surface-variant">
              Điểm TB: <span className="font-mono font-bold text-secondary">{avgScore}</span>
            </div>
            <div className="flex flex-1 flex-col gap-3 p-3">
              {items.length ? (
                items.slice(0, 4).map((lead) => (
                  <button
                    className="rounded-lg border border-outline bg-white p-3 text-left transition-colors hover:border-primary hover:bg-red-50"
                    key={lead.id}
                    onClick={() => onSelect(lead)}
                    type="button"
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="truncate text-body-md font-bold text-secondary">{contactLabel(lead)}</p>
                        <p className="mt-1 text-label-sm text-on-surface-variant">{sourceLabel(lead.sourcePlatform)}</p>
                      </div>
                      <span className="font-mono text-mono-status font-bold text-primary">{lead.score}</span>
                    </div>
                    <p className="mt-2 text-label-sm text-on-surface-variant">{ownerLabel(lead)}</p>
                  </button>
                ))
              ) : (
                <div className="flex flex-1 items-center justify-center rounded-lg border border-dashed border-outline p-4 text-center text-body-md text-on-surface-variant">
                  Chưa có lead ở trạng thái này
                </div>
              )}
              {items.length > 4 ? <p className="text-center text-label-sm text-on-surface-variant">+{items.length - 4} lead khác trong bảng</p> : null}
            </div>
          </Card>
        );
      })}
    </section>
  );
}

function LeadDrawer({
  lead,
  detail,
  context,
  loading,
  onClose,
  onRecordActivity,
  recording,
  onStageAction,
  stagePending,
  canWrite,
}: {
  readonly lead: LeadListItem;
  readonly detail: LeadListItem | null;
  readonly context: LeadContext | null;
  readonly loading: boolean;
  readonly onClose: () => void;
  readonly onRecordActivity: (eventCode: string, notes: string) => void;
  readonly recording: boolean;
  readonly onStageAction: (action: LeadStageAction, reason?: string) => void;
  readonly stagePending: boolean;
  readonly canWrite: boolean;
}) {
  const [tab, setTab] = useState<DrawerTab>("timeline");
  const [eventCode, setEventCode] = useState<string>(ACTIVITY_EVENTS[0].value);
  const [notes, setNotes] = useState("");
  const [wonOpen, setWonOpen] = useState(false);
  const [wonReason, setWonReason] = useState("");
  const hydratedLead = detail ?? lead;
  const activities = context?.activities ?? EMPTY_ACTIVITIES;
  const stageNorm = normalize(hydratedLead.stage);
  const isTerminal = stageNorm === "customer" || stageNorm === "lost";

  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-black/35">
      <aside className="flex h-full w-full max-w-[440px] flex-col bg-surface-container-lowest shadow-2xl">
        <div className="border-b border-outline p-5">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0">
              <p className="text-label-sm uppercase text-on-surface-variant">Chi tiết lead</p>
              <h2 className="mt-1 truncate text-headline-sm font-bold text-secondary">{contactLabel(hydratedLead, context)}</h2>
              <p className="mt-1 text-label-sm text-on-surface-variant">{contactMeta(hydratedLead, context)}</p>
            </div>
            <button
              aria-label="Đóng chi tiết lead"
              className="rounded-full p-2 text-on-surface-variant hover:bg-surface-container"
              onClick={onClose}
              type="button"
            >
              <span aria-hidden="true" className="material-symbols-outlined text-[20px]">close</span>
            </button>
          </div>
          <div className="mt-4 flex flex-wrap items-center gap-2">
            <StatusPill tone={stageTone(hydratedLead.stage)}>{stageLabel(hydratedLead.stage)}</StatusPill>
            <span className="rounded-full bg-red-100 px-2.5 py-0.5 font-mono text-mono-status font-bold text-primary">
              {hydratedLead.score} điểm
            </span>
            <span className="rounded-full bg-surface-container px-2.5 py-0.5 text-label-sm font-semibold text-on-surface-variant">
              {sourceLabel(hydratedLead.sourcePlatform)}
            </span>
          </div>
          {canWrite ? (
            <div className="mt-4 flex flex-wrap gap-2">
              {!isTerminal ? (
                <>
                  <button
                    className="rounded bg-success px-3 py-1.5 text-label-sm font-bold text-on-primary hover:opacity-90 disabled:opacity-60"
                    disabled={stagePending}
                    onClick={() => setWonOpen(true)}
                    type="button"
                  >
                    Đã chốt
                  </button>
                  <button
                    className="rounded border border-outline bg-white px-3 py-1.5 text-label-sm font-bold text-secondary hover:bg-surface-container disabled:opacity-60"
                    disabled={stagePending}
                    onClick={() => onStageAction("lost", "Đánh dấu mất")}
                    type="button"
                  >
                    Đánh dấu mất
                  </button>
                </>
              ) : (
                <button
                  className="rounded border border-outline bg-white px-3 py-1.5 text-label-sm font-bold text-secondary hover:bg-surface-container disabled:opacity-60"
                  disabled={stagePending}
                  onClick={() => onStageAction("reopen", "Mở lại pipeline")}
                  type="button"
                >
                  Mở lại
                </button>
              )}
            </div>
          ) : null}
          {wonOpen ? (
            <form
              className="mt-4 space-y-3 rounded-lg border border-outline bg-surface-container-low p-3"
              onSubmit={(event) => {
                event.preventDefault();
                onStageAction("customer", wonReason.trim() || "Đã chốt");
                setWonOpen(false);
                setWonReason("");
              }}
            >
              <p className="text-label-sm font-bold text-secondary">Xác nhận đã chốt</p>
              <p className="text-label-sm text-on-surface-variant">
                Lead sẽ chuyển sang trạng thái khách hàng và rời pipeline.
              </p>
              <input
                className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md text-secondary focus:border-primary focus:outline-none"
                onChange={(event) => setWonReason(event.target.value)}
                placeholder="Lý do / ghi chú"
                value={wonReason}
              />
              <div className="flex gap-2">
                <button
                  className="flex-1 rounded bg-primary px-3 py-2 text-label-sm font-bold text-on-primary disabled:opacity-60"
                  disabled={stagePending}
                  type="submit"
                >
                  Xác nhận
                </button>
                <button
                  className="rounded border border-outline bg-white px-3 py-2 text-label-sm font-bold text-secondary"
                  onClick={() => setWonOpen(false)}
                  type="button"
                >
                  Huỷ
                </button>
              </div>
            </form>
          ) : null}
        </div>

        <div className="grid grid-cols-2 border-b border-outline">
          <button
            className={`px-2 py-3 text-label-caps uppercase ${
              tab === "timeline" ? "border-b-2 border-primary text-primary" : "text-on-surface-variant"
            }`}
            onClick={() => setTab("timeline")}
            type="button"
          >
            Lịch sử
          </button>
          <button
            className={`px-2 py-3 text-label-caps uppercase ${
              tab === "context" ? "border-b-2 border-primary text-primary" : "text-on-surface-variant"
            }`}
            onClick={() => setTab("context")}
            type="button"
          >
            Ngữ cảnh
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          {loading ? (
            <div className="rounded-lg border border-outline bg-surface-container-low p-4 text-body-md text-on-surface-variant">
              Đang tải dữ liệu chấm điểm...
            </div>
          ) : tab === "timeline" ? (
            <div className="space-y-5">
              <div className="rounded-lg border border-outline bg-surface-container-lowest p-4">
                <p className="text-label-caps uppercase text-on-surface-variant">Phụ trách</p>
                <p className="mt-1 text-body-md font-bold text-secondary">{ownerLabel(hydratedLead)}</p>
                <p className="mt-1 text-label-sm text-on-surface-variant">Sale phụ trách theo kênh của khách, hệ thống tự gán.</p>
              </div>

              <form
                className="rounded-lg border border-outline bg-red-50/60 p-4"
                onSubmit={(event) => {
                  event.preventDefault();
                  onRecordActivity(eventCode, notes);
                  setNotes("");
                }}
              >
                <label className="text-label-caps uppercase text-on-surface-variant" htmlFor="lead-activity-event">
                  Ghi nhận tương tác
                </label>
                <select
                  className="mt-2 w-full rounded border border-outline bg-white px-3 py-2 text-body-md text-secondary focus:border-primary focus:outline-none"
                  id="lead-activity-event"
                  onChange={(event) => setEventCode(event.target.value)}
                  value={eventCode}
                >
                  {ACTIVITY_EVENTS.map((event) => (
                    <option key={event.value} value={event.value}>
                      {event.label}
                    </option>
                  ))}
                </select>
                <textarea
                  className="mt-3 min-h-[82px] w-full resize-none rounded border border-outline bg-white px-3 py-2 text-body-md text-secondary focus:border-primary focus:outline-none"
                  onChange={(event) => setNotes(event.target.value)}
                  placeholder="Ghi chú ngắn cho dòng thời gian chấm điểm"
                  value={notes}
                />
                <button
                  className="mt-3 w-full rounded bg-primary px-4 py-2 text-body-md font-bold text-on-primary hover:bg-primary-hover disabled:cursor-not-allowed disabled:opacity-60"
                  disabled={recording}
                  type="submit"
                >
                  {recording ? "Đang ghi" : "Lưu hoạt động"}
                </button>
              </form>

              <div className="space-y-3">
                {activities.length ? (
                  activities.map((activity, index) => (
                    <article className="relative pl-8" key={`${activity.activityType}-${activity.occurredAt}-${index}`}>
                      <span className="absolute left-2 top-2 size-3 rounded-full border-2 border-primary bg-white" />
                      {index < activities.length - 1 ? <span className="absolute bottom-[-20px] left-[13px] top-5 w-px bg-outline" /> : null}
                      <div className={index === 0 ? "rounded-lg bg-primary p-4 text-on-primary" : "rounded-lg border border-outline bg-white p-4"}>
                        <div className="flex items-center justify-between gap-3">
                          <p className="text-label-sm font-bold uppercase">{activityLabel(activity.activityType)}</p>
                          <p className={index === 0 ? "text-label-sm text-red-100" : "text-label-sm text-on-surface-variant"}>
                            {formatDateTime(activity.occurredAt)}
                          </p>
                        </div>
                        <p className="mt-2 text-body-md">{activity.notes || "Chưa có ghi chú cho hoạt động này."}</p>
                      </div>
                    </article>
                  ))
                ) : (
                  <div className="rounded-lg border border-dashed border-outline p-4 text-body-md text-on-surface-variant">
                    Chưa có hoạt động chấm điểm. Khi sale ghi nhận tương tác, dòng thời gian sẽ tự cập nhật.
                  </div>
                )}
              </div>
            </div>
          ) : (
            <div className="space-y-4">
              <Card>
                <p className="text-label-caps uppercase text-on-surface-variant">Gợi ý tiếp theo</p>
                <p className="mt-2 text-body-lg font-bold text-secondary">{context?.nextStep ?? "Đang chờ hệ thống tổng hợp"}</p>
              </Card>
              <Card>
                <p className="text-label-caps uppercase text-on-surface-variant">Thông tin liên hệ</p>
                <dl className="mt-3 space-y-3 text-body-md">
                  <div className="flex justify-between gap-3">
                    <dt className="text-on-surface-variant">Tên</dt>
                    <dd className="text-right font-semibold text-secondary">{context?.contact?.name ?? "Chưa có"}</dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-on-surface-variant">Điện thoại</dt>
                    <dd className="text-right font-semibold text-secondary">{context?.contact?.phone ?? "Chưa có"}</dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-on-surface-variant">Email</dt>
                    <dd className="text-right font-semibold text-secondary">{context?.contact?.email ?? "Chưa có"}</dd>
                  </div>
                </dl>
              </Card>
              <Card>
                <p className="text-label-caps uppercase text-on-surface-variant">Mốc thời gian</p>
                <dl className="mt-3 space-y-3 text-body-md">
                  <div className="flex justify-between gap-3">
                    <dt className="text-on-surface-variant">Tạo lead</dt>
                    <dd className="text-right font-semibold text-secondary">{formatDateTime(hydratedLead.createdAt)}</dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-on-surface-variant">Hoạt động cuối</dt>
                    <dd className="text-right font-semibold text-secondary">{formatDateTime(hydratedLead.lastActivityAt)}</dd>
                  </div>
                </dl>
              </Card>
            </div>
          )}
        </div>

        <div className="border-t border-outline p-4">
          <Link
            className="flex w-full items-center justify-center gap-2 rounded bg-primary px-4 py-3 text-body-md font-bold text-on-primary hover:bg-primary-hover"
            to="/inbox"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">forum</span>
            Xem chi tiết cuộc trò chuyện
          </Link>
        </div>
      </aside>
    </div>
  );
}

export default function LeadsPage() {
  const queryClient = useQueryClient();
  const { leadId: routeLeadId } = useParams<{ leadId?: string }>();
  const navigate = useNavigate();
  const canWrite = useAuthStore((s) => s.permissions.includes("leads:write"));
  const [search, setSearch] = useState("");
  const [source, setSource] = useState("all");
  const [stage, setStage] = useState("all");
  const [owner, setOwner] = useState<OwnerFilter>("all");
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const activeSelectedId = routeLeadId ?? selectedId;
  const [notice, setNotice] = useState<{ readonly tone: "success" | "error"; readonly message: string } | null>(null);

  useEffect(() => {
    if (!notice) return;
    const timeout = window.setTimeout(() => setNotice(null), 3_800);
    return () => window.clearTimeout(timeout);
  }, [notice]);

  const debouncedSearch = useDebounce(search, 300);

  // Reset về trang 1 khi thay đổi điều kiện tìm kiếm hoặc bộ lọc
  useEffect(() => {
    setPage(1);
  }, [debouncedSearch, stage, source, owner]);

  // Query danh sách phân trang (20 lead/trang) cho LeadTable
  const leadsQuery = useQuery({
    queryKey: ["leads", "list", page, PAGE_SIZE, stage, debouncedSearch, source, owner],
    queryFn: () =>
      listLeads({
        page,
        pageSize: PAGE_SIZE,
        stage: stage === "all" ? undefined : stage,
        q: debouncedSearch.trim() || undefined,
        source: source === "all" ? undefined : source,
        owner: owner === "all" ? undefined : owner,
      }),
  });

  // Query tổng thể (không phân trang theo table) cho Bảng xử lý Kanban và thống kê tổng quan
  const kanbanLeadsQuery = useQuery({
    queryKey: ["leads", "kanban", stage, debouncedSearch, source, owner],
    queryFn: () =>
      listLeads({
        page: 1,
        pageSize: 200,
        stage: stage === "all" ? undefined : stage,
        q: debouncedSearch.trim() || undefined,
        source: source === "all" ? undefined : source,
        owner: owner === "all" ? undefined : owner,
      }),
    staleTime: 30_000,
  });

  const forecastQuery = useQuery({
    queryKey: ["leads", "forecast"],
    queryFn: () => getLeadForecast(7),
    staleTime: 60_000,
  });
  const detailQuery = useQuery({
    queryKey: ["leads", activeSelectedId, "detail"],
    queryFn: () => getLead(activeSelectedId ?? ""),
    enabled: Boolean(activeSelectedId),
  });
  const contextQuery = useQuery({
    queryKey: ["leads", activeSelectedId, "context"],
    queryFn: () => getLeadContext(activeSelectedId ?? ""),
    enabled: Boolean(activeSelectedId),
  });

  const leads = leadsQuery.data?.items ?? EMPTY_LEADS;
  const kanbanLeads = kanbanLeadsQuery.data?.items ?? EMPTY_LEADS;
  const leadsTotal = leadsQuery.data?.total ?? kanbanLeadsQuery.data?.total ?? leads.length;
  const totalPages = Math.max(1, Math.ceil(leadsTotal / PAGE_SIZE));
  // All filters (stage/q/source) are server-side; list is already filtered.
  const filteredLeads = leads;
  const selectedLead = useMemo(
    () => leads.find((lead) => lead.id === activeSelectedId) ?? kanbanLeads.find((lead) => lead.id === activeSelectedId) ?? null,
    [leads, kanbanLeads, activeSelectedId]
  );
  // Fixed catalog so dropdown is not truncated by loaded pages; merge extras + keep selection.
  const sourceOptions = useMemo(() => {
    const known = ["zalo", "facebook", "website"];
    const fromRows = kanbanLeads.map((lead) => lead.sourcePlatform).filter((v): v is string => Boolean(v?.trim()));
    const selected = source !== "all" ? [source] : [];
    return Array.from(new Set([...known, ...fromRows, ...selected])).sort((a, b) =>
      sourceLabel(a).localeCompare(sourceLabel(b), "vi"),
    );
  }, [kanbanLeads, source]);
  const hotLeads = kanbanLeads.filter((lead) => normalize(lead.stage) === "hot");
  const avgScore = kanbanLeads.length ? Math.round(kanbanLeads.reduce((sum, lead) => sum + lead.score, 0) / kanbanLeads.length) : 0;
  const forecastTotal = forecastQuery.data?.forecast.reduce((sum, point) => sum + point.predicted_leads, 0) ?? 0;

  const activityMutation = useMutation({
    mutationFn: ({ leadId, eventCode, notes }: { readonly leadId: string; readonly eventCode: string; readonly notes: string }) =>
      recordLeadActivity(leadId, { eventCode, platform: selectedLead?.sourcePlatform ?? null, notes: notes.trim() || null }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["leads"] });
      setNotice({ tone: "success", message: "Đã ghi nhận hoạt động." });
    },
    onError: (error) => setNotice({ tone: "error", message: errorMessage(error) }),
  });

  const stageMutation = useMutation({
    mutationFn: ({
      leadId,
      action,
      reason,
    }: {
      readonly leadId: string;
      readonly action: LeadStageAction;
      readonly reason?: string;
    }) => updateLeadStage(leadId, { stage: action, reason: reason ?? null }),
    onSuccess: async (res) => {
      await queryClient.invalidateQueries({ queryKey: ["leads"] });
      const label =
        normalize(res.stage) === "customer"
          ? "Đã chuyển thành khách hàng."
          : normalize(res.stage) === "lost"
            ? "Đã đánh dấu mất khách."
            : `Đã mở lại pipeline (${stageLabel(res.stage)}).`;
      setNotice({ tone: "success", message: label });
    },
    onError: (error) => setNotice({ tone: "error", message: errorMessage(error) }),
  });

  return (
    <AppShell title="Khách hàng tiềm năng">
      <div className="mb-stack-lg flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-headline-md font-bold text-secondary">Khách hàng tiềm năng</h1>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Quản lý lead, bảng xử lý và dòng thời gian chấm điểm.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusPill tone={leadsQuery.isError ? "error" : "success"}>{leadsQuery.isError ? "Mất kết nối" : "Đã kết nối"}</StatusPill>
          <button
            className="inline-flex items-center gap-2 rounded border border-outline bg-white px-4 py-2 text-body-md font-bold text-secondary hover:border-primary hover:text-primary disabled:cursor-not-allowed disabled:opacity-60"
            disabled={!filteredLeads.length}
            onClick={() => exportLeadCsv(filteredLeads)}
            type="button"
          >
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">download</span>
            Tải danh sách
          </button>
        </div>
      </div>

      {notice ? (
        <div className="mb-gutter">
          <Alert tone={notice.tone}>{notice.message}</Alert>
        </div>
      ) : null}

      <section className="mb-gutter grid grid-cols-1 gap-gutter md:grid-cols-3">
        <Card>
          <p className="text-label-caps uppercase text-on-surface-variant">Tổng lead</p>
          <p className="mt-2 text-telemetry-data text-secondary">{leadsTotal.toLocaleString("vi-VN")}</p>
          <p className="mt-1 text-label-sm text-on-surface-variant">Sắp xếp theo điểm AI</p>
        </Card>
        <Card>
          <p className="text-label-caps uppercase text-on-surface-variant">Lead nóng</p>
          <p className="mt-2 text-telemetry-data text-primary">{hotLeads.length.toLocaleString("vi-VN")}</p>
          <p className="mt-1 text-label-sm text-on-surface-variant">Nhóm nóng cần xử lý trước</p>
        </Card>
        <Card>
          <p className="text-label-caps uppercase text-on-surface-variant">Dự báo 7 ngày</p>
          <p className="mt-2 text-telemetry-data text-secondary">
            {forecastQuery.data?.note ? "--" : Math.round(forecastTotal).toLocaleString("vi-VN")}
          </p>
          <p className="mt-1 text-label-sm text-on-surface-variant">
            {forecastQuery.data?.note ? "Cần thêm dữ liệu lịch sử" : `Điểm TB ${avgScore}`}
          </p>
        </Card>
      </section>

      <Card className="mb-gutter p-0">
        <div className="border-b border-outline p-card-padding">
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(220px,1.4fr)_repeat(2,minmax(150px,1fr))]">
            <label className="block">
              <span className="text-label-sm font-semibold text-on-surface-variant">Tìm kiếm Lead</span>
              <div className="mt-2 flex items-center gap-2 rounded border border-outline bg-white px-3 py-2 focus-within:border-primary">
                <span aria-hidden="true" className="material-symbols-outlined text-[18px] text-on-surface-variant">search</span>
                <input
                  className="w-full bg-transparent text-body-md text-secondary outline-none"
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Tên, mã, nguồn..."
                  value={search}
                />
              </div>
            </label>
            <label className="block">
              <span className="text-label-sm font-semibold text-on-surface-variant">Nguồn</span>
              <select
                className="mt-2 w-full rounded border border-outline bg-white px-3 py-2 text-body-md text-secondary focus:border-primary focus:outline-none"
                onChange={(event) => setSource(event.target.value)}
                value={source}
              >
                <option value="all">Tất cả nguồn</option>
                {sourceOptions.map((option) => (
                  <option key={option} value={option}>
                    {sourceLabel(option)}
                  </option>
                ))}
              </select>
            </label>
            {/* <label className="block">
              <span className="text-label-sm font-semibold text-on-surface-variant">Agent phụ trách</span>
              <select
                className="mt-2 w-full rounded border border-outline bg-white px-3 py-2 text-body-md text-secondary focus:border-primary focus:outline-none"
                onChange={(event) => setOwner(event.target.value as OwnerFilter)}
                value={owner}
              >
                <option value="all">Tất cả</option>
                <option value="assigned">Đã phân công</option>
                <option value="unassigned">Chưa phân công</option>
              </select>
            </label> */}
            <label className="block">
              <span className="text-label-sm font-semibold text-on-surface-variant">Trạng thái Sale</span>
              <select
                className="mt-2 w-full rounded border border-outline bg-white px-3 py-2 text-body-md text-secondary focus:border-primary focus:outline-none"
                onChange={(event) => setStage(event.target.value)}
                value={stage}
              >
                <option value="all">Tất cả</option>
                {STAGES.map((item) => (
                  <option key={item.value} value={item.value}>
                    {item.label}
                  </option>
                ))}
              </select>
            </label>
          </div>
        </div>

        {leadsQuery.isLoading ? (
          <div className="p-card-padding text-body-md text-on-surface-variant">Đang tải danh sách lead...</div>
        ) : leadsQuery.isError ? (
          <div className="p-card-padding text-body-md text-error">Không tải được dữ liệu lead. Vui lòng thử lại hoặc kiểm tra quyền truy cập.</div>
        ) : filteredLeads.length ? (
          <>
            <LeadTable
              leads={filteredLeads}
              onSelect={(lead) => setSelectedId(lead.id)}
              selectedId={activeSelectedId}
            />
            <div className="flex flex-col gap-3 border-t border-outline px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
              <div className="text-label-sm text-on-surface-variant">
                Hiển thị <span className="font-semibold text-secondary">{filteredLeads.length > 0 ? (page - 1) * PAGE_SIZE + 1 : 0}</span> -{" "}
                <span className="font-semibold text-secondary">{Math.min(page * PAGE_SIZE, leadsTotal)}</span> trên{" "}
                <span className="font-semibold text-secondary">{leadsTotal.toLocaleString("vi-VN")}</span> lead
              </div>
              <div className="flex items-center gap-1.5">
                <button
                  type="button"
                  disabled={page <= 1 || leadsQuery.isFetching}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  className="inline-flex items-center gap-1 rounded border border-outline bg-white px-3 py-1.5 text-label-sm font-semibold text-secondary hover:bg-surface-variant disabled:cursor-not-allowed disabled:opacity-40"
                  aria-label="Trang trước"
                >
                  <span aria-hidden="true" className="material-symbols-outlined text-[16px]">chevron_left</span>
                  Trước
                </button>

                <div className="flex items-center gap-1 px-1">
                  {generatePageNumbers(page, totalPages).map((p, idx) =>
                    p === "..." ? (
                      <span key={`ellipsis-${idx}`} className="px-2 text-label-sm text-on-surface-variant">...</span>
                    ) : (
                      <button
                        key={p}
                        type="button"
                        onClick={() => setPage(Number(p))}
                        disabled={leadsQuery.isFetching}
                        className={`min-w-[32px] rounded px-2.5 py-1 text-label-sm font-bold transition-colors ${
                          page === p
                            ? "bg-primary text-on-primary"
                            : "border border-outline bg-white text-secondary hover:bg-surface-variant"
                        }`}
                      >
                        {p}
                      </button>
                    )
                  )}
                </div>

                <button
                  type="button"
                  disabled={page >= totalPages || leadsQuery.isFetching}
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                  className="inline-flex items-center gap-1 rounded border border-outline bg-white px-3 py-1.5 text-label-sm font-semibold text-secondary hover:bg-surface-variant disabled:cursor-not-allowed disabled:opacity-40"
                  aria-label="Trang sau"
                >
                  Sau
                  <span aria-hidden="true" className="material-symbols-outlined text-[16px]">chevron_right</span>
                </button>
              </div>
            </div>
          </>
        ) : (
          <div className="p-card-padding text-body-md text-on-surface-variant">Không có lead phù hợp với bộ lọc hiện tại.</div>
        )}
      </Card>

      <div className="mb-stack-md flex items-center justify-between gap-3">
        <div>
          <h2 className="text-headline-sm font-bold text-secondary">Bảng xử lý</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Nhóm lead theo mức chấm điểm để sale xử lý theo ưu tiên.</p>
        </div>
      </div>
      <KanbanBoard leads={kanbanLeads} onSelect={(lead) => setSelectedId(lead.id)} />

      {selectedLead ? (
        <LeadDrawer
          canWrite={canWrite}
          context={contextQuery.data ?? null}
          detail={detailQuery.data ?? null}
          lead={selectedLead}
          loading={detailQuery.isLoading || contextQuery.isLoading}
          onClose={() => {
            setSelectedId(null);
            if (routeLeadId) navigate("/leads", { replace: true });
          }}
          onRecordActivity={(eventCode, notes) => activityMutation.mutate({ leadId: selectedLead.id, eventCode, notes })}
          onStageAction={(action, reason) => stageMutation.mutate({ leadId: selectedLead.id, action, reason })}
          recording={activityMutation.isPending}
          stagePending={stageMutation.isPending}
        />
      ) : null}
    </AppShell>
  );
}
