using System;

namespace SRMS.Desktop.Services
{
    public interface ILogger
    {
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception ex = null);
        void LogError(Exception ex, string context = null);
        void LogSecurity(string message, string username = null, string ipAddress = null);
    }
}