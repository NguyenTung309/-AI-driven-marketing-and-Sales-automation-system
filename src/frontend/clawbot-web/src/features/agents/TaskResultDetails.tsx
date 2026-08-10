import { Link } from "react-router-dom";
import { operationalPhaseLabel, splitToolResults, toHumanTaskSummary } from "@/shared/utils/userText";
import { StructuredData } from "@/shared/ui/StructuredData";
import type { OrchestrationV2TaskDto, OrchestrationV2Trace } from "@/shared/api/orchestrationV2";

// Chỉ nhận đúng đường dẫn báo cáo nội bộ: reportUrl đi qua output của LLM nên không được tin, một
// giá trị lạ (http://…, javascript:…) mà đem render thành link là mở đường cho redirect ra ngoài.
const REPORT_LINK_PATTERN = /^\/reports\/[0-9a-f-]{36}$/i;

// Link đã hiện thành nút riêng thì thôi lặp lại ở dãy pill.
const LINK_KEYS = new Set(["reportUrl", "reportId"]);

function reportLinkOf(toolResults: Readonly<Record<string, string>> | null): string | null {
  const url = toolResults?.reportUrl;
  return url && REPORT_LINK_PATTERN.test(url) ? url : null;
}

// Renders the per-agent step detail: structured tool results (content_id, post_url…), the tool actions that
// ran, the human-readable output, and the static input. Shared by the dashboard panel and the run-detail page.
export function TaskResultDetails({
  task,
  toolTraces,
  editedByUser = false,
}: {
  readonly task: OrchestrationV2TaskDto;
  readonly toolTraces: readonly OrchestrationV2Trace[];
  /** Kết quả này do người dùng sửa tay (trace task_edited), không phải agent sinh ra. */
  readonly editedByUser?: boolean;
}) {
  const { text, toolResults } = splitToolResults(task.output);
  const humanSummary = toHumanTaskSummary(task.output);
  const reportLink = reportLinkOf(toolResults);
  const hasInput = Object.keys(task.input).length > 0;
  if (!toolResults && !text && !toolTraces.length && !hasInput && task.dependsOn.length === 0) return null;

  return (
    <div className="mt-2 flex flex-col gap-2">
      {task.dependsOn.length > 0 && (
        <p className="text-label-sm text-on-surface-variant">phụ thuộc: {task.dependsOn.join(", ")}</p>
      )}

      {editedByUser && (
        <span className="inline-flex w-fit items-center gap-1 rounded-full border border-warning/40 bg-warning/10 px-2 py-0.5 text-label-sm text-warning">
          <span aria-hidden="true" className="material-symbols-outlined text-[16px]">edit_note</span>
          Kết quả đã chỉnh sửa thủ công
        </span>
      )}

      {reportLink && (
        <Link
          to={reportLink}
          className="inline-flex w-fit items-center gap-1.5 rounded border border-primary/40 bg-primary/5 px-3 py-1.5 text-label-sm text-primary hover:bg-primary/10"
        >
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">
            table_chart
          </span>
          Xem báo cáo đầy đủ (bảng, biểu đồ, tải Excel/PDF)
        </Link>
      )}

      {toolResults && Object.keys(toolResults).length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {Object.entries(toolResults).filter(([key]) => !LINK_KEYS.has(key)).map(([key, value]) => (
            <span
              key={key}
              className="inline-flex max-w-full items-center gap-1 rounded-full border border-success/40 bg-success/10 px-2 py-0.5 text-label-sm"
              title={`${key}: ${value}`}
            >
              <span className="font-mono text-on-surface-variant">{key}</span>
              <span className="truncate font-mono text-success">{value}</span>
            </span>
          ))}
        </div>
      )}

      {toolTraces.length > 0 && (
        <details className="rounded border border-outline bg-surface-container-low p-2">
          <summary className="cursor-pointer text-label-sm text-on-surface">Công cụ đã chạy ({toolTraces.length})</summary>
          <ul className="mt-2 flex flex-col gap-1">
            {toolTraces.map((trace, index) => (
              <li key={`${trace.taskId}-${index}`} className="font-mono text-mono-status text-on-surface-variant">
                <span className={trace.phase.toLowerCase().includes("fail") || trace.phase.toLowerCase().includes("error") ? "text-error" : trace.phase.toLowerCase().includes("block") ? "text-warning" : "text-success"}>
                  [{operationalPhaseLabel(trace.phase)}]
                </span>{" "}
                <span className="break-words">{trace.message}</span>
              </li>
            ))}
          </ul>
        </details>
      )}

      {(text || toolResults) && (
        <section className="rounded border border-primary/30 bg-primary/5 p-3">
          <p className="text-label-caps uppercase text-primary">Kết quả cho người đọc</p>
          <p className="mt-1 max-h-48 overflow-auto whitespace-pre-wrap break-words text-body-sm text-on-surface">{humanSummary}</p>
        </section>
      )}

      {text && (
        <details className="rounded border border-outline bg-surface-container-low p-2">
          <summary className="cursor-pointer text-label-sm text-on-surface-variant">Dữ liệu bàn giao cho agent kế tiếp</summary>
          <div className="mt-2">
            <StructuredData maxHeightClass="max-h-72" value={text} />
          </div>
        </details>
      )}

      {hasInput && (
        <details className="rounded border border-outline bg-surface-container-low p-2">
          <summary className="cursor-pointer text-label-sm text-on-surface-variant">Đầu vào</summary>
          <div className="mt-2">
            <StructuredData maxHeightClass="max-h-48" value={task.input} />
          </div>
        </details>
      )}
    </div>
  );
}
