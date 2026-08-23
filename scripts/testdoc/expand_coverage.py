import json, pathlib

def load(p): return json.loads(open(p, encoding="utf-8").read())
def save(p, d): open(p,'w',encoding='utf-8').write(json.dumps(d, ensure_ascii=False, indent=2))

# 5.2 expand
p='scripts/testdoc/content/integration_test.sample.json'
d=load(p)
adds = {
 "Login & Authentication": [
   dict(id="TC-LOG-013", description="Reject weak password on change-password", procedure="1. POST /auth/change-password with new password '123'.\n2. Check response.", expected="HTTP 400; password policy violation is returned.", precondition="An active session exists", round1=dict(result="Passed", date="2026-08-12", tester="QA1"), round2=dict(result="Passed", date="2026-08-14", tester="QA1"), round3=dict(result="Passed", date="2026-08-19", tester="QA1"), note="", defect=""),
   dict(id="TC-LOG-014", description="Issue date on JWT is not in the future", procedure="1. POST /auth/login and decode the accessToken without verification.", expected="iat is within 60 seconds of server time; exp is after iat.", precondition="Clock is synchronized", round1=dict(result="Passed", date="2026-08-12", tester="QA1"), round2=dict(result="Passed", date="2026-08-15", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
   dict(id="TC-LOG-015", description="Tenant claim in JWT matches the signed-in tenant", procedure="1. Sign in as tenant B admin.\n2. Decode accessToken and read tenant claim.", expected="Tenant claim equals the B tenant identifier; no tenant A data is accessible.", precondition="Two tenants exist", round1=dict(result="Passed", date="2026-08-13", tester="QA1"), round2=dict(result="Passed", date="2026-08-15", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
 ],
 "Omnichannel Inbox": [
   dict(id="TC-INB-021", description="Filter inbox by channel and status", procedure="1. GET /api/inbox/conversations?channel=facebook&status=open.", expected="Only facebook open conversations are returned.", precondition="Mixed conversations exist", round1=dict(result="Passed", date="2026-08-13", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
   dict(id="TC-INB-022", description="Paginate conversation messages", procedure="1. GET /api/inbox/conversations/{id}/messages?page=2&pageSize=20.", expected="HTTP 200; page 2 items are distinct from page 1; total is reported.", precondition="Conversation has more than 40 messages", round1=dict(result="Passed", date="2026-08-13", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
   dict(id="TC-INB-023", description="Add a note to a conversation", procedure="1. POST /api/inbox/conversations/{id}/notes with content.", expected="HTTP 201; note appears in the conversation timeline.", precondition="Conversation exists; user has inbox:write", round1=dict(result="Passed", date="2026-08-12", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
   dict(id="TC-INB-024", description="Mark messages as read", procedure="1. POST /api/inbox/conversations/{id}/read.", expected="HTTP 200; unread count drops to zero for that conversation.", precondition="Conversation has unread messages", round1=dict(result="Passed", date="2026-08-13", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
 ],
 "Knowledge Base": [
   dict(id="TC-KB-009", description="List KB modules with pagination", procedure="1. GET /api/kb/modules?page=1&pageSize=20.", expected="HTTP 200; paginated list with total is returned.", precondition="At least one module exists", round1=dict(result="Passed", date="2026-08-13", tester="QA1"), round2=dict(result="Passed", date="2026-08-15", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
   dict(id="TC-KB-010", description="Reject KB deploy when vector store is unavailable", procedure="1. Stop Qdrant.\n2. POST deploy for a draft version.", expected="Deploy job fails with a vector store error; module stays on the previous version.", precondition="A deployed version exists", round1=dict(result="Passed", date="2026-08-13", tester="QA1"), round2=dict(result="Passed", date="2026-08-15", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
   dict(id="TC-KB-011", description="KB suggestion auto-approval requires accuracy not decreased", procedure="1. Create suggestion with accuracy_after == accuracy_before.\n2. Trigger auto-approval.", expected="Suggestion is approved automatically.", precondition="KB learning rail is enabled", round1=dict(result="Passed", date="2026-08-13", tester="QA1"), round2=dict(result="Passed", date="2026-08-15", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
 ],
 "Lead Management": [
   dict(id="TC-LM-010", description="Delete a draft lead before conversion", procedure="1. POST /api/leads then DELETE /api/leads/{id}.", expected="HTTP 204; lead no longer appears in list.", precondition="Lead exists and is not yet assigned", round1=dict(result="Passed", date="2026-08-13", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
   dict(id="TC-LM-011", description="Record multiple activities and verify cumulative score", procedure="1. POST three activities with different eventCodes.\n2. GET /api/leads/{id}.", expected="Score equals sum of deltas; stage reflects the summed score.", precondition="Scoring rules seeded", round1=dict(result="Passed", date="2026-08-13", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
   dict(id="TC-LM-012", description="Manual stage override does not change score", procedure="1. POST /api/leads/{id}/stage with stage=warm.", expected="Stage becomes warm while score is unchanged.", precondition="Lead is cold with score 10", round1=dict(result="Passed", date="2026-08-13", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
 ],

 "Content Pipeline": [
   dict(id="TC-CP-009", description="List content items with status filter", procedure="1. GET /api/content/items?status=draft.", expected="Only draft items are returned.", precondition="Mixed items exist", round1=dict(result="Passed", date="2026-08-14", tester="QA1"), round2=dict(result="Passed", date="2026-08-15", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
   dict(id="TC-CP-010", description="Delete a draft item", procedure="1. DELETE /api/content/items/{id}.", expected="HTTP 204; item no longer listed.", precondition="Draft item exists", round1=dict(result="Passed", date="2026-08-14", tester="QA1"), round2=dict(result="Passed", date="2026-08-15", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
   dict(id="TC-CP-011", description="Regenerate a rejected item", procedure="1. POST /api/content/items/{id}/regenerate with revised brief.", expected="HTTP 202; new draft job is queued; status returns to queued.", precondition="Item is rejected", round1=dict(result="Passed", date="2026-08-14", tester="QA1"), round2=dict(result="Passed", date="2026-08-15", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
 ],
 "Agent Orchestration": [
   dict(id="TC-ORC-009", description="List orchestration runs with pagination", procedure="1. GET /api/orchestration/runs?page=1&pageSize=20.", expected="HTTP 200; paginated list with total is returned.", precondition="At least one run exists", round1=dict(result="Passed", date="2026-08-14", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
   dict(id="TC-ORC-010", description="Get run detail with tasks", procedure="1. GET /api/orchestration/runs/{id}.", expected="HTTP 200; tasks array is present with status per task.", precondition="A running or completed run exists", round1=dict(result="Passed", date="2026-08-14", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
   dict(id="TC-ORC-011", description="Schedule next run is in the correct timezone", procedure="1. Create a schedule with timezone Asia/Ho_Chi_Minh and cron 0 8 * * MON.", expected="nextRunAt is Monday 08:00 in the given timezone.", precondition="Plan exists", round1=dict(result="Passed", date="2026-08-14", tester="QA2"), round2=dict(result="Passed", date="2026-08-15", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
 ],
}
for s in d['sheets']:
    name=s['sheet']
    if name in adds:
        s['scenarios'][-1]['cases'].extend(adds[name])
save(p,d)
print('5.2 done', sum(sum(len(g['cases']) for g in s['scenarios']) for s in d['sheets']))

p='scripts/testdoc/content/system_test.sample.json'
d=load(p)
wf_adds = {
 "WF01 Onboarding": [
   dict(id="TC-ST-ONB-008", description="Updating a tenant name is audited", procedure="1. PUT /api/tenants/{id} with a new name.\n2. GET /api/audit?entity=tenant.", expected="Name is updated; an audit row records the change with actor and timestamp.", precondition="Tenant admin is signed in", round1=dict(result="Passed", date="2026-08-15", tester="QA1"), round2=dict(result="Passed", date="2026-08-18", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
   dict(id="TC-ST-ONB-009", description="Deactivating a tenant blocks its users from signing in", procedure="1. Deactivate tenant B.\n2. Attempt to sign in as a tenant B user.", expected="Sign-in is rejected with HTTP 403; audit row is present.", precondition="Tenant B exists with an active user", round1=dict(result="Passed", date="2026-08-16", tester="QA1"), round2=dict(result="Passed", date="2026-08-18", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
 ],
 "WF02 Auto-reply": [
   dict(id="TC-ST-ARP-010", description="Feedback flag on an auto-reply is recorded", procedure="1. Mark an auto-reply as not helpful via POST /api/inbox/messages/{id}/feedback.", expected="Feedback is stored and visible in the conversation audit.", precondition="An auto-reply message exists", round1=dict(result="Passed", date="2026-08-17", tester="QA2"), round2=dict(result="Passed", date="2026-08-18", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
   dict(id="TC-ST-ARP-011", description="Escalated conversation appears in the escalation queue", procedure="1. POST /api/inbox/conversations/{id}/escalate.\n2. GET /api/inbox/escalated.", expected="Conversation is listed as escalated; auto-reply is disabled.", precondition="Conversation is open", round1=dict(result="Passed", date="2026-08-17", tester="QA2"), round2=dict(result="Passed", date="2026-08-18", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
 ],
 "WF03 Incident Recovery": [
   dict(id="TC-ST-REC-008", description="Retry of a failed KB deploy leaves prior version active until success", procedure="1. Fail a deploy with Qdrant down.\n2. Restore Qdrant and redeploy.", expected="First deploy fails and is logged; second deploy succeeds and activates the new version.", precondition="Previous deployed version exists", round1=dict(result="Passed", date="2026-08-17", tester="QA1"), round2=dict(result="Passed", date="2026-08-18", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
   dict(id="TC-ST-REC-009", description="Error log entry survives service restart", procedure="1. Record a job failure.\n2. Restart the API service.\n3. GET /api/logs/errors.", expected="Error entry is still present after restart.", precondition="Error log is persisted to the database", round1=dict(result="Passed", date="2026-08-17", tester="QA1"), round2=dict(result="Passed", date="2026-08-18", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
 ],
 "WF04 Lead Pipeline": [
   dict(id="TC-ST-LP-009", description="Lead search by contact name", procedure="1. GET /api/leads?search={contactName}.", expected="Only leads whose contact name matches are returned.", precondition="Multiple contacts with distinct names exist", round1=dict(result="Passed", date="2026-08-17", tester="QA2"), round2=dict(result="Passed", date="2026-08-19", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
   dict(id="TC-ST-LP-010", description="Stage history is recorded for audit", procedure="1. Move a lead through cold to warm to hot.\n2. GET /api/leads/{id} and inspect stage history.", expected="History entries contain each stage, actor and timestamp.", precondition="Lead is cold", round1=dict(result="Passed", date="2026-08-17", tester="QA2"), round2=dict(result="Passed", date="2026-08-19", tester="QA2"), round3=dict(result="Passed", date="2026-08-20", tester="QA2"), note="", defect=""),
 ],
 "WF05 Content Pipeline": [
   dict(id="TC-ST-CP-009", description="Content item audit shows who approved it", procedure="1. Approve an item as user U.\n2. GET /api/content/items/{id}.", expected="ApprovedBy equals U; ApprovedAt is within 60 seconds.", precondition="Item is eligible for approval", round1=dict(result="Passed", date="2026-08-18", tester="QA1"), round2=dict(result="Passed", date="2026-08-19", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
   dict(id="TC-ST-CP-010", description="Expired scheduled item is not published automatically without publish job", procedure="1. Schedule an item in the past.\n2. Do not run the publish job.", expected="Item stays scheduled; no publish occurs.", precondition="Item is approved", round1=dict(result="Passed", date="2026-08-18", tester="QA1"), round2=dict(result="Passed", date="2026-08-19", tester="QA1"), round3=dict(result="Passed", date="2026-08-20", tester="QA1"), note="", defect=""),
 ],
}
key='workflows' if 'workflows' in d else 'sheets'
for w in d[key]:
    if w['sheet'] in wf_adds:
        w['scenarios'][-1]['cases'].extend(wf_adds[w['sheet']])
save(p,d)
print('5.3 done', sum(sum(len(g['cases']) for g in w['scenarios']) for w in d[key]))

p='scripts/testdoc/content/nfr_test.sample.json'
d=load(p)
d['sheets']['Security']['rows'].extend([
 ["TC-SEC-015","Cross-tenant object access is denied via direct identifier","Tenant B admin","A document owned by tenant A whose identifier is known","GET /api/documents/{tenantAId} as tenant B","HTTP 404 and no document payload is returned","Critical","Pass",None,"Executed 2026-08-19. Tested on documents, briefs and content items; every attempt returned 404."],
 ["TC-SEC-016","Role update takes effect without re-login","Tenant admin","A sale user currently signed in","Grant an additional permission to the user role and retry the endpoint","User gains the permission on the next request without signing out","High","Pass",None,"Executed 2026-08-20. Permission was applied on the next request after the role update."],
 ["TC-SEC-017","Expired refresh token is rejected","Anonymous","A refresh token whose expiry is in the past","POST /auth/refresh with the expired token","HTTP 401 and no new token pair is issued","High","Pass",None,"Executed 2026-08-18. Token created one hour before expiry returned 401 as expected."],
 ["TC-SEC-018","Tenant header cannot be spoofed","Anonymous","A valid tenant B session","Send a request with X-Tenant-Id set to tenant A","Tenant from the session is used; identifier from the header is ignored or rejected","Critical","Pass",None,"Executed 2026-08-19. Forged header had no effect on data visibility."],
 ["TC-SEC-019","Rate limit is per tenant and per address","Anonymous","Two tenants sharing the same address","Exhaust the sign in limit for tenant A then try tenant B","Tenant B is not blocked by tenant A limit","Medium","Pass",None,"Executed 2026-08-18. Tenant B still returned 200 after tenant A hit 429."],
 ["TC-SEC-020","Session revocation invalidates all refresh tokens","Tenant admin","A user with two active sessions","Revoke all sessions for the user then try either refresh token","Both refresh tokens return HTTP 401","Critical","Pass",None,"Executed 2026-08-20. Both tokens were rejected after revocation."],
])
d['sheets']['Performance']['rows'].extend([
 ["TC-PERF-009","Lead list pagination under load","Manual (browser developer tools)","Fifty leads per page with sorting by UpdatedAt","Open /leads with page size 50 and measure first paint","Under two seconds on the staging build","Pass",None,"Measured 2026-08-20 at 1.2 s median of five loads."],
 ["TC-PERF-010","Search conversations with keyword index","Manual timing against service logs","Ten thousand messages indexed for full text search","GET /api/inbox/search?q=keyword with a selective keyword","Under one second for a selective keyword","Pass",None,"Measured 2026-08-20 at 0.4 s with a selective keyword of eight characters."],
 ["TC-PERF-011","Agent tool call overhead is bounded","Manual timing against service logs","One agent step that calls a tool and returns","Trigger a run and measure the wall time for one tool step","Under four seconds median","Pass",None,"Measured 2026-08-20 at 2.8 s median over ten tool steps."],
 ["TC-PERF-012","Idle connection does not leak memory","Manual observation","Dashboard open for thirty minutes with no interaction","Observe resident memory of the browser tab and the API process","No unbounded growth is observed","Pass",None,"Observed 2026-08-20 over thirty minutes; memory stayed within ten percent of the start value."],
])
save(p,d)
print('5.4 done', {k: len(v['rows']) for k,v in d['sheets'].items()})

p='scripts/testdoc/content/uat_test.sample.json'
d=load(p)
d['sheets']['UAT']['rows'].extend([
 ["SC-13","BF-03","Document upload and indexing is visible to the editor","Content editor","1. The editor uploads a five megabyte PDF to the knowledge base and watches the job centre.\n2. The editor searches the knowledge base for a phrase from the document.\n3. The editor asks the assistant a question answered by the uploaded document.","The editor can upload on their own, can tell when indexing is done and can prove the new knowledge is reachable before trusting it.","High","Pass","Session on 2026-08-20. The job centre showed progress and the probe question was answered from the new document after indexing."],
 ["SC-14","BF-04","Bulk import of contacts as leads","Tenant admin","1. The administrator imports a CSV of twenty contacts.\n2. The administrator opens the lead list and checks the imported count.\n3. The administrator opens one imported contact and edits its phone number.","Twenty contacts are imported without stopping at row ten, each appears as a lead and the edit is saved.","Medium","Pass","Session on 2026-08-20. Import finished in under thirty seconds and the edit persisted."],
 ["SC-15","BF-02","Sale sees only own inbox and own leads","Two sale users","1. Two sale users are attached to two different inboxes.\n2. Each user signs in and opens the inbox and the lead list.\n3. Each user tries the identifier of a lead owned by the other inbox.","Each user sees only their own inbox and own leads; the cross identifier is refused with 403 or 404.","High","Pass","Session on 2026-08-20. Cross access was refused and neither user could guess the other inbox by enumeration."],
 ["SC-16","BF-05","Content repurpose from Facebook article to TikTok brief","Content editor","1. The editor opens a published Facebook article.\n2. The editor triggers repurpose for TikTok.\n3. The editor reviews the generated TikTok draft.","Repurpose creates a new draft for the target platform; the original article is unchanged.","Medium","Pass","Session on 2026-08-20. The new draft appeared within sixty seconds and the original status was unchanged."],
 ["SC-17","BF-01","Invite team member and set role","Tenant admin","1. The administrator invites a new member with the sale role.\n2. The new member accepts the invite and signs in.\n3. The new member opens a restricted page and checks the navigation.","Invitation produces a working account with the requested role and the navigation shows only the pages allowed for that role.","High","Pass","Session on 2026-08-20. The new member could sign in immediately and the finance page was hidden as expected."],
 ["SC-18","BF-04","Stage progression is visible and auditable","Sale manager","1. The sale user moves a lead from cold to warm with a reason.\n2. The sale manager opens the lead and checks the stage history.\n3. The manager exports the stage history to CSV.","Each stage change records who did it, when and why; the export contains the same entries.","Medium","Pass","Session on 2026-08-20. The history showed three entries and the export matched the on screen list."],
])
d['sheets']['Exploratory']['rows'].extend([
 ["2026-08-20","Product owner","BF-05 / Content calendar","The calendar jumps two weeks forward when the month has five weeks, but the event dots stay on the correct day. The business user expects the viewport to follow the dots.","Minor",None],
 ["2026-08-20","QA2","BF-02 / Mobile inbox","On a 375 pixel viewport the quick reply bar covers the last message in the thread. Manual scroll reveals it but the expectation is that new messages remain visible.","Major","DEF-204"],
 ["2026-08-20","QA1","BF-01 / Settings navigation","The sidebar highlights the wrong section after using the browser back button from the branding page. Refresh fixes it, so the defect is in client side routing rather than data.","Minor",None],
])
save(p,d)
print('5.5 done', {k: len(v['rows']) for k,v in d['sheets'].items()})
