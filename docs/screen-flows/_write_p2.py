import os

out = r'E:\DoAn\-AI-driven-marketing-and-Sales-automation-system\docs\screen-flows\USER-FLOWS.md'

part2 = """## Flow F-01 — System Login and 2FA

**Related SRS Scenario:** SC-05
**Primary Actor:** User (Sale Agent / Admin / QA Admin)
**Entry Point:** `/login` (P-01)
**Success End State:** JWT issued; user lands on DashboardPage (P-03)

### Flow Diagram — F-01

```
[Entry: LoginPage (P-01)]
         |
         v
[Step 1: Enter email + password, click DANG NHAP]
         |
         +--- [Branch: Account locked (HTTP 423)]
         |           |
         |           v
         |  [Error: "Tai khoan bi khoa. Lien he admin."]
         |           |
         |           +--> [End: Cannot proceed]
         |
         +--- [Branch: Invalid credentials (HTTP 401)]
         |           |
         |           v
         |  [Error: "Dang nhap that bai. Kiem tra lai."]
         |           |
         |           +--> [Back to Step 1; fields retained]
         |
         +--- [Branch: 2FA required (HTTP 202)]
         |           |
         |           v
         |  [Step 2: Enter 6-digit TOTP code]
         |           |
         |           +--- [Branch: Invalid code]
         |           |           |
         |           |           v
         |           |  [Error: "Ma xac thuc khong dung."]
         |           |           |
         |           |           +--> [Back to Step 2; code cleared]
         |           |
         |           v
         |  [Step 3: Click XAC NHAN -> Auth OK]
         |
         v
[Success: setAuth() -> loadPermissions() -> /]
         |
         v
[End: DashboardPage (P-03)]
```

### Narrative

1. User navigates to `/login` (P-01). Brand panel + credential form render. Focus on email input.
2. User enters email + password, clicks DANG NHAP.
3. **Happy path:** Server returns 200 with accessToken. setAuth() stores token; loadPermissions() fetches /auth/me; navigate to `/` (P-03).
4. **2FA branch:** Server returns 202. Form transitions to 6-digit OTP. User enters code, clicks XAC NHAN. Success = same as happy path.
5. **Error branches:** Invalid credentials show inline error. Account locked (423) shows different message. Both retain fields for retry.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Account locked (HTTP 423) | Lock message; no retry until admin unlocks |
| B-2 | Invalid credentials (HTTP 401) | Inline error; password cleared |
| B-3 | 2FA required (HTTP 202) | Transition to OTP step |
| B-4 | 2FA code invalid | Error; code cleared |
| B-5 | Session timeout | Redirect to P-01 with notice |

### Resilience and System-Status

- **Undo/Confirm:** N/A (login is atomic).
- **Resume vs Restart:** Each attempt is atomic; no persistence needed.
- **What Changed:** On success, Topbar shows user initials; Sidebar renders full nav.
- **Optimistic vs Confirmed:** Confirmed — waits for server (security-critical).

### Flow-Level Accessibility

- **Keyboard:** All fields keyboard-operable. Tab: email -> password -> remember-me -> submit -> forgot-password.
- **Focus Order:** On route to P-03, focus moves to main heading "Tong quan van hanh".
- **AT Completable:** Errors use Alert (aria-live). OTP uses inputMode="numeric".
- **No Mouse-Only:** Show/hide password has aria-label.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Login form | LoginPage | P-01; email + password |
| 2FA OTP form | LoginPage (2FA) | P-01; 6-digit code |
| Error: locked | LoginPage (locked) | P-01; error alert |
| Error: invalid | LoginPage (invalid) | P-01; inline error |
| Loading | LoginPage (submitting) | P-01; button disabled |
| Success redirect | DashboardPage | P-03; default landing |

### Success Criteria

User authenticated with valid JWT. Permissions loaded; sidebar correct. 2FA users complete both steps.

---

## Flow F-02 — Password Recovery

**Related SRS Scenario:** SC-05
**Primary Actor:** User
**Entry Point:** "Quen mat khau?" from P-01 -> `/forgot-password` (P-02)
**Success End State:** Password reset; user returns to LoginPage

### Flow Diagram — F-02

```
[Entry: ForgotPasswordPage (P-02) — Step 1]
         |
         v
[Step 1: Enter email, click GUI MA OTP]
         |
         +--- [Branch: Email not found]
         |           |
         |           v
         |  [Error: "Khong tim thay tai khoan voi email nay."]
         |           |
         |           +--> [Back to Step 1]
         |
         v
[Step 2: Enter 6-digit OTP from email]
         |
         +--- [Branch: OTP expired (> 10 min)]
         |           |
         |           v
         |  [Error: "Ma OTP da het han. Yeu cau lai."]
         |           |
         |           +--> [Back to Step 1]
         |
         v
[Step 3: Enter new password + confirm password]
         |
         +--- [Branch: Passwords do not match]
         |           |
         |           v
         |  [Error: "Mat khau xac nhan khong khop."]
         |           |
         |           +--> [Stay Step 3; confirm cleared]
         |
         v
[Step 4: Success screen]
         |
         v
[End: "Dat lai mat khau thanh cong" + link to /login]
```

### Narrative

1. User clicks "Quen mat khau?" on P-01 -> P-02 (4-step wizard).
2. **Step 1:** Enter email, click GUI MA OTP. Backend sends OTP. UI transitions to OTP input.
3. **Step 2:** Enter 6-digit OTP. TTL 600s (10 min). On success, transition to password form.
4. **Step 3:** Enter new password + confirmation. Validation: passwords match, strength requirements.
5. **Step 4:** Success screen with checkmark. Link "Quay lai dang nhap" -> P-01.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Email not found | Error; remain Step 1 |
| B-2 | OTP expired (TTL > 600s) | Error; redirect Step 1; new OTP sent |
| B-3 | OTP incorrect | Error; code cleared |
| B-4 | Passwords mismatch | Error; confirm cleared |
| B-5 | Network error | Error with retry; form preserved |

### Resilience and System-Status

- **Resume vs Restart:** Progress lost if navigate away. Acceptable for low-frequency flow.
- **What Changed:** Step 4 shows explicit success confirmation.

### Flow-Level Accessibility

- **Keyboard:** All inputs, buttons, back link keyboard-operable.
- **Focus Order:** Step transition moves focus to first input of new step.
- **AT Completable:** Errors via Alert. OTP uses inputMode="numeric".

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Request OTP | ForgotPasswordPage (Step 1) | P-02; email input |
| OTP input | ForgotPasswordPage (Step 2) | P-02; 6-digit code |
| Password form | ForgotPasswordPage (Step 3) | P-02; new + confirm |
| Success | ForgotPasswordPage (Step 4) | P-02; checkmark + link |
| Loading | ForgotPasswordPage (submitting) | P-02; button disabled |

### Success Criteria

User receives OTP, validates, sets new password. Can log in with new credentials.

---

## Flow F-03 — Dashboard Overview

**Related SRS Scenario:** SC-01, SC-02
**Primary Actor:** Admin / Sale Agent / Marketer
**Entry Point:** `/` after login, or Sidebar "Tong quan"
**Success End State:** User views real-time metrics; navigates to detail screens

### Flow Diagram — F-03

```
[Entry: DashboardPage (P-03)]
         |
         v
[Step 1: 6 API queries fire in parallel]
         |
         +--> [Loading: Skeleton cards]
         |
         v
[Step 2: 4 Metric cards rendered]
         |   - Hoi thoai AI xu ly
         |   - Lead moi
         |   - Chuyen doi
         |   - Phan hoi trung binh
         |
         v
[Step 3: Channel chart + Quick Actions + Funnel]
         |
         v
[Step 4: Forecast + Agent status + Anomaly table]
         |
         +--- [Branch: API error on any query]
         |           |
         |           v
         |  [Error banner + Retry; unaffected blocks render normally]
         |
         +--- [Branch: Quick Action click]
         |           |
         |           v
         |  [Navigate to /inbox (P-05) or /kb (P-14) or /notifications (P-09)]
         |
         v
[Success: Metrics visible; operational overview complete]
```

### Narrative

1. User lands on P-03. 6 parallel queries: omnichannel, delta, funnel, agent-performance, forecast, anomalies.
2. **Loading:** MetricCard shows skeletons. Realtime pill shows connection state.
3. **Happy path:** All resolve. MetricCards with delta comparison. ChannelChart bars per platform. FunnelPanel conversion funnel. ForecastChart SVG line. AgentStatus top 4 agents. LiveTaskTable anomalies.
4. **Error branch:** Failing block shows own error. Only affected block impacted. Global error if omnichannel fails.
5. **Quick Actions:** Three cards link to P-05, P-14, P-09.

### Branch and Error List

| Branch | Condition | Outcome |
|---|---|---|
| B-1 | Single API fail | That block shows error + retry |
| B-2 | All API fail | Global error banner; shell renders |
| B-3 | Session expired (401) | Redirect P-01 |
| B-4 | SignalR disconnected | "Cap nhat tuc thi gian doan" |
| B-5 | Quick Action click | Navigate to target |

### Resilience and System-Status

- **What Changed:** MetricCard delta ("+12.3%"). Tone positive/negative.
- **System Status:** Realtime pill shows SignalR state. Stale indicator if data stale.

### Flow-Level Accessibility

- **Keyboard:** Quick Actions keyboard-operable. Table rows tabbable.
- **Focus Order:** Route to P-03, focus -> h1 "Tong quan van hanh".
- **AT Completable:** MetricCard values announced. StatusPill uses text.

### Screens / States

| Screen / State | Name | Notes |
|---|---|---|
| Loading skeleton | DashboardPage (loading) | P-03; skeletons |
| Full dashboard | DashboardPage (loaded) | P-03; all metrics |
| Partial error | DashboardPage (partial-error) | P-03; some blocks error |
| Full error | DashboardPage (error) | P-03; global banner |

### Success Criteria

User sees real-time metrics within 3 seconds. Anomalies visible; quick-action navigation works.

---

"""

with open(out, 'a', encoding='utf-8') as f:
    f.write(part2)
print("Part 2 done:", len(part2), "chars")
