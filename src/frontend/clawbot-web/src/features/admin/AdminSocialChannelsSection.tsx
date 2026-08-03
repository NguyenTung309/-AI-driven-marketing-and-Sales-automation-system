import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Card, StatusPill, ToggleSwitch } from "@/shared/ui";
import { errorMessage, inputClass } from "./adminHelpers";
import { Field } from "./adminUi";
import {
  disconnectMeta,
  getMetaIntegrationStatus,
  getSocialCredentials,
  setDefaultMetaAsset,
  startMetaConnection,
  syncMetaAssets,
  updateInstagramCredential,
  updateMetaAppConfiguration,
  updateSocialCredential,
  validateMetaConnection,
  type MetaAuthorizationMode,
  type MetaIntegrationStatus,
  type SocialChannelCredential,
  type UpdateInstagramCredentialPayload,
} from "@/shared/api/admin";

interface ZaloFormState {
  readonly enabled: boolean;
  readonly endpoint: string;
  readonly oaId: string;
  readonly token: string;
  readonly clearToken: boolean;
}

interface InstagramFormState {
  readonly enabled: boolean;
  readonly userId: string;
  readonly token: string;
  readonly clearToken: boolean;
}

interface MetaAppFormState {
  readonly appId: string;
  readonly appSecret: string;
  readonly configurationId: string;
  readonly authorizationMode: MetaAuthorizationMode;
  readonly webhookVerifyToken: string;
  readonly redirectUri: string;
  readonly frontendReturnUrl: string;
}

const EMPTY_ZALO: ZaloFormState = { enabled: false, endpoint: "", oaId: "", token: "", clearToken: false };
const EMPTY_INSTAGRAM: InstagramFormState = { enabled: false, userId: "", token: "", clearToken: false };
const EMPTY_META_APP: MetaAppFormState = {
  appId: "",
  appSecret: "",
  configurationId: "",
  authorizationMode: "development_user",
  webhookVerifyToken: "",
  redirectUri: "http://localhost:15873/api/admin/meta/callback",
  frontendReturnUrl: "http://localhost:15876/system",
};

function formatDate(value: string | null | undefined): string {
  if (!value) return "Chưa có";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("vi-VN");
}

function metaCallbackError(reason: string | null): string {
  if (reason === "permissions_missing") return "Configuration Meta còn thiếu quyền đọc danh sách Page, đọc tương tác hoặc đăng bài.";
  if (reason === "business_system_user_required") return "Configuration phải dùng System-user access token cho tác vụ tự động.";
  if (reason === "user_access_token_required") return "Configuration ở chế độ phát triển phải dùng User access token.";
  if (reason === "token_invalid") return "Meta trả về token không hợp lệ cho App đang cấu hình.";
  if (reason === "authorization_denied") return "Bạn đã hủy hoặc chưa hoàn tất việc cấp quyền trên Meta.";
  if (reason === "app_not_configured") return "Hãy lưu đầy đủ cấu hình Meta App trên giao diện rồi kết nối lại.";
  if (reason === "code_missing") return "Meta không trả về authorization code để hoàn tất kết nối.";
  return "Không hoàn tất được kết nối Meta. Hãy kiểm tra cấu hình App và tài sản đã chọn.";
}

function MetaAppConfigurationForm({
  status,
  saving,
  onSave,
}: {
  readonly status: MetaIntegrationStatus | undefined;
  readonly saving: boolean;
  readonly onSave: (form: MetaAppFormState) => void;
}) {
  const config = status?.appConfiguration;
  const [form, setForm] = useState<MetaAppFormState>(() => config ? {
    appId: config.appId,
    appSecret: "",
    configurationId: config.configurationId,
    authorizationMode: config.configured ? (config.authorizationMode || "business_system_user") : EMPTY_META_APP.authorizationMode,
    webhookVerifyToken: "",
    redirectUri: config.redirectUri || EMPTY_META_APP.redirectUri,
    frontendReturnUrl: config.frontendReturnUrl || EMPTY_META_APP.frontendReturnUrl,
  } : EMPTY_META_APP);

  const isDevelopment = form.authorizationMode === "development_user";

  return (
    <div className="mt-5 rounded-lg border border-outline bg-surface p-4">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h4 className="text-title-md text-secondary">Cấu hình Meta App</h4>
          <p className="mt-1 text-label-sm text-on-surface-variant">
            App Secret và webhook token được mã hóa khi lưu. Đổi chế độ kết nối sẽ yêu cầu cấp quyền lại.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusPill tone={config?.source === "database" ? "success" : "neutral"}>
            {config?.source === "database" ? "Đã lưu trên hệ thống" : "Đang dùng cấu hình dự phòng"}
          </StatusPill>
          <StatusPill tone="neutral">Graph {config?.apiVersion || "v25.0"}</StatusPill>
        </div>
      </div>

      <div className="mb-4 grid grid-cols-1 gap-3 lg:grid-cols-2">
        <label className={`cursor-pointer rounded-lg border p-3 transition-colors ${isDevelopment ? "border-primary bg-primary/5" : "border-outline bg-white hover:border-primary/60"}`}>
          <span className="flex items-start gap-3">
            <input
              type="radio"
              name="meta-authorization-mode"
              className="mt-1 size-4 text-primary focus:ring-primary"
              checked={isDevelopment}
              onChange={() => setForm((old) => ({ ...old, authorizationMode: "development_user" }))}
            />
            <span>
              <span className="block text-body-md font-semibold text-secondary">Phát triển / kiểm thử</span>
              <span className="mt-1 block text-label-sm text-on-surface-variant">User access token; phù hợp để thử Page của bạn trên localhost.</span>
            </span>
          </span>
        </label>
        <label className={`cursor-pointer rounded-lg border p-3 transition-colors ${!isDevelopment ? "border-primary bg-primary/5" : "border-outline bg-white hover:border-primary/60"}`}>
          <span className="flex items-start gap-3">
            <input
              type="radio"
              name="meta-authorization-mode"
              className="mt-1 size-4 text-primary focus:ring-primary"
              checked={!isDevelopment}
              onChange={() => setForm((old) => ({ ...old, authorizationMode: "business_system_user" }))}
            />
            <span>
              <span className="block text-body-md font-semibold text-secondary">Production / tác vụ nền</span>
              <span className="mt-1 block text-label-sm text-on-surface-variant">System-user access token; dành cho kết nối doanh nghiệp dài hạn.</span>
            </span>
          </span>
        </label>
      </div>
     
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Field label="Meta App ID">
          <input
            className={inputClass}
            value={form.appId}
            onChange={(event) => setForm((old) => ({ ...old, appId: event.target.value }))}
            placeholder="Nhập App ID"
          />
        </Field>
        <Field label="Login for Business Configuration ID">
          <input
            className={inputClass}
            value={form.configurationId}
            onChange={(event) => setForm((old) => ({ ...old, configurationId: event.target.value }))}
            placeholder="Nhập Configuration ID"
          />
          <span className="mt-1 block text-label-sm text-on-surface-variant">
            Lấy tại Đăng nhập Facebook dành cho doanh nghiệp (Facebook Login for Business) &gt; Cấu hình. Configuration này phải chọn <strong>{isDevelopment ? "User access token" : "System-user access token"}</strong>.
          </span>
        </Field>
        <div className={isDevelopment ? "lg:col-span-2" : undefined}>
          <Field label="Meta App Secret">
            <input
              type="password"
              autoComplete="new-password"
              className={inputClass}
              value={form.appSecret}
              onChange={(event) => setForm((old) => ({ ...old, appSecret: event.target.value }))}
              placeholder={config?.hasAppSecret ? "Đã lưu, nhập để thay thế" : "Nhập App Secret"}
            />
          </Field>
        </div>
        {!isDevelopment ? (
          <Field label="Business Webhook Verify Token">
            <input
              type="password"
              autoComplete="new-password"
              className={inputClass}
              value={form.webhookVerifyToken}
              onChange={(event) => setForm((old) => ({ ...old, webhookVerifyToken: event.target.value }))}
              placeholder={config?.hasWebhookVerifyToken ? "Đã lưu, nhập để thay thế" : "Nhập verify token tự đặt"}
            />
          </Field>
        ) : null}
        <div className="lg:col-span-2">
          <Field label="OAuth Callback URL">
            <input
              className={inputClass}
              value={form.redirectUri}
              onChange={(event) => setForm((old) => ({ ...old, redirectUri: event.target.value }))}
            />
          </Field>
        </div>
        <div className="lg:col-span-2">
          <Field label="URL quay về giao diện sau OAuth">
            <input
              className={inputClass}
              value={form.frontendReturnUrl}
              onChange={(event) => setForm((old) => ({ ...old, frontendReturnUrl: event.target.value }))}
            />
          </Field>
        </div>
      </div>

      <div className="mt-4 flex justify-end">
        <Button type="button" disabled={saving} onClick={() => onSave(form)}>
          {saving ? "Đang lưu..." : "Lưu cấu hình Meta App"}
        </Button>
      </div>
    </div>
  );
}

function MetaCard({
  status,
  busy,
  configSaving,
  onSaveConfig,
  onConnect,
  onSync,
  onValidate,
  onDefault,
  onDisconnect,
}: {
  readonly status: MetaIntegrationStatus | undefined;
  readonly busy: boolean;
  readonly configSaving: boolean;
  readonly onSaveConfig: (form: MetaAppFormState) => void;
  readonly onConnect: () => void;
  readonly onSync: () => void;
  readonly onValidate: () => void;
  readonly onDefault: (assetId: string) => void;
  readonly onDisconnect: () => void;
}) {
  const connected = Boolean(status?.connected);
  const reconnectRequired = status?.status === "reconnect_required";
  const isDevelopment = !status?.configured || status.appConfiguration.authorizationMode === "development_user";
  const pages = status?.assets.filter((asset) => asset.assetType === "page" && asset.isActive) ?? [];

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="max-w-2xl">
          <div className="flex items-center gap-2">
            <span aria-hidden="true" className="material-symbols-outlined text-[22px] text-blue-700">hub</span>
            <h3 className="text-headline-sm text-secondary">Meta Facebook</h3>
          </div>
          <p className="mt-1 text-body-md text-on-surface-variant">
            {isDevelopment
              ? "Kết nối tài khoản có vai trò trong App để thử đăng Facebook Page ngay trên localhost."
              : "Kết nối Facebook Login for Business để quản lý Page token và tác vụ nền theo từng đơn vị."}
          </p>
        </div>
        {!status?.configured ? (
          <StatusPill tone="warning">Chưa lưu cấu hình Meta App</StatusPill>
        ) : reconnectRequired ? (
          <StatusPill tone="error">Cần kết nối lại</StatusPill>
        ) : connected ? (
          <StatusPill tone="success">Đã kết nối</StatusPill>
        ) : (
          <StatusPill tone="neutral">Chưa kết nối</StatusPill>
        )}
      </div>

      {status?.lastError ? <Alert tone="error">Kết nối Meta cần được kiểm tra lại.</Alert> : null}
      {status?.configured && !isDevelopment && !status.businessWebhookConfigured ? (
        <Alert tone="warning">Business Integration Webhook chưa cấu hình; hệ thống tạm dùng kiểm tra hằng ngày làm dự phòng.</Alert>
      ) : null}

      <MetaAppConfigurationForm
        key={status?.appConfiguration.updatedAt
          ?? `${status?.appConfiguration.source ?? "empty"}:${status?.appConfiguration.appId ?? ""}:${status?.appConfiguration.configurationId ?? ""}:${status?.appConfiguration.authorizationMode ?? ""}`}
        status={status}
        saving={configSaving}
        onSave={onSaveConfig}
      />

      {connected || reconnectRequired ? (
        <div className="mt-5 grid grid-cols-1 gap-3 rounded-lg border border-outline bg-surface p-4 md:grid-cols-3">
          <div>
            <p className="text-label-caps uppercase text-on-surface-variant">{isDevelopment ? "Meta User ID" : "Business ID"}</p>
            <p className="mt-1 break-all font-mono text-body-md text-secondary">{(isDevelopment ? status?.systemUserId : status?.clientBusinessId) || "Chưa xác định"}</p>
          </div>
          <div>
            <p className="text-label-caps uppercase text-on-surface-variant">Kiểm tra gần nhất</p>
            <p className="mt-1 text-body-md text-secondary">{formatDate(status?.lastValidatedAt)}</p>
          </div>
          <div>
            <p className="text-label-caps uppercase text-on-surface-variant">Ngày hết hạn token</p>
            <p className="mt-1 text-body-md text-secondary">{status?.expiresAt ? formatDate(status.expiresAt) : "Không có ngày hết hạn"}</p>
          </div>
        </div>
      ) : null}

      {connected ? (
        <div className="mt-5">
          <div className="mb-2 flex items-center justify-between gap-3">
            <div>
              <h4 className="text-title-md text-secondary">Facebook Pages được cấp quyền</h4>
              <p className="text-label-sm text-on-surface-variant">Chọn Page mặc định; khi lên lịch vẫn có thể chọn Page khác.</p>
            </div>
            <StatusPill tone={pages.length ? "success" : "warning"}>{pages.length} Page</StatusPill>
          </div>
          {pages.length ? (
            <div className="divide-y divide-outline overflow-hidden rounded-lg border border-outline bg-white">
              {pages.map((page) => (
                <label key={page.id} className="flex items-start gap-3 p-3 hover:bg-surface">
                  <input
                    type="radio"
                    name="default-meta-page"
                    className="mt-1"
                    checked={page.isDefault}
                    disabled={busy || !page.tasks.some((task) => task.toUpperCase() === "CREATE_CONTENT")}
                    onChange={() => onDefault(page.id)}
                  />
                  <span className="min-w-0 flex-1">
                    <span className="block text-body-md font-semibold text-secondary">{page.name}</span>
                    <span className="block break-all font-mono text-label-sm text-on-surface-variant">{page.externalId}</span>
                    {page.tasks.length ? (
                      <span className="mt-1 block text-label-sm text-on-surface-variant">Quyền tài sản: {page.tasks.join(", ")}</span>
                    ) : null}
                  </span>
                  {page.tasks.some((task) => task.toUpperCase() === "CREATE_CONTENT") ? (
                    page.isDefault ? <StatusPill tone="success">Mặc định</StatusPill> : null
                  ) : (
                    <StatusPill tone="warning">Thiếu quyền đăng</StatusPill>
                  )}
                </label>
              ))}
            </div>
          ) : (
            <Alert tone="warning">Meta chưa trả về Page nào có quyền. Hãy kiểm tra tài sản đã chọn trong màn cấp quyền.</Alert>
          )}
        </div>
      ) : null}

      <div className="mt-5 flex flex-wrap justify-end gap-2">
        {connected ? (
          <>
            <Button type="button" variant="outline" disabled={busy} onClick={onValidate}>Kiểm tra token</Button>
            <Button type="button" variant="outline" disabled={busy} onClick={onSync}>Đồng bộ Pages</Button>
            <Button type="button" variant="ghost" disabled={busy} onClick={onDisconnect}>Ngắt tại ClawBot</Button>
          </>
        ) : null}
        <Button type="button" disabled={busy || !status?.configured} onClick={onConnect}>
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">login</span>
          {reconnectRequired ? "Kết nối lại Meta" : connected ? "Cấp lại quyền" : "Kết nối Meta"}
        </Button>
      </div>
    </Card>
  );
}

function InstagramCard({ credential, ready, saving, onSave }: {
  readonly credential: SocialChannelCredential | undefined;
  readonly ready: boolean;
  readonly saving: boolean;
  readonly onSave: (form: InstagramFormState) => Promise<SocialChannelCredential | null>;
}) {
  const [form, setForm] = useState<InstagramFormState>(() => credential ? {
    enabled: credential.enabled,
    userId: credential.pageId,
    token: "",
    clearToken: false,
  } : EMPTY_INSTAGRAM);
  const hasTokenAfterSave = !form.clearToken
    && (form.token.trim().length > 0 || credential?.hasPageAccessToken === true);
  const hasValidEnabledCredentials = !form.enabled
    || (/^\d+$/.test(form.userId.trim()) && hasTokenAfterSave);
  const isInvalidStoredCredential = credential?.resolutionState === "invalid";
  const canSaveInvalidCredential = !isInvalidStoredCredential
    || form.clearToken
    || (form.enabled && form.token.trim().length > 0);
  const controlsDisabled = saving || !ready;

  const save = async (submission: InstagramFormState) => {
    const normalizedSubmission = {
      ...submission,
      userId: submission.userId.trim(),
      token: submission.token.trim(),
    };
    setForm((current) => ({ ...current, token: "", clearToken: false }));
    try {
      const saved = await onSave(normalizedSubmission);
      if (!saved) return;
      setForm({
        enabled: saved.enabled,
        userId: saved.pageId,
        token: "",
        clearToken: false,
      });
    } catch {
      // The parent keeps only a safe message; secret-bearing mutation state is reset there.
    }
  };

  const clearAndDisable = () => save({
    enabled: false,
    userId: "",
    token: "",
    clearToken: true,
  });

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-headline-sm text-secondary">Instagram độc lập (tùy chọn)</h3>
          <p className="mt-1 max-w-2xl text-body-md text-on-surface-variant">
            Khi tắt, ClawBot dùng tài khoản Instagram liên kết trong Meta. Bật chỉ khi bạn muốn ghi đè bằng tài khoản riêng.
          </p>
        </div>
        <StatusPill tone={!ready ? "neutral" : isInvalidStoredCredential ? "error" : form.enabled ? "success" : "neutral"}>
          {!ready
            ? "Đang tải cấu hình"
            : isInvalidStoredCredential
              ? "Cần sửa cấu hình"
              : form.enabled
                ? "Đang dùng thông tin riêng"
                : "Đang dùng mặc định"}
        </StatusPill>
      </div>

      {isInvalidStoredCredential ? (
        <div className="mb-4 space-y-3">
          <Alert tone="error">
            Không đọc được thông tin Instagram đã lưu. Hãy nhập lại đầy đủ User ID và access token, hoặc xóa thông tin riêng để quay về Instagram liên kết trong Meta.
          </Alert>
          <Button
            type="button"
            variant="outline"
            disabled={controlsDisabled}
            onClick={() => void clearAndDisable()}
          >
            Tắt và xóa thông tin Instagram riêng
          </Button>
        </div>
      ) : null}

      <div className="mb-4">
        <ToggleSwitch
          checked={form.enabled}
          disabled={controlsDisabled}
          onChange={(enabled) => setForm((current) => ({ ...current, enabled }))}
          label="Dùng thông tin Instagram riêng"
        />
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Field label="Instagram User ID">
          <input
            className={inputClass}
            inputMode="numeric"
            pattern="[0-9]*"
            disabled={controlsDisabled}
            placeholder="17841400000000000"
            value={form.userId}
            onChange={(event) => setForm((current) => ({ ...current, userId: event.target.value }))}
          />
        </Field>
        <Field label="Access token Instagram">
          <input
            type="password"
            autoComplete="new-password"
            className={inputClass}
            disabled={controlsDisabled || form.clearToken}
            placeholder={credential?.hasPageAccessToken ? "Đã lưu — nhập để thay" : "Nhập access token"}
            value={form.token}
            onChange={(event) => setForm((current) => ({ ...current, token: event.target.value }))}
          />
        </Field>
      </div>

      <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-body-sm text-on-surface-variant">
            Để trống để giữ mã truy cập đã lưu. Mã không bao giờ được hiển thị lại.
          </p>
          <label className="mt-2 inline-flex items-center gap-2 text-body-sm text-on-surface-variant">
            <input
              type="checkbox"
              checked={form.clearToken}
              disabled={controlsDisabled}
              onChange={(event) => setForm((current) => ({
                ...current,
                clearToken: event.target.checked,
                token: event.target.checked ? "" : current.token,
              }))}
            />
            Xóa mã truy cập đã lưu (nếu có)
          </label>
          {!hasValidEnabledCredentials ? (
            <p className="mt-2 text-body-sm text-error">Cần Instagram User ID dạng số và access token để bật ghi đè.</p>
          ) : null}
        </div>
        <Button
          type="button"
          disabled={controlsDisabled || !hasValidEnabledCredentials || !canSaveInvalidCredential}
          onClick={() => void save(form)}
        >
          {saving ? "Đang lưu..." : "Lưu Instagram"}
        </Button>
      </div>
    </Card>
  );
}

function ZaloCard({ credential, saving, onSave }: {
  readonly credential: SocialChannelCredential | undefined;
  readonly saving: boolean;
  readonly onSave: (form: ZaloFormState) => void;
}) {
  const [form, setForm] = useState<ZaloFormState>(() => credential ? {
      enabled: credential.enabled,
      endpoint: credential.endpoint,
      oaId: credential.oaId,
      token: "",
      clearToken: false,
    } : EMPTY_ZALO);

  return (
    <Card>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h3 className="text-headline-sm text-secondary">Zalo OA</h3>
          <p className="mt-1 text-body-md text-on-surface-variant">Cấu hình OA dùng để đăng nội dung đã duyệt.</p>
        </div>
        <div className="flex items-center gap-2">
          {credential?.hasOaAccessToken ? <StatusPill tone="success">Đã có token</StatusPill> : <StatusPill tone="neutral">Chưa có token</StatusPill>}
          <ToggleSwitch checked={form.enabled} onChange={(enabled) => setForm((old) => ({ ...old, enabled }))} />
        </div>
      </div>
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Field label="OA ID">
          <input className={inputClass} value={form.oaId} onChange={(event) => setForm((old) => ({ ...old, oaId: event.target.value }))} />
        </Field>
        <Field label="Endpoint API">
          <input className={inputClass} value={form.endpoint} onChange={(event) => setForm((old) => ({ ...old, endpoint: event.target.value }))} />
        </Field>
        <div className="lg:col-span-2">
          <Field label="OA Access Token">
            <input
              type="password"
              autoComplete="off"
              className={inputClass}
              value={form.token}
              onChange={(event) => setForm((old) => ({ ...old, token: event.target.value, clearToken: false }))}
              placeholder={credential?.hasOaAccessToken ? "Đã lưu, nhập để thay thế" : "Nhập OA access token"}
            />
          </Field>
          {credential?.hasOaAccessToken ? (
            <label className="mt-1 flex items-center gap-2 text-label-sm text-on-surface-variant">
              <input type="checkbox" checked={form.clearToken} onChange={(event) => setForm((old) => ({ ...old, clearToken: event.target.checked }))} />
              Xóa token đã lưu
            </label>
          ) : null}
        </div>
      </div>
      <div className="mt-4 flex justify-end">
        <Button type="button" disabled={saving} onClick={() => onSave(form)}>{saving ? "Đang lưu..." : "Lưu Zalo OA"}</Button>
      </div>
    </Card>
  );
}

export function AdminSocialChannelsSection() {
  const queryClient = useQueryClient();
  const metaQuery = useQuery({ queryKey: ["admin", "meta"], queryFn: getMetaIntegrationStatus });
  const credentialsQuery = useQuery({ queryKey: ["admin", "social-credentials"], queryFn: getSocialCredentials });
  const instagram = credentialsQuery.data?.find((credential) => credential.provider === "instagram");
  const zalo = credentialsQuery.data?.find((credential) => credential.provider === "zalo");

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["admin", "meta"] });
  const configMutation = useMutation({
    mutationFn: (form: MetaAppFormState) => updateMetaAppConfiguration({
      appId: form.appId.trim(),
      appSecret: form.appSecret.trim() || null,
      configurationId: form.configurationId.trim(),
      authorizationMode: form.authorizationMode,
      webhookVerifyToken: form.webhookVerifyToken.trim() || null,
      redirectUri: form.redirectUri.trim(),
      frontendReturnUrl: form.frontendReturnUrl.trim(),
    }),
    onSuccess: refresh,
  });
  const connectMutation = useMutation({
    mutationFn: startMetaConnection,
    onSuccess: ({ authorizationUrl }) => window.location.assign(authorizationUrl),
  });
  const syncMutation = useMutation({ mutationFn: syncMetaAssets, onSettled: refresh });
  const validateMutation = useMutation({ mutationFn: validateMetaConnection, onSuccess: refresh });
  const defaultMutation = useMutation({ mutationFn: setDefaultMetaAsset, onSuccess: refresh });
  const disconnectMutation = useMutation({
    mutationFn: disconnectMeta,
    onSuccess: async () => {
      await refresh();
    },
  });
  const zaloMutation = useMutation({
    mutationFn: (form: ZaloFormState) => updateSocialCredential("zalo", {
      enabled: form.enabled,
      endpoint: form.endpoint.trim(),
      oaId: form.oaId.trim(),
      oaAccessToken: form.clearToken ? "" : form.token.trim() || null,
    }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "social-credentials"] }),
  });
  const instagramPayloadRef = useRef<UpdateInstagramCredentialPayload | null>(null);
  const [instagramSaveError, setInstagramSaveError] = useState<string | null>(null);
  const instagramMutation = useMutation({
    gcTime: 0,
    mutationFn: async () => {
      const payload = instagramPayloadRef.current;
      if (!payload) throw new Error("Instagram save payload is unavailable.");
      return updateInstagramCredential(payload);
    },
  });

  const saveInstagram = async (form: InstagramFormState): Promise<SocialChannelCredential | null> => {
    setInstagramSaveError(null);
    instagramPayloadRef.current = {
      enabled: form.enabled,
      pageId: form.userId,
      pageAccessToken: form.clearToken ? "" : form.token || null,
    };
    try {
      const saved = await instagramMutation.mutateAsync();
      await queryClient.cancelQueries({ queryKey: ["admin", "social-credentials"] });
      queryClient.setQueryData<readonly SocialChannelCredential[]>(
        ["admin", "social-credentials"],
        (current) => {
          if (!current) return [saved];
          const hasInstagram = current.some((credential) => credential.provider === "instagram");
          return hasInstagram
            ? current.map((credential) => credential.provider === "instagram" ? saved : credential)
            : [...current, saved];
        },
      );
      return saved;
    } catch (error: unknown) {
      setInstagramSaveError(errorMessage(error));
      return null;
    } finally {
      instagramPayloadRef.current = null;
      instagramMutation.reset();
    }
  };

  const metaBusy = configMutation.isPending || connectMutation.isPending || syncMutation.isPending || validateMutation.isPending || defaultMutation.isPending || disconnectMutation.isPending;
  const error = metaQuery.error ?? credentialsQuery.error ?? configMutation.error ?? connectMutation.error ?? syncMutation.error ?? validateMutation.error ?? defaultMutation.error ?? disconnectMutation.error ?? zaloMutation.error;
  const callbackParams = typeof window === "undefined" ? null : new URLSearchParams(window.location.search);
  const callbackResult = callbackParams?.get("meta") ?? null;
  const callbackReason = callbackParams?.get("meta_reason") ?? null;

  return (
    <div className="space-y-gutter">
      <div>
        <h2 className="text-headline-sm text-secondary">Kênh đăng bài</h2>
        <p className="mt-1 text-body-md text-on-surface-variant">Cấu hình Meta App và kết nối OAuth ngay tại đây; mọi secret và token đều được mã hóa, không hiển thị lại trên giao diện.</p>
      </div>
      {callbackResult === "connected" ? <Alert tone="success">Đã kết nối Meta và đồng bộ các Page được cấp quyền.</Alert> : null}
      {callbackResult === "error" ? <Alert tone="error">{metaCallbackError(callbackReason)}</Alert> : null}
      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
      {instagramSaveError ? <Alert tone="error">{instagramSaveError}</Alert> : null}
      <MetaCard
        status={metaQuery.data}
        busy={metaBusy}
        configSaving={configMutation.isPending}
        onSaveConfig={(form) => configMutation.mutate(form)}
        onConnect={() => connectMutation.mutate()}
        onSync={() => syncMutation.mutate()}
        onValidate={() => validateMutation.mutate()}
        onDefault={(assetId) => defaultMutation.mutate(assetId)}
        onDisconnect={() => {
          if (window.confirm("Xóa token Meta khỏi ClawBot? Muốn thu hồi hoàn toàn, bạn vẫn cần gỡ ứng dụng trong phần cài đặt Facebook/Meta.")) {
            disconnectMutation.mutate();
          }
        }}
      />
      <InstagramCard
        key={instagram ? `${instagram.updatedAt ?? "configured"}:${instagram.resolutionState}:${instagram.enabled}:${instagram.pageId}:${instagram.hasPageAccessToken}` : "instagram-loading"}
        credential={instagram}
        ready={credentialsQuery.data !== undefined}
        saving={instagramMutation.isPending}
        onSave={saveInstagram}
      />
      <ZaloCard
        key={zalo ? `${zalo.updatedAt ?? "configured"}:${zalo.enabled}:${zalo.endpoint}:${zalo.oaId}` : "zalo-loading"}
        credential={zalo}
        saving={zaloMutation.isPending}
        onSave={(form) => zaloMutation.mutate(form)}
      />
    </div>
  );
}
