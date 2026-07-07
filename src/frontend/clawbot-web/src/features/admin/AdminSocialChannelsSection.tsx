import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Card, StatusPill, ToggleSwitch } from "@/shared/ui";
import { errorMessage, Field, inputClass } from "./adminHelpers";
import {
  getSocialCredentials,
  updateSocialCredential,
  type SocialChannelCredential,
  type UpdateSocialChannelPayload,
} from "@/shared/api/admin";

interface ChannelFormState {
  readonly enabled: boolean;
  readonly endpoint: string;
  readonly channelId: string;
  readonly token: string;
  readonly clearToken: boolean;
}

const EMPTY_FORM: ChannelFormState = { enabled: false, endpoint: "", channelId: "", token: "", clearToken: false };

function ChannelCard({
  title,
  description,
  idLabel,
  tokenLabel,
  credential,
  hasToken,
  onSave,
  saving,
}: {
  readonly title: string;
  readonly description: string;
  readonly idLabel: string;
  readonly tokenLabel: string;
  readonly credential: SocialChannelCredential | undefined;
  readonly hasToken: boolean;
  readonly onSave: (form: ChannelFormState) => void;
  readonly saving: boolean;
}) {
  const [form, setForm] = useState<ChannelFormState>(EMPTY_FORM);

  useEffect(() => {
    if (!credential) return;
    setForm({
      enabled: credential.enabled,
      endpoint: credential.endpoint,
      channelId: credential.provider === "facebook" ? credential.pageId : credential.oaId,
      token: "",
      clearToken: false,
    });
  }, [credential]);

  return (
    <Card>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h3 className="text-headline-sm text-secondary">{title}</h3>
          <p className="mt-1 text-body-md text-on-surface-variant">{description}</p>
        </div>
        <div className="flex items-center gap-2">
          {hasToken ? <StatusPill tone="success">Đã có token</StatusPill> : <StatusPill tone="neutral">Chưa có token</StatusPill>}
          <ToggleSwitch checked={form.enabled} onChange={(enabled) => setForm((f) => ({ ...f, enabled }))} />
        </div>
      </div>
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Field label={idLabel}>
          <input
            className={inputClass}
            value={form.channelId}
            onChange={(event) => setForm((f) => ({ ...f, channelId: event.target.value }))}
          />
        </Field>
        <Field label="Endpoint API">
          <input
            className={inputClass}
            value={form.endpoint}
            onChange={(event) => setForm((f) => ({ ...f, endpoint: event.target.value }))}
            placeholder="https://..."
          />
        </Field>
        <div className="lg:col-span-2">
          <Field label={tokenLabel}>
            <input
              type="password"
              autoComplete="off"
              className={inputClass}
              value={form.token}
              onChange={(event) => setForm((f) => ({ ...f, token: event.target.value, clearToken: false }))}
              placeholder={hasToken ? "•••••• (đã lưu — nhập để thay)" : "Dán access token"}
            />
          </Field>
          {hasToken ? (
            <label className="mt-1 flex items-center gap-2 text-label-sm text-on-surface-variant">
              <input
                type="checkbox"
                checked={form.clearToken}
                onChange={(event) => setForm((f) => ({ ...f, clearToken: event.target.checked }))}
              />
              Xóa token đã lưu
            </label>
          ) : null}
        </div>
      </div>
      <div className="mt-4 flex justify-end">
        <Button type="button" disabled={saving} onClick={() => onSave(form)}>
          {saving ? "Đang lưu..." : "Lưu kênh"}
        </Button>
      </div>
    </Card>
  );
}

// Publish-channel credentials (FB page / Zalo OA) — stored encrypted per tenant in social_credentials;
// GraphSocialPublisher resolves the same rows when ContentPublishJob runs.
export function AdminSocialChannelsSection() {
  const queryClient = useQueryClient();
  const credentialsQuery = useQuery({ queryKey: ["admin", "social-credentials"], queryFn: getSocialCredentials });

  const saveMutation = useMutation({
    mutationFn: ({ provider, payload }: { provider: string; payload: UpdateSocialChannelPayload }) =>
      updateSocialCredential(provider, payload),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "social-credentials"] }),
  });

  const items = credentialsQuery.data ?? [];
  const facebook = items.find((c) => c.provider === "facebook");
  const zalo = items.find((c) => c.provider === "zalo");
  const error = credentialsQuery.error ?? saveMutation.error;

  function save(provider: "facebook" | "zalo", form: ChannelFormState) {
    const token = form.clearToken ? "" : form.token.trim() ? form.token.trim() : null;
    const payload: UpdateSocialChannelPayload =
      provider === "facebook"
        ? { enabled: form.enabled, endpoint: form.endpoint.trim(), pageId: form.channelId.trim(), pageAccessToken: token }
        : { enabled: form.enabled, endpoint: form.endpoint.trim(), oaId: form.channelId.trim(), oaAccessToken: token };
    saveMutation.mutate({ provider, payload });
  }

  return (
    <div className="space-y-gutter">
      <div>
        <h2 className="text-headline-sm text-secondary">Kênh đăng bài</h2>
        <p className="mt-1 text-body-md text-on-surface-variant">
          Thông tin xác thực để hệ thống tự đăng nội dung đã duyệt. Token được mã hoá, không hiển thị lại sau khi lưu.
        </p>
      </div>
      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
      <div className="grid grid-cols-1 gap-gutter xl:grid-cols-2">
        <ChannelCard
          title="Facebook Page"
          description="Cần Page ID + Page Access Token (quyền pages_manage_posts)."
          idLabel="Page ID"
          tokenLabel="Page Access Token"
          credential={facebook}
          hasToken={Boolean(facebook?.hasPageAccessToken)}
          saving={saveMutation.isPending}
          onSave={(form) => save("facebook", form)}
        />
        <ChannelCard
          title="Zalo OA"
          description="Cần OA ID + OA Access Token của Official Account."
          idLabel="OA ID"
          tokenLabel="OA Access Token"
          credential={zalo}
          hasToken={Boolean(zalo?.hasOaAccessToken)}
          saving={saveMutation.isPending}
          onSave={(form) => save("zalo", form)}
        />
      </div>
    </div>
  );
}
