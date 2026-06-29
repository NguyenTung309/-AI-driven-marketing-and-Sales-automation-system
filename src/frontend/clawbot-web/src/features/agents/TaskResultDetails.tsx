import { splitToolResults, operationalPhaseLabel } from "@/shared/utils/userText";
import type { OrchestrationTaskDto, OrchestrationTraceDto } from "@/shared/api/orchestration";

// Renders the per-agent step detail: structured tool results (content_id, post_url…), the tool actions that
// ran, the human-readable output, and the static input. Shared by the dashboard panel and the run-detail page.
export function TaskResultDetails({
  task,
  toolTraces,
}: {
  readonly task: OrchestrationTaskDto;
  readonly toolTraces: readonly OrchestrationTraceDto[];
}) {
  const { text, toolResults } = splitToolResults(task.output);
  const hasInput = Object.keys(task.input).length > 0;
  if (!toolResults && !text && !toolTraces.length && !hasInput && task.dependsOn.length === 0) return null;

  return (
    <div className="mt-2 flex flex-col gap-2">
      {task.dependsOn.length > 0 && (
        <p className="text-label-sm text-on-surface-variant">phụ thuộc: {task.dependsOn.join(", ")}</p>
      )}

      {toolResults && Object.keys(toolResults).length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {Object.entries(toolResults).map(([key, value]) => (
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

      {text && (
        <details className="rounded border border-outline bg-surface-container-low p-2">
          <summary className="cursor-pointer text-label-sm text-on-surface">Kết quả ({task.agent})</summary>
          <pre className="mt-2 max-h-72 overflow-auto whitespace-pre-wrap break-words text-body-sm text-on-surface">{text}</pre>
        </details>
      )}

      {hasInput && (
        <details className="rounded border border-outline bg-surface-container-low p-2">
          <summary className="cursor-pointer text-label-sm text-on-surface-variant">Đầu vào</summary>
          <pre className="mt-2 max-h-48 overflow-auto whitespace-pre-wrap break-words font-mono text-mono-status text-on-surface-variant">
            {JSON.stringify(task.input, null, 2)}
          </pre>
        </details>
      )}
    </div>
  );
}
