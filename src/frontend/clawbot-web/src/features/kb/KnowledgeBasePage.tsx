import { useDeferredValue, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert } from "@/shared/ui/Alert";
import { Modal } from "@/shared/ui/Modal";
import { StatusPill } from "@/shared/ui/StatusPill";
import {
  addKbTestCase,
  archiveKbModule,
  createKbModule,
  createKbVersion,
  deployKbVersion,
  getKbAccuracy,
  getKbVersion,
  getKbVersionDiff,
  listKbModules,
  listKbTestCases,
  listKbVersions,
  rollbackKbVersion,
  runKbTest,
  updateKbModule,
  type CreateKbModulePayload,
  type KbAccuracySummary,
  type KbModule,
  type KbTestCase,
  type KbVersion,
  type KbVersionDiff,
} from "@/shared/api/kb";
import { ModuleFormModal, QaModal, type ModuleDialogMode } from "./KnowledgeBaseDialogs";
import { AccuracyPanel, DiffDrawer, EditorWorkspace, ModuleRail, VersionRail } from "./KnowledgeBaseWorkspace";

const EMPTY_MODULES: readonly KbModule[] = [];
const EMPTY_VERSIONS: readonly KbVersion[] = [];
const EMPTY_TEST_CASES: readonly KbTestCase[] = [];
const EMPTY_ACCURACY: readonly KbAccuracySummary[] = [];

function normalize(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function errorText(error: unknown): string {
  if (error instanceof Error) return error.message;
  return "Backend không thể xử lý yêu cầu.";
}

function moduleMatches(module: KbModule, query: string): boolean {
  const value = normalize(query);
  if (!value) return true;
  return [module.code, module.name, module.description, module.ownerRole].some((field) => normalize(field).includes(value));
}

function moduleAccuracy(accuracy: readonly KbAccuracySummary[], moduleId: string): KbAccuracySummary | null {
  return accuracy.find((item) => item.kbModuleId === moduleId) ?? null;
}

export default function KnowledgeBasePage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const deferredSearch = useDeferredValue(search);
  const [selectedModuleId, setSelectedModuleId] = useState<string | null>(null);
  const [selectedVersionId, setSelectedVersionId] = useState<string | null>(null);
  const [moduleDialog, setModuleDialog] = useState<ModuleDialogMode>(null);
  const [qaOpen, setQaOpen] = useState(false);
  const [diff, setDiff] = useState<KbVersionDiff | null>(null);
  const [archiveConfirm, setArchiveConfirm] = useState(false);

  const modulesQuery = useQuery({ queryKey: ["kb", "modules"], queryFn: listKbModules });
  const accuracyQuery = useQuery({ queryKey: ["kb", "accuracy"], queryFn: getKbAccuracy });
  const modules = modulesQuery.data ?? EMPTY_MODULES;
  const accuracy = accuracyQuery.data ?? EMPTY_ACCURACY;
  const visibleModules = useMemo(() => modules.filter((module) => moduleMatches(module, deferredSearch)), [modules, deferredSearch]);
  const selectedModule = useMemo(
    () => modules.find((module) => module.id === selectedModuleId) ?? modules[0] ?? null,
    [modules, selectedModuleId]
  );

  const versionsQuery = useQuery({
    queryKey: ["kb", selectedModule?.id, "versions"],
    queryFn: () => listKbVersions(selectedModule?.id ?? ""),
    enabled: Boolean(selectedModule?.id),
  });
  const versions = versionsQuery.data ?? EMPTY_VERSIONS;
  const selectedVersion = useMemo(
    () => versions.find((version) => version.id === selectedVersionId) ?? versions[0] ?? null,
    [versions, selectedVersionId]
  );
  const versionDetailQuery = useQuery({
    queryKey: ["kb", selectedModule?.id, "versions", selectedVersion?.id],
    queryFn: () => getKbVersion(selectedModule?.id ?? "", selectedVersion?.id ?? ""),
    enabled: Boolean(selectedModule?.id && selectedVersion?.id),
  });
  const testCasesQuery = useQuery({
    queryKey: ["kb", selectedModule?.id, "test-cases"],
    queryFn: () => listKbTestCases(selectedModule?.id ?? ""),
    enabled: Boolean(selectedModule?.id && qaOpen),
  });
  const testCases = testCasesQuery.data ?? EMPTY_TEST_CASES;
  const selectedAccuracy = selectedModule ? moduleAccuracy(accuracy, selectedModule.id) : null;

  const selectModule = (id: string) => {
    setSelectedModuleId(id);
    setSelectedVersionId(null);
    setDiff(null);
  };

  const moduleMutation = useMutation({
    mutationFn: async (payload: CreateKbModulePayload) => {
      if (moduleDialog === "edit" && selectedModule) {
        await updateKbModule(selectedModule.id, {
          name: payload.name,
          description: payload.description,
          ownerRole: payload.ownerRole,
        });
        return selectedModule.id;
      }
      const created = await createKbModule(payload);
      return created.id;
    },
    onSuccess: async (id) => {
      setSelectedModuleId(id);
      setSelectedVersionId(null);
      setModuleDialog(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["kb", "modules"] }),
        queryClient.invalidateQueries({ queryKey: ["kb", "accuracy"] }),
      ]);
    },
  });

  const archiveMutation = useMutation({
    mutationFn: (id: string) => archiveKbModule(id),
    onSuccess: async () => {
      setSelectedModuleId(null);
      setSelectedVersionId(null);
      setArchiveConfirm(false);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["kb", "modules"] }),
        queryClient.invalidateQueries({ queryKey: ["kb", "accuracy"] }),
      ]);
    },
  });

  const saveVersionMutation = useMutation({
    mutationFn: (content: string) => createKbVersion(selectedModule?.id ?? "", content),
    onSuccess: async (created) => {
      setSelectedVersionId(created.id);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["kb", selectedModule?.id, "versions"] }),
        queryClient.invalidateQueries({ queryKey: ["kb", "modules"] }),
      ]);
    },
  });

  const deploymentMutation = useMutation({
    mutationFn: ({ rollback }: { readonly rollback: boolean }) =>
      rollback
        ? rollbackKbVersion(selectedModule?.id ?? "", selectedVersion?.id ?? "")
        : deployKbVersion(selectedModule?.id ?? "", selectedVersion?.id ?? ""),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["kb", selectedModule?.id, "versions"] }),
        queryClient.invalidateQueries({ queryKey: ["kb", selectedModule?.id, "versions", selectedVersion?.id] }),
        queryClient.invalidateQueries({ queryKey: ["kb", "modules"] }),
      ]);
    },
  });

  const addTestCaseMutation = useMutation({
    mutationFn: ({ question, answer }: { readonly question: string; readonly answer: string }) =>
      addKbTestCase(selectedModule?.id ?? "", question, answer),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["kb", selectedModule?.id, "test-cases"] });
    },
  });

  const testMutation = useMutation({
    mutationFn: () => runKbTest(selectedModule?.id ?? ""),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["kb", "accuracy"] }),
        queryClient.invalidateQueries({ queryKey: ["kb", selectedModule?.id, "versions"] }),
      ]);
    },
  });

  const diffMutation = useMutation({
    mutationFn: () =>
      getKbVersionDiff(
        selectedModule?.id ?? "",
        Math.max(1, (selectedVersion?.version ?? 1) - 1),
        selectedVersion?.version ?? 1
      ),
    onSuccess: setDiff,
  });

  const errors = [
    modulesQuery.error,
    accuracyQuery.error,
    versionsQuery.error,
    versionDetailQuery.error,
    moduleMutation.error,
    archiveMutation.error,
    saveVersionMutation.error,
    deploymentMutation.error,
    addTestCaseMutation.error,
    testMutation.error,
    diffMutation.error,
  ].filter(Boolean);

  return (
    <AppShell title="Kho tri thức Markdown">
      <div className="mb-stack-lg flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-headline-md font-bold text-secondary">Kho tri thức Markdown</h1>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Quản lý module, version, deploy và kiểm định độ chính xác cho RAG.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusPill tone={errors.length ? "error" : "success"}>{errors.length ? "Mất kết nối" : "Đã kết nối"}</StatusPill>
          <button
            className="inline-flex items-center gap-2 rounded bg-primary px-4 py-2 text-body-md font-bold text-white hover:bg-primary-hover"
            onClick={() => setModuleDialog("create")}
            type="button"
          >
            <span className="material-symbols-outlined text-[18px]">add</span>
            Tạo module mới
          </button>
        </div>
      </div>

      {errors.length ? (
        <div className="mb-gutter">
          <Alert tone="error">{errorText(errors[0])}</Alert>
        </div>
      ) : null}

      <section className="overflow-hidden rounded-lg border border-outline shadow-sm xl:grid xl:grid-cols-[250px_310px_minmax(0,1fr)]">
        <ModuleRail
          loading={modulesQuery.isLoading}
          modules={visibleModules}
          onCreate={() => setModuleDialog("create")}
          onSearch={setSearch}
          onSelect={selectModule}
          search={search}
          selectedId={selectedModule?.id ?? null}
        />
        <VersionRail
          accuracy={selectedAccuracy}
          loading={versionsQuery.isLoading}
          module={selectedModule}
          onSelect={setSelectedVersionId}
          selectedId={selectedVersion?.id ?? null}
          versions={versions}
        />
        <EditorWorkspace
          deploying={deploymentMutation.isPending}
          initialContent={versionDetailQuery.data?.contentMd ?? ""}
          key={`${selectedModule?.id ?? "none"}-${selectedVersion?.id ?? "new"}-${versionDetailQuery.data ? "loaded" : "loading"}`}
          loading={versionDetailQuery.isLoading}
          module={selectedModule}
          onArchive={() => setArchiveConfirm(true)}
          onCompare={() => diffMutation.mutate()}
          onDeploy={() => deploymentMutation.mutate({ rollback: false })}
          onEditModule={() => setModuleDialog("edit")}
          onOpenQa={() => setQaOpen(true)}
          onRollback={() => deploymentMutation.mutate({ rollback: true })}
          onSave={(content) => saveVersionMutation.mutate(content)}
          saving={saveVersionMutation.isPending}
          testPending={testMutation.isPending}
          version={selectedVersion}
        />
      </section>

      <AccuracyPanel items={accuracy} loading={accuracyQuery.isLoading} />

      <ModuleFormModal
        key={`${moduleDialog ?? "closed"}-${selectedModule?.id ?? "none"}`}
        mode={moduleDialog}
        module={selectedModule}
        onClose={() => setModuleDialog(null)}
        onSubmit={(payload) => moduleMutation.mutate(payload)}
        pending={moduleMutation.isPending}
      />
      <QaModal
        adding={addTestCaseMutation.isPending}
        cases={testCases}
        loading={testCasesQuery.isLoading}
        module={selectedModule}
        onAdd={(question, answer) => addTestCaseMutation.mutate({ question, answer })}
        onClose={() => setQaOpen(false)}
        onRun={() => testMutation.mutate()}
        open={qaOpen}
        testResult={testMutation.data ?? null}
        testing={testMutation.isPending}
      />
      <Modal
        footer={
          <>
            <button className="rounded px-4 py-2 text-body-md font-bold text-on-surface-variant hover:bg-surface-variant" onClick={() => setArchiveConfirm(false)} type="button">
              Hủy
            </button>
            <button
              className="rounded bg-error px-4 py-2 text-body-md font-bold text-white hover:bg-red-700 disabled:opacity-50"
              disabled={archiveMutation.isPending}
              onClick={() => selectedModule && archiveMutation.mutate(selectedModule.id)}
              type="button"
            >
              {archiveMutation.isPending ? "Đang lưu trữ" : "Lưu trữ module"}
            </button>
          </>
        }
        onClose={() => setArchiveConfirm(false)}
        open={archiveConfirm}
        title="Lưu trữ module"
      >
        <Alert tone="warning">
          Module <strong>{selectedModule?.name}</strong> sẽ bị ẩn khỏi danh sách. Các version hiện tại vẫn được giữ trong cơ sở dữ liệu.
        </Alert>
      </Modal>
      <DiffDrawer diff={diff} onClose={() => setDiff(null)} />
    </AppShell>
  );
}
