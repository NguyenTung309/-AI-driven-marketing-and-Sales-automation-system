import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getContentPublishingPolicy,
  updateContentPublishingPolicy,
  type ContentPublishingApprovalPolicy,
  type ContentPublishingPolicy,
} from "@/shared/api/content";
import { useAuthStore } from "@/shared/auth/authStore";
import { Alert, StatusPill } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";

const CONTENT_PUBLISHING_POLICY_QUERY_KEY = ["content", "publishing-policy"] as const;

function visionLabel(capability: string): string {
  const value = capability.trim().toLowerCase();
  if (value === "available") return "Có hỗ trợ";
  if (value === "unavailable") return "Model hiện tại không hỗ trợ (sẽ bỏ qua ảnh)";
  return "Chưa xác định (thử một lần khi review có ảnh)";
}

function policyLabel(policy: string): string {
  return policy === "automatic" ? "Tự động phát hành" : "Cần người duyệt";
}

export function ContentPublishingPolicyControl({
  compact = false,
  className = "",
}: {
  readonly compact?: boolean;
  readonly className?: string;
}) {
  const queryClient = useQueryClient();
  const permissions = useAuthStore((state) => state.permissions);
  const canEdit = permissions.includes("system:config");

  const policyQuery = useQuery({
    queryKey: CONTENT_PUBLISHING_POLICY_QUERY_KEY,
    queryFn: getContentPublishingPolicy,
    staleTime: 60_000,
  });

  const policyMutation = useMutation({
    mutationFn: (publishingApprovalPolicy: ContentPublishingApprovalPolicy) =>
      updateContentPublishingPolicy({ publishingApprovalPolicy }),
    onSuccess: async (next) => {
      queryClient.setQueryData<ContentPublishingPolicy>(CONTENT_PUBLISHING_POLICY_QUERY_KEY, next);
      await queryClient.invalidateQueries({ queryKey: CONTENT_PUBLISHING_POLICY_QUERY_KEY });
    },
  });

  const policy = policyQuery.data;
  const selected = (policy?.publishingApprovalPolicy === "automatic" ? "automatic" : "human_required") as ContentPublishingApprovalPolicy;
  const busy = policyMutation.isPending || policyQuery.isFetching;

  function onSelect(next: ContentPublishingApprovalPolicy) {
    if (!canEdit || busy || next === selected) return;
    policyMutation.mutate(next);
  }

  return (
    <section
      className={`rounded-lg border border-outline bg-surface p-4 ${className}`}
      aria-labelledby="content-publishing-policy-title"
    >
      <div className="mb-3 flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 id="content-publishing-policy-title" className="text-body-md font-semibold text-secondary">
            Chính sách phát hành nội dung
          </h3>
          {!compact ? (
            <p className="mt-1 text-body-sm text-on-surface-variant">
              Agent review chữ luôn bắt buộc. Policy này chỉ quyết định có cần người duyệt sau review hay không.
              Cả hai chế độ đều tự chọn giờ vàng sau khi qua cổng duyệt.
            </p>
          ) : null}
        </div>
        {policy ? (
          <StatusPill tone={selected === "automatic" ? "warning" : "neutral"}>
            {policyLabel(selected)} · v{policy.policyVersion}
          </StatusPill>
        ) : null}
      </div>

      <div className="space-y-2 rounded-md border border-outline-variant bg-surface-container-low p-3">
        <p className="text-body-sm text-secondary">
          <span className="font-semibold">Agent review nội dung chữ:</span> Luôn bắt buộc
        </p>
        <p className="text-body-sm text-on-surface-variant">
          <span className="font-semibold text-secondary">Review hình ảnh:</span>{" "}
          {visionLabel(policy?.reviewerVisionCapability ?? "unknown")}
        </p>
      </div>

      <fieldset className="mt-4" disabled={!canEdit || busy || policyQuery.isLoading}>
        <legend className="mb-2 text-label-caps uppercase text-secondary">Chế độ phát hành</legend>
        <div className="grid gap-2 sm:grid-cols-2" role="radiogroup" aria-label="Chế độ phát hành nội dung">
          {(
            [
              {
                value: "human_required" as const,
                title: "Cần người duyệt",
                description: "Sau agent review, người có content:approve mới cho phát hành. An toàn mặc định.",
              },
              {
                value: "automatic" as const,
                title: "Tự động phát hành",
                description: "Agent review đạt thì hệ thống tự duyệt và tạo lịch giờ vàng. Non-pass vẫn giữ người.",
              },
            ] as const
          ).map((option) => {
            const active = selected === option.value;
            return (
              <label
                key={option.value}
                className={`cursor-pointer rounded-lg border px-3 py-3 transition-colors ${
                  active
                    ? "border-primary bg-primary/5 ring-1 ring-primary"
                    : "border-outline bg-white hover:bg-surface-container-low"
                } ${!canEdit ? "cursor-default opacity-90" : ""}`}
              >
                <span className="flex items-start gap-2">
                  <input
                    type="radio"
                    className="mt-1"
                    name="content-publishing-approval-policy"
                    value={option.value}
                    checked={active}
                    disabled={!canEdit || busy}
                    onChange={() => onSelect(option.value)}
                  />
                  <span>
                    <span className="block text-body-sm font-semibold text-secondary">{option.title}</span>
                    <span className="mt-1 block text-label-sm text-on-surface-variant">{option.description}</span>
                  </span>
                </span>
              </label>
            );
          })}
        </div>
      </fieldset>

      {!canEdit ? (
        <p className="mt-3 text-label-sm text-on-surface-variant">
          Chỉ admin (system:config) mới đổi được policy tenant-wide. Bạn đang xem chế độ hiện hành.
        </p>
      ) : (
        <p className="mt-3 text-label-sm text-on-surface-variant">
          Đổi policy không áp dụng ngược cho bài đang chờ. Bài mới / revision mới mới dùng version sau.
        </p>
      )}

      {policyQuery.isError ? (
        <div className="mt-3">
          <Alert tone="error">{toUserFriendlyError(policyQuery.error)}</Alert>
        </div>
      ) : null}
      {policyMutation.isError ? (
        <div className="mt-3">
          <Alert tone="error">{toUserFriendlyError(policyMutation.error)}</Alert>
        </div>
      ) : null}
      {policyMutation.isSuccess ? (
        <div className="mt-3">
          <Alert tone="success">
            Đã lưu chính sách phát hành: {policyLabel(policyMutation.data.publishingApprovalPolicy)}.
          </Alert>
        </div>
      ) : null}
    </section>
  );
}
