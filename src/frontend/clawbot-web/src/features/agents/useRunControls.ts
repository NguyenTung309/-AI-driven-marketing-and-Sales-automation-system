import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  approveOrchestrationV2Run,
  controlOrchestrationV2Run,
  getOrchestrationV2Run,
  type OrchestrationV2ControlAction,
} from "@/shared/api/orchestrationV2";

/**
 * Điều khiển phiên điều phối (phê duyệt / tạm dừng / tiếp tục / hủy) dùng chung cho
 * OrchestrationPanel, AgentRunDetailPage và modal Phiên gần đây.
 *
 * Control luôn cần etag; khi caller không có sẵn (vd. run summary trong danh sách),
 * hook tự fetch detail để lấy etag mới — đây là fix cho lỗi 409 cố hữu của nút "Hủy".
 */
export function useRunControls(defaultSessionId: string | null) {
  const queryClient = useQueryClient();

  const invalidate = async (sessionId: string | null) => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["orchestration", "runs"] }),
      sessionId ? queryClient.invalidateQueries({ queryKey: ["orchestration", "session", sessionId] }) : Promise.resolve(),
    ]);
  };

  const approve = useMutation({
    mutationFn: (vars: { sessionId?: string; etag: string }) =>
      approveOrchestrationV2Run(vars.sessionId ?? defaultSessionId ?? "", vars.etag),
    onSuccess: (_data, vars) => invalidate(vars.sessionId ?? defaultSessionId),
  });

  const control = useMutation({
    mutationFn: async (vars: { action: OrchestrationV2ControlAction; sessionId?: string; etag?: string | null }) => {
      const sessionId = vars.sessionId ?? defaultSessionId;
      if (!sessionId) throw new Error("Thiếu mã phiên.");
      const etag = vars.action === "cancel" ? null : (vars.etag ?? (await getOrchestrationV2Run(sessionId)).etag);
      try {
        return await controlOrchestrationV2Run(sessionId, vars.action, etag);
      } catch (err: any) {
        if (err?.response?.status === 409) {
          const fresh = await getOrchestrationV2Run(sessionId);
          return await controlOrchestrationV2Run(sessionId, vars.action, fresh.etag);
        }
        throw err;
      }
    },
    onSuccess: (_data, vars) => invalidate(vars.sessionId ?? defaultSessionId),
  });

  return {
    approve,
    control,
    busy: approve.isPending || control.isPending,
    error: approve.error ?? control.error,
  };
}
