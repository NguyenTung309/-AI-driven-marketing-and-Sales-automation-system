import { useQuery } from "@tanstack/react-query";
import { StatusPill } from "@/shared/ui/StatusPill";
import { listAgentTools, type AgentToolInfo } from "@/shared/api/agents";

// B8: một picker công cụ duy nhất, đọc catalog thật từ backend (/api/agents/tools — tên, mức rủi ro,
// quyền yêu cầu) thay cho các danh sách hardcode trôi dạt khi backend thêm tool mới.
export function ToolPicker({
  selected,
  onToggle,
}: {
  readonly selected: readonly string[];
  readonly onToggle: (name: string) => void;
}) {
  const toolsQuery = useQuery({ queryKey: ["agent-tools"], queryFn: listAgentTools, staleTime: 300_000 });

  if (toolsQuery.isLoading) {
    return <p className="text-label-sm text-on-surface-variant">Đang tải danh sách công cụ...</p>;
  }

  const tools = toolsQuery.data ?? [];
  if (!tools.length) {
    return <p className="text-label-sm text-on-surface-variant">Không tải được danh sách công cụ.</p>;
  }

  const renderTool = (tool: AgentToolInfo) => (
    <label className="flex items-start gap-2 rounded border border-outline bg-surface px-3 py-2 text-body-md" key={tool.name}>
      <input checked={selected.includes(tool.name)} className="mt-1" onChange={() => onToggle(tool.name)} type="checkbox" />
      <span className="min-w-0">
        <span className="flex flex-wrap items-center gap-2">
          <span className="truncate font-mono text-mono-status text-secondary">{tool.name}</span>
          {tool.risk.toLowerCase() === "high" ? <StatusPill tone="error">Rủi ro cao</StatusPill> : null}
        </span>
        <span className="block text-label-sm text-on-surface-variant">{tool.description}</span>
        {tool.permission ? (
          <span className="block text-label-sm text-on-surface-variant/70">Cần quyền: {tool.permission}</span>
        ) : null}
      </span>
    </label>
  );

  const lowRisk = tools.filter((tool) => tool.risk.toLowerCase() !== "high");
  const highRisk = tools.filter((tool) => tool.risk.toLowerCase() === "high");

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">{lowRisk.map(renderTool)}</div>
      {highRisk.length ? (
        <div>
          <p className="mb-1 text-label-sm font-bold text-error">Công cụ rủi ro cao (không thể hoàn tác / chạm khách hàng)</p>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">{highRisk.map(renderTool)}</div>
        </div>
      ) : null}
      <p className="text-label-sm text-on-surface-variant">
        Hệ thống kiểm tra quyền của bạn với từng công cụ khi lưu; không chọn gì = agent chỉ trả lời văn bản.
      </p>
    </div>
  );
}
