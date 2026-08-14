namespace Clawbot.Agents.Core.Chat;

/// <summary>
/// Kết luận của <see cref="LlmBaseUrlGuard.CheckBaseUrl"/>.
/// Tách riêng từng lý do vì gộp hết vào một chữ "URL không hợp lệ" khiến người vận hành
/// không phân biệt được mình gõ sai với việc máy chủ đang hỏng DNS.
/// </summary>
public enum BaseUrlVerdict
{
    /// <summary>Host phân giải ra toàn địa chỉ public — dùng được.</summary>
    Allowed = 0,

    /// <summary>
    /// Máy chủ không phân giải được tên miền nên chưa xác minh được là public hay nội bộ.
    /// Vẫn cho lưu cấu hình: chặn thật nằm ở lúc mở kết nối, nơi mọi địa chỉ nội bộ đều bị từ chối.
    /// </summary>
    AllowedDnsUnverified = 1,

    /// <summary>Không phải URL tuyệt đối, hoặc có nhúng user:password.</summary>
    Malformed = 2,

    /// <summary>Không phải http/https, hoặc dùng http cleartext ra ngoài internet.</summary>
    SchemeNotAllowed = 3,

    /// <summary>Trỏ vào mạng nội bộ/loopback mà người vận hành chưa cấp phép.</summary>
    PrivateHostNotGranted = 4,

    /// <summary>DNS trả về lẫn địa chỉ public và nội bộ — dấu hiệu DNS rebinding.</summary>
    MixedDnsAnswer = 5,
}

public static class BaseUrlVerdictExtensions
{
    public static bool IsAllowed(this BaseUrlVerdict verdict) =>
        verdict is BaseUrlVerdict.Allowed or BaseUrlVerdict.AllowedDnsUnverified;

    /// <summary>Mã lỗi trả về API, giữ dạng snake_case như các mã sẵn có.</summary>
    public static string ToErrorCode(this BaseUrlVerdict verdict) => verdict switch
    {
        BaseUrlVerdict.SchemeNotAllowed => "base_url_requires_https",
        BaseUrlVerdict.PrivateHostNotGranted => "base_url_private_host",
        BaseUrlVerdict.MixedDnsAnswer => "base_url_mixed_dns",
        _ => "invalid_base_url",
    };
}
