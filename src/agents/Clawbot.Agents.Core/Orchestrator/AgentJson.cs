using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Clawbot.Agents.Core.Orchestrator;

/// <summary>
/// Chuẩn serialize cho mọi payload của agent (kết quả tool, đầu vào task, tin A2A): camelCase giống FE và
/// KHÔNG escape ký tự non-ASCII. Encoder mặc định của System.Text.Json biến tiếng Việt thành ế... nên
/// UI hiển thị ra chuỗi rác và LLM cũng tốn thêm token để đọc. UnicodeRanges.All giữ nguyên chữ có dấu
/// nhưng vẫn escape ký tự nhạy cảm HTML (&lt; &gt; &amp; ') nên an toàn khi nhúng lại vào trang.
/// </summary>
public static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };
}
