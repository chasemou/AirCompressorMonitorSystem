using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using WpfTest.Models;

namespace WpfTest.Services
{
    /// <summary>
    /// 轻量日志服务：三级日志（INFO/WARN/ERROR），内存保留最近200条，同时异步写文件。
    /// </summary>
    public class LogService
    {
        /// <summary>UI 绑定用的日志集合（最新在最前）</summary>
        public ObservableCollection<LogEntry> Logs { get; } = new();

        private const int MaxMemoryLogs = 200;
        private readonly string _logDir;
        private readonly object _fileLock = new();

        public LogService()
        {
            _logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            try { Directory.CreateDirectory(_logDir); } catch { }
        }

        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message) => Write("ERROR", message);

        private void Write(string level, string message)
        {
            var entry = new LogEntry { Time = DateTime.Now, Level = level, Message = message };

            //UI 线程更新集合（ObservableCollection 不支持跨线程修改）
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Logs.Insert(0, entry);
                if (Logs.Count > MaxMemoryLogs)
                    Logs.RemoveAt(Logs.Count - 1);
            });

            //异步写文件，不阻塞调用线程
            Task.Run(() =>
            {
                try
                {
                    string file = Path.Combine(_logDir, $"{DateTime.Now:yyyyMMdd}.txt");
                    lock (_fileLock)
                    {
                        File.AppendAllText(file, $"{entry.Time:yyyy-MM-dd HH:mm:ss} [{level}] {message}\n");
                    }
                }
                catch { /* 写日志失败不影响主流程 */ }
            });
        }
    }
}
