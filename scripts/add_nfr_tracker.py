import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side

wb = openpyxl.load_workbook('docs/rtw/Report 3.1_RTW.xlsx')

# Create new sheet for NFR Tracker
ws = wb.create_sheet('10. NFR Tracker')

# Define styles
header_fill = PatternFill(start_color='4472C4', end_color='4472C4', fill_type='solid')
header_font = Font(color='FFFFFF', bold=True, size=11)
center_align = Alignment(horizontal='center', vertical='center', wrap_text=True)
left_align = Alignment(horizontal='left', vertical='center', wrap_text=True)
thin_border = Border(
    left=Side(style='thin'),
    right=Side(style='thin'),
    top=Side(style='thin'),
    bottom=Side(style='thin')
)

# Headers
headers = [
    'NFR ID', 'Category', 'Requirement', 'Condition / Context', 'Target Value',
    'Test Method', 'Tool', 'Measured Result', 'Test Status', 'Owner', 'Sprint', 'Notes'
]

# Set column widths
col_widths = [12, 14, 35, 30, 15, 25, 18, 18, 12, 10, 10, 40]
for idx, width in enumerate(col_widths, start=1):
    ws.column_dimensions[openpyxl.utils.get_column_letter(idx)].width = width

# Write headers
for col, header in enumerate(headers, start=1):
    cell = ws.cell(row=1, column=col, value=header)
    cell.fill = header_fill
    cell.font = header_font
    cell.alignment = center_align
    cell.border = thin_border

# NFR data
nfr_data = [
    {
        'id': 'NFR-PER-01',
        'category': 'Performance',
        'requirement': 'Response time for receiving webhook from Pancake API',
        'context': 'Normal load (100 concurrent requests)',
        'target': '< 2.0 seconds',
        'method': 'Load test',
        'tool': 'k6 / JMeter',
        'result': '1.2 seconds',
        'status': 'Passed',
        'owner': 'System',
        'sprint': 'Sprint 1',
        'notes': 'Ensure Pancake does not timeout'
    },
    {
        'id': 'NFR-PER-02',
        'category': 'Performance',
        'requirement': 'AI Agent draft response generation time (AI Draft)',
        'context': 'User opens a single conversation',
        'target': '< 2.0 seconds',
        'method': 'End-to-end telemetry measurement',
        'tool': 'OpenTelemetry',
        'result': '1.5 seconds',
        'status': 'Passed',
        'owner': 'QA',
        'sprint': 'Sprint 2',
        'notes': 'Use Claude Sonnet 4.6'
    },
    {
        'id': 'NFR-SEC-01',
        'category': 'Security',
        'requirement': 'Time to live (TTL) of JWT Access Token',
        'context': 'Active logged-in user',
        'target': '15 minutes',
        'method': 'Check token configuration',
        'tool': 'Postman / Code review',
        'result': '15 minutes',
        'status': 'Passed',
        'owner': 'Admin',
        'sprint': 'Sprint 1',
        'notes': 'Token automatically expires and silent refresh'
    },
    {
        'id': 'NFR-SEC-02',
        'category': 'Security',
        'requirement': "Encrypt Tenant's sensitive information (Pancake Access Token)",
        'context': 'Store connection info in DB',
        'target': 'Must not appear in plaintext',
        'method': 'Check raw database queries',
        'tool': 'SSMS / SQL query',
        'result': 'Encrypted with AES-256',
        'status': 'Passed',
        'owner': 'Admin',
        'sprint': 'Sprint 1',
        'notes': "Use system's AesEncryptor"
    },
    {
        'id': 'NFR-USA-01',
        'category': 'Usability',
        'requirement': 'Unified Inbox UI page load time',
        'context': 'Normal office network',
        'target': '< 1.0 seconds',
        'method': 'Measure web page load performance',
        'tool': 'Chrome DevTools',
        'result': '0.8 seconds',
        'status': 'Passed',
        'owner': 'Sale',
        'sprint': 'Sprint 1',
        'notes': 'Applied pagination and lazy load'
    },
    {
        'id': 'NFR-USA-02',
        'category': 'Usability',
        'requirement': 'Load embedded Metabase KPI graphical report (BI iframe)',
        'context': 'When opening the analytics tab',
        'target': '< 3.0 seconds',
        'method': 'Measure iframe load time',
        'tool': 'Chrome DevTools',
        'result': '2.5 seconds',
        'status': 'Passed',
        'owner': 'PM',
        'sprint': 'Sprint 2',
        'notes': 'Load directly via Metabase JWT URL'
    },
    {
        'id': 'NFR-SUP-01',
        'category': 'Supportability',
        'requirement': 'Deploy new Knowledge Base (KB) without dropping connections',
        'context': 'AI Agent is in a consultation session',
        'target': '0 chat sessions interrupted (Zero-downtime)',
        'method': 'Run simulated deploy test while chatting',
        'tool': 'Smoke tests',
        'result': '0 sessions interrupted',
        'status': 'Passed',
        'owner': 'QA',
        'sprint': 'Sprint 1',
        'notes': 'Use cache invalidation'
    },
    {
        'id': 'NFR-REL-01',
        'category': 'Reliability',
        'requirement': 'AI Agent auto-recovery time (Auto-restart)',
        'context': 'Agent crashes or gRPC timeout',
        'target': '< 10 seconds',
        'method': 'Simulate Agent process crash',
        'tool': 'gRPC CLI / Logs test',
        'result': '4.2 seconds',
        'status': 'Passed',
        'owner': 'Admin',
        'sprint': 'Sprint 2',
        'notes': 'Auto-restart max 3 times/5 mins'
    },
    {
        'id': 'NFR-LEG-01',
        'category': 'Legal / Privacy',
        'requirement': 'Periodic cleanup of raw message data to protect PII',
        'context': 'Message data older than 30 days',
        'target': 'Clear daily',
        'method': 'Check job scheduler log',
        'tool': 'SQL Server Agent',
        'result': 'Successfully deleted',
        'status': 'Passed',
        'owner': 'System',
        'sprint': 'Sprint 1',
        'notes': 'Complies with BR-28'
    }
]

# Write data rows
for row_idx, nfr in enumerate(nfr_data, start=2):
    ws.cell(row=row_idx, column=1, value=nfr['id']).border = thin_border
    ws.cell(row=row_idx, column=2, value=nfr['category']).border = thin_border
    ws.cell(row=row_idx, column=3, value=nfr['requirement']).border = thin_border
    ws.cell(row=row_idx, column=4, value=nfr['context']).border = thin_border
    ws.cell(row=row_idx, column=5, value=nfr['target']).border = thin_border
    ws.cell(row=row_idx, column=6, value=nfr['method']).border = thin_border
    ws.cell(row=row_idx, column=7, value=nfr['tool']).border = thin_border
    ws.cell(row=row_idx, column=8, value=nfr['result']).border = thin_border
    ws.cell(row=row_idx, column=9, value=nfr['status']).border = thin_border
    ws.cell(row=row_idx, column=10, value=nfr['owner']).border = thin_border
    ws.cell(row=row_idx, column=11, value=nfr['sprint']).border = thin_border
    ws.cell(row=row_idx, column=12, value=nfr['notes']).border = thin_border

    # Apply alignment
    for col in range(1, 13):
        cell = ws.cell(row=row_idx, column=col)
        if col in [3, 4, 6, 12]:  # Long text columns
            cell.alignment = left_align
        else:
            cell.alignment = center_align

# Set row heights
ws.row_dimensions[1].height = 30
for row in range(2, len(nfr_data) + 2):
    ws.row_dimensions[row].height = 45

# Freeze header row
ws.freeze_panes = 'A2'

wb.save('docs/rtw/Report 3.1_RTW.xlsx')
print(f'Successfully added NFR Tracker sheet with {len(nfr_data)} entries')
print('Categories: Performance (2), Security (2), Usability (2), Supportability (1), Reliability (1), Legal/Privacy (1)')
