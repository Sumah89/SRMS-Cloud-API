using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace SRMS.Desktop.Services
{
    public class Logger : ILogger, IDisposable
    {
        private static readonly Lazy<Logger> _instance = new Lazy<Logger>(() => new Logger(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static ILogger Instance => _instance.Value;

        private readonly string _logPath;
        private readonly string _securityLogPath;
        private readonly object _lockObject = new object();
        private bool _disposed = false;
        private StreamWriter _logWriter;
        private StreamWriter _securityWriter;
        private Timer _flushTimer;

        private const int MAX_LOG_SIZE = 10 * 1024 * 1024;
        private const int MAX_LOG_FILES = 10;

        private Logger()
        {
            try
            {
                string logDirectory = Path.Combine(Global.AppDataPath, "Logs");
                if (!Directory.Exists(logDirectory))
                    Directory.CreateDirectory(logDirectory);

                _logPath = Path.Combine(logDirectory, $"app_{DateTime.Now:yyyyMMdd}.log");
                _securityLogPath = Path.Combine(logDirectory, $"security_{DateTime.Now:yyyyMMdd}.log");

                RotateLogIfNeeded(_logPath);
                RotateLogIfNeeded(_securityLogPath);

                _logWriter = new StreamWriter(_logPath, true) { AutoFlush = false };
                _securityWriter = new StreamWriter(_securityLogPath, true) { AutoFlush = false };

                _flushTimer = new Timer(FlushLogs, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

                LogInfo("Logger initialized");
            }
            catch { }
        }

        public void LogDebug(string message) => WriteLog(_logWriter, "DEBUG", message, null);
        public void LogInfo(string message) => WriteLog(_logWriter, "INFO", message, null);
        public void LogWarning(string message) => WriteLog(_logWriter, "WARN", message, null);
        public void LogError(string message, Exception ex = null) => WriteLog(_logWriter, "ERROR", message, ex);
        public void LogError(Exception ex, string context = null) => WriteLog(_logWriter, "ERROR", context ?? "Exception occurred", ex);

        public void LogSecurity(string message, string username = null, string ipAddress = null)
        {
            string formattedMessage = $"[User: {username ?? "Anonymous"}] [IP: {ipAddress ?? "Unknown"}] {message}";
            WriteLog(_securityWriter, "SECURITY", formattedMessage, null);
            WriteLog(_logWriter, "SECURITY", formattedMessage, null);
        }

        private void WriteLog(StreamWriter writer, string level, string message, Exception ex)
        {
            if (_disposed || writer == null) return;
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string threadId = Thread.CurrentThread.ManagedThreadId.ToString();
                string logEntry = $"{timestamp} | {threadId} | {level} | {message}";

                if (ex != null)
                {
                    logEntry += $"\nException: {ex.GetType().Name}: {ex.Message}";
                    logEntry += $"\nStackTrace: {ex.StackTrace}";
                    if (ex.InnerException != null)
                        logEntry += $"\nInnerException: {ex.InnerException.Message}";
                }

                lock (_lockObject)
                {
                    writer.WriteLine(logEntry);
                    if (level == "ERROR") writer.Flush();
                }
                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch { }
        }

        private void FlushLogs(object? state)
        {
            if (_disposed) return;
            try
            {
                lock (_lockObject)
                {
                    _logWriter?.Flush();
                    _securityWriter?.Flush();
                }
            }
            catch { }
        }

        private void RotateLogIfNeeded(string logPath)
        {
            try
            {
                if (!File.Exists(logPath)) return;
                FileInfo fileInfo = new FileInfo(logPath);
                if (fileInfo.Length < MAX_LOG_SIZE) return;

                string directory = Path.GetDirectoryName(logPath);
                string fileName = Path.GetFileNameWithoutExtension(logPath);
                string extension = Path.GetExtension(logPath);

                for (int i = MAX_LOG_FILES - 1; i >= 0; i--)
                {
                    string oldFile = Path.Combine(directory, $"{fileName}.{i}{extension}");
                    string newFile = Path.Combine(directory, $"{fileName}.{i + 1}{extension}");
                    if (File.Exists(oldFile))
                    {
                        if (i + 1 >= MAX_LOG_FILES) { try { File.Delete(oldFile); } catch { } }
                        else { try { File.Move(oldFile, newFile); } catch { } }
                    }
                }
                try { File.Move(logPath, Path.Combine(directory, $"{fileName}.0{extension}")); } catch { }
            }
            catch { }
        }

        public string GetRecentLogs(int lineCount = 100)
        {
            try
            {
                if (!File.Exists(_logPath))
                    return "No logs found";

                string[] lines = File.ReadAllLines(_logPath);
                int startIndex = Math.Max(0, lines.Length - lineCount);
                string[] recentLines = lines.Skip(startIndex).ToArray();
                return string.Join(Environment.NewLine, recentLines);
            }
            catch
            {
                return "Error reading logs";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _flushTimer?.Dispose();
            lock (_lockObject)
            {
                _logWriter?.Flush();
                _logWriter?.Dispose();
                _logWriter = null;
                _securityWriter?.Flush();
                _securityWriter?.Dispose();
                _securityWriter = null;
            }
            _disposed = true;
        }
    }
}