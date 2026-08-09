# Updated sitemap matching USER-FLOWS.md + confirmed removed screens
# Removed: agent-hub, /inbox/:channelId, /system/channels, /agents-office,
#   /chat-widget/*, /support/*, /logs, /tokens, /prompts

sitemap = """P-ID\tPage Name\tRoute\tComponent\tRoles\tLevel
P-00\tLogin\t/login\tLoginPage\tPublic\tDEEP
P-00a\tForgot Password\t/forgot-password\tForgotPasswordPage\tPublic\tDEEP
P-01\tDashboard\t/\tDashboardPage\tAll\tPRIMARY
P-01a\tOmnichannel Trends\t(zone C bar chart in P-01)\tChannelChart\tAll\tSUB
P-01b\tFunnel Overview\t(zone C panel in P-01)\tFunnelPanel\tAll\tSUB
P-01c\tLead 7-day Forecast\t(zone C chart in P-01)\tForecastChart\tAll\tSUB
P-01d\tAgent Status\t(zone C cards in P-01)\tAgentStatus\tAll\tSUB
P-02\tLeads\t/leads\tLeadsPage\tAll (read); Admin/SalesLead/Sale (write)\tPRIMARY
P-02a\tLead Pipeline View\t(table in P-02)\tLeadTable\tAll\tPRIMARY under section
P-02b\tLead Detail Drawer\t/leads/:leadId\tLeadDetailDrawer\tAll\tDEEP - from P-02a
P-02b1\tTimeline Tab\t(drawer tab in P-02b)\tTimeline section\tAll\tSUB
P-02b2\tContext Panel\t(drawer tab in P-02b)\tContext section\tAll\tSUB
P-02b3\tRevenue Tab\t(drawer tab in P-02b)\tRevenue section\tAll\tSUB
P-03\tConversations\t/conversations\tConversationsPage\tAll (read); Admin/SalesLead/Sale (write)\tPRIMARY
P-03a\tConversation List\t(left panel in P-03)\tConversationRow\tAll\tPRIMARY under section
P-03b\tChat Pane\t/conversations/:conversationId\tChatPane\tAll\tDEEP - from P-03a
P-03b1\tSale Assist Panel\t(side panel in P-03b)\tSaleAssistPanel\tAdmin/SalesLead/Sale\tSUB
P-03b2\tContact Memory Panel\t(side panel in P-03b)\tContactMemoryPanel\tAll\tSUB
P-03c\tConversation Deep Link\t/conversations/:conversationId\tConversationsPage (deep-link)\tAll\tDEEP - from notification
P-04\tContent Workspace\t/content\tContentWorkspacePage\tAdmin (full); Marketer (r+w+approve); All (read)\tPRIMARY
P-04a\tContent Queue\t(tab in P-04)\tQueue section\tAdmin/Marketer (write); All (read)\tPRIMARY under section
P-04b\tContent Calendar\t(tab in P-04)\tCalendar grid\tAdmin/Marketer (write); All (read)\tSUB
P-04c\tChain Metrics\t(tab in P-04)\tMetrics section\tAll\tSUB
P-04d\tPublishing Policy Control\t(toggle in P-04)\tContentPublishingPolicyControl\tAdmin\tSUB
P-05\tDocuments\t/documents\tDocumentsPage\tAdmin/SalesLead/Marketer/Sale (write); All (read)\tPRIMARY
P-05a\tTemplate Management\t(left panel in P-05)\tTemplate list cards\tAdmin/SalesLead/Marketer/Sale\tPRIMARY under section
P-05b\tTemplate Preview / Edit\t(right panel in P-05)\tTemplateFieldsEditor\tAdmin/SalesLead/Marketer/Sale\tDEEP - from P-05a
P-05c\tGenerate Document Kit\t(button in P-05)\tgenerateDocumentKit API\tAdmin/SalesLead/Marketer/Sale\tDEEP - from P-05a
P-05d\tGenerated Documents List\t(right panel in P-05)\tlistGeneratedDocuments\tAll\tPRIMARY under section
P-06\tAnalytics Reports\t/analytics\tAnalyticsReportsPage\tAll\tPRIMARY
P-06a\tOverview Tab\t(tab in P-06)\tAggregateMetrics + Channel breakdown\tAll\tPRIMARY under section
P-06b\tAgent Performance Tab\t(tab in P-06)\tAgentPerformance cards + cost table\tAll\tSUB
P-06c\tLead Analytics Tab\t(tab in P-06)\tForecastChart + Anomaly table\tAll\tSUB
P-06d\tAnomaly Detection\t(within P-06c)\tAnomaly detection section\tAll\tSUB
P-06e\tExport CSV/PDF\t(button in P-06a)\tdownloadAnalyticsExport\tAll\tDEEP - from P-06a
P-07\tKnowledge Base\t/kb\tKnowledgeBasePage\tAll (read); Admin/SalesLead/Marketer/QA (write)\tPRIMARY
P-07a\tModule List\t(left rail in P-07)\tModuleRail\tAll\tPRIMARY under section
P-07b\tModule Detail / Editor\t(center panel in P-07)\tEditorWorkspace (markdown)\tAdmin/SalesLead/Marketer/QA\tDEEP - from P-07a
P-07b1\tVersion List\t(right rail in P-07b)\tVersionRail\tAdmin/SalesLead/Marketer/QA\tSUB
P-07b2\tVersion Diff\t(drawer in P-07b1)\tDiffDrawer\tAdmin/SalesLead/Marketer/QA\tDEEP - from P-07b1
P-07b3\tDeploy / Rollback\t(buttons in P-07b1)\tdeployKbVersion / rollbackKbVersion\tAdmin/SalesLead/Marketer/QA\tDEEP - from P-07b1
P-07c\tTest Cases\t(within module in P-07b)\tTest case CRUD\tAdmin/SalesLead/Marketer/QA\tSUB - within module
P-07d\tAuto-classify Modal\t(dialog in P-07a)\tKbAutoClassifyModal\tAdmin/SalesLead/Marketer/QA\tDEEP - from P-07a
P-07e\tKB Suggestions Panel\t(tab in P-07)\tKbSuggestionsPanel\tAdmin/SalesLead/Marketer/QA\tSUB
P-07f\tAccuracy Dashboard\t(right panel in P-07)\tAccuracyPanel\tAll\tSUB
P-08\tNotifications\t/notifications\tNotificationsPage\tAll\tPRIMARY
P-08a\tAll Notifications Tab\t(tab in P-08)\tAll notifications list\tAll\tPRIMARY under section
P-08b\tUnread Tab\t(tab in P-08)\tUnread filter\tAll\tSUB
P-08c\tSystem Tab\t(tab in P-08)\tSystem notifications\tAll\tSUB
P-08d\tLead Tab\t(tab in P-08)\tLead notifications\tAll\tSUB
P-09\tAgents\t/agents\tAgentDashboardPage\tAdmin/SalesLead/Marketer\tPRIMARY
P-09a\tOrchestration\t(tab in P-09)\tOrchestrationPanel + TaskDagCanvas\tAdmin/SalesLead/Marketer\tPRIMARY under section
P-09a1\tCreate Run / Plan\t(dialog in P-09a)\tPlanSuggestionsDialog\tAdmin/SalesLead/Marketer\tDEEP - from P-09a
P-09a2\tApprove Run\t(button in P-09a1)\tapprove endpoint\tAdmin/SalesLead\tDEEP - from P-09a1
P-09b\tAgent Team\t(tab in P-09)\tAgentListItem grid\tAdmin/SalesLead/Marketer\tSUB
P-09b1\tAgent Config Drawer\t(slide-in in P-09b)\tAgentConfigDrawer (prompt/model/tools)\tAdmin\tDEEP - from P-09b
P-09b2\tCreate Sub-Agent\t(dialog in P-09b)\tCreateSubAgentDialog\tAdmin\tDEEP - from P-09b
P-09c\tLogs\t(tab in P-09)\tCost breakdown + trace logs\tAdmin/SalesLead/Marketer\tSUB
P-09d\tPublishing Policy Control\t(toggle in P-09)\tContentPublishingPolicyControl\tAdmin\tSUB - component, not a route
P-09e\tAgent Runs List\t/agents/runs\tAgentRunsPage\tAdmin/SalesLead/Marketer\tDEEP - from P-09a
P-09f\tAgent Run Detail\t/agents/runs/:sessionId\tAgentRunDetailPage\tAdmin/SalesLead/Marketer\tDEEP - from P-09e
P-09g\tSchedules Card\t(card in P-09a)\tSchedulesCard\tAdmin/SalesLead/Marketer\tDEEP - from P-09a
P-10\tLLM Providers\t/llm-providers\tLlmProvidersPage\tAdmin\tPRIMARY
P-10a\tLLM Config Table\t(top section in P-10)\tLLM config DataTable\tAdmin\tPRIMARY under section
P-10b\tEmbedding Config Table\t(bottom section in P-10)\tEmbedding config DataTable\tAdmin\tSUB
P-10c\tTest / Rotate Key\t(action buttons in P-10a)\ttestLlmConfig / rotateLlmKey\tAdmin\tDEEP - from P-10a
P-11\tSystem / Admin Console\t/system\tAdminConsolePage\tAdmin\tPRIMARY
P-11a\tUsers Tab\t(tab in P-11)\tAdminUsersTab\tAdmin\tPRIMARY under section
P-11a1\tCreate/Edit User\t(dialog in P-11a)\tAdminUserModal\tAdmin\tDEEP - from P-11a
P-11a2\tReset Password\t(dialog in P-11a)\tresetAdminUserPassword\tAdmin\tDEEP - from P-11a
P-11b\tRoles & Permissions\t(tab in P-11)\tAdminRolesTab\tAdmin\tSUB
P-11b1\tCreate/Edit Role\t(dialog in P-11b)\tAdminRoleModal\tAdmin\tDEEP - from P-11b
P-11b2\tPermission Matrix\t(panel within P-11b)\tPermission checkbox grid\tAdmin\tSUB - within P-11b
P-11c\tAPI Keys\t(tab in P-11)\tAdminKeysTab\tAdmin\tSUB
P-11c1\tCreate Key\t(dialog in P-11c)\tAdminKeyModal\tAdmin\tDEEP - from P-11c
P-11d\tIntegrations\t(tab in P-11)\tAdminIntegrationsTab\tAdmin\tSUB
P-11d1\tBranding\t(form section in P-11d)\tBranding form fields\tAdmin\tSUB - within P-11d
P-11d2\tPancake Config\t(form section in P-11d)\tPancake form + webhook URL\tAdmin\tSUB - within P-11d
P-11d3\tMeta Integration\t(section in P-11d)\tMeta OAuth flow\tAdmin\tSUB - within P-11d
P-11d4\tSocial Channels\t(section in P-11d)\tAdminSocialChannelsSection\tAdmin\tSUB - within P-11d
P-11e\tAutomated Jobs\t(tab in P-11)\tAdminJobsTab\tAdmin\tSUB
P-11f\tSystem Errors / Logs\t(tab in P-11)\tAdminSystemLogsTab\tAdmin\tSUB
P-11g\tAudit Log\t(tab in P-11)\tAdminAuditTab\tAdmin\tSUB
P-12\tTask Logs\t/logs\tTaskLogsPage\tAll\tPRIMARY
P-12a\tRun List\t(table in P-12)\tTask run InfiniteDataTable\tAll\tPRIMARY under section
P-12b\tRun Detail / Traces\t(expand row in P-12a)\tTaskRunTrace + TaskRunAudit\tAll\tDEEP - from P-12a
P-13\tProfile\t/profile\tProfilePage\tAll\tPRIMARY - via topbar dropdown
P-13a\tBasic Info Tab\t(tab in P-13)\tdisplayName / phone / dob / avatar\tAll\tPRIMARY under section
P-13b\tPermissions Tab\t(tab in P-13)\tPermission list (read-only)\tAll\tSUB
P-13c\tSecurity / Login History\t(tab in P-13)\tLoginHistoryItem DataTable\tAll\tSUB
P-13c1\tChange Password\t(dialog in P-13c)\tChangePasswordDialog\tAll\tDEEP - from P-13c
P-13c2\tTwo-Factor Setup\t(dialog in P-13c)\tTwoFactorSetupDialog\tAll\tDEEP - from P-13c
P-13d\tNotification Settings\t(card in P-13)\tNotificationSettingsCard\tAll\tSUB
"""

out = r'E:\DoAn\-AI-driven-marketing-and-Sales-automation-system\docs\screen-flows\SITEMAP-UPDATED.tsv'
import os
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, 'w', encoding='utf-8') as f:
    f.write(sitemap)
print("Written:", os.path.getsize(out), "bytes")
