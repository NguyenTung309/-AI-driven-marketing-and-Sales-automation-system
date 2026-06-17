import type { AuditLog } from "../admin.types";
import { formatDateTime } from "../admin.types";
import { Card } from "@/shared/ui";
import { EmptyState } from "./EmptyState";

interface AuditPanelProps {
  readonly auditLogs: readonly AuditLog[];
}

export function AuditPanel({ auditLogs }: AuditPanelProps) {
  return (
    <section className="space-y-gutter">
      <Card className="p-0">
        <div className="border-b border-outline p-card-padding">
          <h2 className="text-headline-sm text-secondary">Nhật ký quản trị</h2>

          <p className="mt-1 text-body-md text-on-surface-variant">
            50 sự kiện gần nhất từ <code>/api/admin/audit-logs</code>.
          </p>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-[900px] w-full border-collapse text-left">
            <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
              <tr>
                <th className="px-4 py-3 font-bold">Thời gian</th>
                <th className="px-4 py-3 font-bold">Action</th>
                <th className="px-4 py-3 font-bold">Resource</th>
                <th className="px-4 py-3 font-bold">IP</th>
                <th className="px-4 py-3 font-bold">Diff</th>
              </tr>
            </thead>

            <tbody className="divide-y divide-outline bg-white">
              {auditLogs.map((log) => (
                <tr key={log.id} className="hover:bg-surface-container-low">
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">
                    {formatDateTime(log.occurredAt)}
                  </td>

                  <td className="px-4 py-4 font-semibold text-secondary">
                    {log.action}
                  </td>

                  <td className="px-4 py-4 text-body-md text-secondary">
                    {log.resourceType}

                    {log.resourceId ? (
                      <span className="ml-2 font-mono text-mono-status text-on-surface-variant">
                        {log.resourceId.slice(0, 8)}
                      </span>
                    ) : null}
                  </td>

                  <td className="px-4 py-4 text-body-md text-on-surface-variant">
                    {log.ipAddress ?? "-"}
                  </td>

                  <td className="max-w-[320px] truncate px-4 py-4 font-mono text-mono-status text-on-surface-variant">
                    {log.diffJson ?? "-"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {!auditLogs.length ? (
          <div className="p-card-padding">
            <EmptyState>Chưa có audit log.</EmptyState>
          </div>
        ) : null}
      </Card>
    </section>
  );
}