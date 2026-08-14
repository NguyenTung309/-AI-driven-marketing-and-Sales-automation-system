import type { StatusTone } from "@/shared/ui/StatusPill";
import type { OrchestrationV2Status, OrchestrationV2TaskDto } from "@/shared/api/orchestrationV2";

export function statusTone(status: OrchestrationV2Status): StatusTone {
  switch (status) {
    case "completed":
      return "success";
    case "failed":
    case "cancelled":
      return "error";
    case "running":
    case "pause_requested":
    case "paused":
    case "pending_approval":
    case "cancelling":
    case "failing":
      return "warning";
    default:
      return "neutral";
  }
}

export function statusLabel(status: OrchestrationV2Status): string {
  switch (status) {
    case "draft":
      return "Nháp";
    case "pending_approval":
      return "Chờ phê duyệt";
    case "running":
      return "Đang chạy";
    case "pause_requested":
      return "Đang dừng an toàn";
    case "paused":
      return "Tạm dừng";
    case "cancelling":
      return "Đang hủy sau khi hoàn tất phát hành";
    case "failing":
      return "Đang dừng sau khi hoàn tất phát hành";
    case "completed":
      return "Hoàn tất";
    case "failed":
      return "Thất bại";
    case "cancelled":
      return "Đã hủy";
    default:
      return status;
  }
}

export function taskStatusLabel(status: string): string {
  switch (status) {
    case "pending":
      return "Chờ chạy";
    case "running":
      return "Đang chạy";
    case "completed":
      return "Hoàn tất";
    case "failed":
      return "Thất bại";
    case "skipped":
      return "Bỏ qua";
    default:
      return status;
  }
}

export function a2aStatusLabel(status: string): string {
  switch (status) {
    case "pending":
      return "Chờ xử lý";
    case "processed":
      return "Đã xử lý";
    case "failed":
      return "Thất bại";
    default:
      return status;
  }
}

export function taskTone(status: string): StatusTone {
  switch (status) {
    case "completed":
      return "success";
    case "failed":
      return "error";
    case "skipped":
      return "neutral";
    default:
      return "warning";
  }
}

// Các bước phụ thuộc trực tiếp hoặc gián tiếp vào `rootId`. Sửa output của bước gốc mà không đặt lại nhóm
// này thì bản sửa không đi tới đâu — chúng đã chạy với kết quả cũ. Khớp ResetDownstream ở OrchestratorGrpcService.
export function transitiveDependents(
  tasks: readonly OrchestrationV2TaskDto[],
  rootId: string,
): readonly OrchestrationV2TaskDto[] {
  const affected = new Set<string>([rootId]);
  let changed = true;
  while (changed) {
    changed = false;
    for (const task of tasks) {
      if (affected.has(task.id)) continue;
      if (task.dependsOn.some((dep) => affected.has(dep))) {
        affected.add(task.id);
        changed = true;
      }
    }
  }
  affected.delete(rootId);
  return tasks.filter((task) => affected.has(task.id));
}

// SPEC-16 P3-2: order tasks by dependency depth (topological) so the DAG reads root→leaf, and expose each
// task's depth for indentation — a lightweight graph visualization without a layout engine.
export function tasksByDepth(
  tasks: readonly OrchestrationV2TaskDto[],
): readonly { task: OrchestrationV2TaskDto; depth: number }[] {
  const depthById = new Map<string, number>();
  const byId = new Map(tasks.map((t) => [t.id, t]));
  const resolve = (id: string): number => {
    const cached = depthById.get(id);
    if (cached !== undefined) return cached;
    const task = byId.get(id);
    if (!task || task.dependsOn.length === 0) {
      depthById.set(id, 0);
      return 0;
    }
    const d = 1 + Math.max(...task.dependsOn.map((dep) => resolve(dep)));
    depthById.set(id, d);
    return d;
  };
  return tasks
    .map((task) => ({ task, depth: resolve(task.id) }))
    .sort((a, b) => a.depth - b.depth || a.task.id.localeCompare(b.task.id));
}
