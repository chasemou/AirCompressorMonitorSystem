using System;

namespace WpfTest.Models
{
    /// <summary>
    /// 日志条目：时间 + 级别 + 消息
    /// </summary>
    public class LogEntry
    {
        public DateTime Time { get; set; }
        public string Level { get; set; } = "INFO";  // INFO / WARN / ERROR
        public string Message { get; set; } = string.Empty;

        public override string ToString() => $"{Time:HH:mm:ss} [{Level}] {Message}";
    }
}
