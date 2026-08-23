// Link bài đăng đến từ dữ liệu nhà cung cấp (Meta) nên coi là không tin cậy: chỉ mở khi trỏ đúng
// host mạng xã hội đã biết, https, không kèm port/credential. Dùng chung cho mọi nơi render postUrl —
// nhân bản hàm này ở từng màn hình là cách nhanh nhất để một chỗ quên guard.
const TRUSTED_SOCIAL_POST_HOSTS = new Set([
  "facebook.com",
  "www.facebook.com",
  "m.facebook.com",
  "instagram.com",
  "www.instagram.com",
]);

export function isSafeExternalPostUrl(value: string | null | undefined): value is string {
  if (!value) return false;
  try {
    const url = new URL(value);
    return url.protocol === "https:"
      && url.port === ""
      && url.username === ""
      && url.password === ""
      && TRUSTED_SOCIAL_POST_HOSTS.has(url.hostname);
  } catch {
    return false;
  }
}
