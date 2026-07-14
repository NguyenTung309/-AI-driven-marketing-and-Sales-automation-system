// Web Push service worker: user đóng tab vẫn nhận được thông báo AI chạy xong / lỗi.
// Bấm vào thông báo mở đúng link đích (cùng deep link với toast trong app).

self.addEventListener("push", (event) => {
  if (!event.data) return;

  let payload;
  try {
    payload = event.data.json();
  } catch {
    payload = { title: "ClawBot", body: event.data.text(), url: "/notifications" };
  }

  event.waitUntil(
    self.registration.showNotification(payload.title ?? "ClawBot", {
      body: payload.body ?? "",
      data: { url: payload.url ?? "/notifications" },
      tag: payload.id,
    })
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const url = event.notification.data?.url ?? "/notifications";

  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
      // Tab đang mở thì điều hướng tab đó thay vì mở thêm cửa sổ.
      for (const client of clients) {
        if ("focus" in client) {
          client.navigate(url);
          return client.focus();
        }
      }
      return self.clients.openWindow(url);
    })
  );
});
