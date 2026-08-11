import { useEffect, useMemo, useRef, useState, type MouseEvent } from "react";
import { Modal } from "@/shared/ui/Modal";
import { StatusPill } from "@/shared/ui/StatusPill";
import { StructuredData } from "@/shared/ui/StructuredData";
import { taskStatusLabel, taskTone, tasksByDepth } from "./orchestrationStatus";
import type { OrchestrationV2TaskDto } from "@/shared/api/orchestrationV2";

const NODE_W = 240;
const NODE_H = 96;
const GAP_X = 72;
const GAP_Y = 20;
const PAD = 12;
const MINI_W = 200;
const MIN_ZOOM = 0.4;
const MAX_ZOOM = 1.5;

interface PositionedNode {
  readonly task: OrchestrationV2TaskDto;
  readonly x: number;
  readonly y: number;
}

interface DagEdge {
  readonly from: PositionedNode;
  readonly to: PositionedNode;
}

function edgeClass(edge: DagEdge): string {
  if (edge.to.task.status === "running") return "text-primary";
  if (edge.from.task.status === "completed") return "text-success";
  if (edge.to.task.status === "failed") return "text-error";
  return "text-on-surface-variant/50";
}

function miniNodeClass(status: string): string {
  if (status === "running") return "text-primary";
  if (status === "completed") return "text-success";
  if (status === "failed") return "text-error";
  return "text-on-surface-variant/40";
}

function edgePath(edge: DagEdge): string {
  const x1 = edge.from.x + NODE_W;
  const y1 = edge.from.y + NODE_H / 2;
  const x2 = edge.to.x;
  const y2 = edge.to.y + NODE_H / 2;
  const bend = GAP_X / 2;
  return `M ${x1} ${y1} C ${x1 + bend} ${y1}, ${x2 - bend} ${y2}, ${x2} ${y2}`;
}

function clampZoom(value: number): number {
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, value));
}

// Layered DAG: column = dependency depth (tasksByDepth), edges = dependsOn links.
// Deterministic coordinates (uniform node size) so the SVG needs no measuring pass.
// Extras: zoom controls, a minimap with live viewport (large plans), and click-an-edge
// to inspect the data handed from the upstream agent to the downstream one.
export function TaskDagCanvas({
  tasks,
  selectedTaskId,
  onSelect,
}: {
  readonly tasks: readonly OrchestrationV2TaskDto[];
  readonly selectedTaskId: string | null;
  readonly onSelect: (taskId: string) => void;
}) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [zoom, setZoom] = useState(1);
  const [viewport, setViewport] = useState({ left: 0, top: 0, w: 0, h: 0 });
  const [edgeInfo, setEdgeInfo] = useState<DagEdge | null>(null);

  const layout = useMemo(() => {
    const rowsPerDepth = new Map<number, number>();
    const nodes: PositionedNode[] = tasksByDepth(tasks).map(({ task, depth }) => {
      const row = rowsPerDepth.get(depth) ?? 0;
      rowsPerDepth.set(depth, row + 1);
      return { task, x: PAD + depth * (NODE_W + GAP_X), y: PAD + row * (NODE_H + GAP_Y) };
    });
    const byId = new Map(nodes.map((node) => [node.task.id, node]));
    const edges: DagEdge[] = nodes.flatMap((node) =>
      node.task.dependsOn
        .map((dep) => byId.get(dep))
        .filter((from): from is PositionedNode => Boolean(from))
        .map((from) => ({ from, to: node })),
    );
    const width = nodes.reduce((max, node) => Math.max(max, node.x + NODE_W), 0) + PAD;
    const height = nodes.reduce((max, node) => Math.max(max, node.y + NODE_H), 0) + PAD;
    return { nodes, edges, width, height };
  }, [tasks]);

  const showMinimap = layout.nodes.length >= 10 || layout.width > 1100;
  const miniScale = MINI_W / Math.max(1, layout.width);
  const miniH = Math.max(40, Math.round(layout.height * miniScale));

  function syncViewport() {
    const el = scrollRef.current;
    if (!el) return;
    setViewport({
      left: el.scrollLeft / zoom,
      top: el.scrollTop / zoom,
      w: el.clientWidth / zoom,
      h: el.clientHeight / zoom,
    });
  }

  useEffect(() => {
    syncViewport();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- re-sync when zoom or layout size changes
  }, [zoom, layout.width, layout.height]);

  function fitToWidth() {
    const el = scrollRef.current;
    if (!el) return;
    setZoom(clampZoom(el.clientWidth / Math.max(1, layout.width)));
  }

  function jumpTo(event: MouseEvent<HTMLDivElement>) {
    const el = scrollRef.current;
    if (!el) return;
    const rect = event.currentTarget.getBoundingClientRect();
    const targetX = (event.clientX - rect.left) / miniScale;
    const targetY = (event.clientY - rect.top) / miniScale;
    el.scrollTo({
      left: Math.max(0, targetX * zoom - el.clientWidth / 2),
      top: Math.max(0, targetY * zoom - el.clientHeight / 2),
      behavior: "smooth",
    });
  }

  if (!layout.nodes.length) return null;

  const zoomButton =
    "rounded border border-outline bg-surface-container-lowest px-2 py-1 text-label-sm text-secondary transition-colors hover:border-primary hover:text-primary disabled:opacity-50";

  return (
    <div className="relative">
      <div className="mb-2 flex items-center gap-2">
        <button className={zoomButton} disabled={zoom <= MIN_ZOOM} onClick={() => setZoom((z) => clampZoom(z - 0.15))} type="button">
          −
        </button>
        <span className="w-12 text-center font-mono text-mono-status text-on-surface-variant">{Math.round(zoom * 100)}%</span>
        <button className={zoomButton} disabled={zoom >= MAX_ZOOM} onClick={() => setZoom((z) => clampZoom(z + 0.15))} type="button">
          +
        </button>
        <button className={zoomButton} onClick={fitToWidth} type="button">
          Vừa khung
        </button>
        <button className={zoomButton} onClick={() => setZoom(1)} type="button">
          100%
        </button>
        <span className="ml-auto text-label-sm text-on-surface-variant">Bấm vào cạnh nối để xem dữ liệu chuyền giữa 2 agent.</span>
      </div>

      <div className="overflow-auto rounded-lg border border-outline bg-surface p-1" onScroll={syncViewport} ref={scrollRef}>
        <div style={{ width: layout.width * zoom, height: layout.height * zoom }}>
          <div className="relative origin-top-left" style={{ width: layout.width, height: layout.height, transform: `scale(${zoom})` }}>
            <svg aria-hidden="true" className="absolute inset-0" height={layout.height} width={layout.width}>
              <defs>
                <marker id="dag-arrow" markerHeight="7" markerWidth="7" orient="auto-start-reverse" refX="6" refY="3.5">
                  <path d="M 0 0 L 7 3.5 L 0 7 z" fill="currentColor" />
                </marker>
              </defs>
              {layout.edges.map((edge) => (
                <g className={edgeClass(edge)} key={`${edge.from.task.id}->${edge.to.task.id}`}>
                  <path
                    className={edge.to.task.status === "running" ? "animate-pulse" : undefined}
                    d={edgePath(edge)}
                    fill="none"
                    markerEnd="url(#dag-arrow)"
                    stroke="currentColor"
                    strokeDasharray={edge.to.task.status === "running" ? "6 4" : undefined}
                    strokeWidth={1.8}
                  />
                  {/* Invisible wide hit-area so the edge is clickable. */}
                  <path
                    className="cursor-pointer"
                    d={edgePath(edge)}
                    fill="none"
                    onClick={() => setEdgeInfo(edge)}
                    pointerEvents="stroke"
                    stroke="transparent"
                    strokeWidth={14}
                  >
                    <title>{`Xem dữ liệu ${edge.from.task.agent} → ${edge.to.task.agent}`}</title>
                  </path>
                </g>
              ))}
            </svg>
            {layout.nodes.map((node) => {
              const running = node.task.status === "running";
              const selected = node.task.id === selectedTaskId;
              // Bước bỏ qua vẫn thỏa phụ thuộc nhưng không bàn giao gì — vẽ mờ, viền đứt để không nhầm là đã chạy.
              const skipped = node.task.status === "skipped";
              return (
                <button
                  className={[
                    "absolute flex flex-col gap-1 rounded-lg bg-surface-container-lowest p-2 text-left shadow-sm transition-shadow hover:shadow-md",
                    skipped ? "border border-dashed border-outline opacity-60" : "border",
                    running ? "border-primary" : node.task.status === "failed" ? "border-error" : skipped ? "" : "border-outline",
                    selected ? "ring-2 ring-primary ring-offset-1 ring-offset-surface" : "",
                  ].join(" ")}
                  key={node.task.id}
                  onClick={() => onSelect(node.task.id)}
                  style={{ left: node.x, top: node.y, width: NODE_W, height: NODE_H }}
                  type="button"
                >
                  <span className="flex w-full items-center gap-2">
                    {running ? <span aria-hidden="true" className="size-2 shrink-0 animate-pulse rounded-full bg-primary" /> : null}
                    <span className="min-w-0 flex-1 truncate font-mono text-mono-status text-secondary">{node.task.agent}</span>
                    <StatusPill tone={taskTone(node.task.status)}>{taskStatusLabel(node.task.status)}</StatusPill>
                  </span>
                  <span className="line-clamp-2 w-full text-label-sm text-on-surface">{node.task.description}</span>
                  {node.task.useCount && node.task.useCount > 1 ? (
                    <span className="text-label-sm text-on-surface-variant">×{node.task.useCount} lượt dùng</span>
                  ) : null}
                </button>
              );
            })}
          </div>
        </div>
      </div>

      {showMinimap ? (
        <div
          className="absolute right-3 top-12 cursor-pointer rounded border border-outline bg-surface-container-lowest/95 p-1 shadow-md"
          onClick={jumpTo}
          role="presentation"
          style={{ width: MINI_W + 8 }}
          title="Bấm để nhảy tới vùng tương ứng"
        >
          <svg height={miniH} width={MINI_W}>
            {layout.nodes.map((node) => (
              <rect
                className={miniNodeClass(node.task.status)}
                fill="currentColor"
                height={Math.max(3, NODE_H * miniScale)}
                key={node.task.id}
                rx={1.5}
                width={Math.max(6, NODE_W * miniScale)}
                x={node.x * miniScale}
                y={node.y * miniScale}
              />
            ))}
            <rect
              className="text-primary"
              fill="none"
              height={Math.min(miniH, viewport.h * miniScale)}
              stroke="currentColor"
              strokeWidth={1.5}
              width={Math.min(MINI_W, viewport.w * miniScale)}
              x={Math.max(0, viewport.left * miniScale)}
              y={Math.max(0, viewport.top * miniScale)}
            />
          </svg>
        </div>
      ) : null}

      <Modal
        maxWidthClass="max-w-2xl"
        onClose={() => setEdgeInfo(null)}
        open={Boolean(edgeInfo)}
        title={edgeInfo ? `Dữ liệu chuyền: ${edgeInfo.from.task.agent} → ${edgeInfo.to.task.agent}` : "Dữ liệu chuyền"}
      >
        {edgeInfo ? (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center gap-2 text-label-sm text-on-surface-variant">
              <span className="font-mono text-secondary">{edgeInfo.from.task.id}</span>
              <span aria-hidden="true">→</span>
              <span className="font-mono text-secondary">{edgeInfo.to.task.id}</span>
              <StatusPill tone={taskTone(edgeInfo.from.task.status)}>Nguồn: {taskStatusLabel(edgeInfo.from.task.status)}</StatusPill>
              <StatusPill tone={taskTone(edgeInfo.to.task.status)}>Đích: {taskStatusLabel(edgeInfo.to.task.status)}</StatusPill>
            </div>
            <div>
              <p className="text-body-md font-bold text-secondary">Nhiệm vụ nguồn</p>
              <p className="text-body-md text-on-surface">{edgeInfo.from.task.description}</p>
            </div>
            <div>
              <p className="text-body-md font-bold text-secondary">Kết quả chuyền cho agent đích (upstream_results)</p>
              {edgeInfo.from.task.output ? (
                <div className="mt-1 rounded border border-outline bg-surface p-3">
                  <StructuredData maxHeightClass="max-h-[40vh]" value={edgeInfo.from.task.output} />
                </div>
              ) : (
                <p className="mt-1 text-body-md text-on-surface-variant">
                  Task nguồn chưa hoàn tất — chưa có dữ liệu chuyền. Khi nguồn xong, kết quả sẽ được tiêm vào đầu vào của agent đích.
                </p>
              )}
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
}
