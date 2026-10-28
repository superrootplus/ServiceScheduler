using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ServiceScheduler
{
    public sealed class LogEntryInfo
    {
        public DateTime Time { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string TaskPath { get; set; }
        public string ServiceName { get; set; }

        public string TimeText { get { return Time.ToString("dd.MM.yyyy HH:mm:ss"); } }
        public string TaskName
        {
            get
            {
                if (string.IsNullOrEmpty(TaskPath)) return string.Empty;
                int slash = TaskPath.LastIndexOf('\\');
                return slash >= 0 ? TaskPath.Substring(slash + 1) : TaskPath;
            }
        }
    }

    public static class Logger
    {
        public const string LogName = "ServiceScheduler";
        public const string SourceName = "ServiceScheduler";
        public static string LastError { get; private set; }

        public static bool Write(string message, EventLogEntryType type, int eventId,
            string taskPath = null, string serviceName = null)
        {
            try
            {
                if (!EventLog.SourceExists(SourceName))
                    EventLog.CreateEventSource(SourceName, LogName);
                string text = AddContext(message, taskPath, serviceName);
                EventLog.WriteEntry(SourceName, text, type, eventId);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                // Журнал не должен прерывать управление службой, но ошибку сохраняем.
                LastError = ex.Message;
                return false;
            }
        }

        private static string AddContext(string message, string taskPath, string serviceName)
        {
            StringBuilder text = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(taskPath))
                text.AppendLine("Задание: " + taskPath);
            if (!string.IsNullOrWhiteSpace(serviceName))
                text.AppendLine("Служба: " + serviceName);
            text.Append(message ?? string.Empty);
            return text.ToString();
        }

        public static void Info(string message, string taskPath = null, string serviceName = null)
        {
            Write(message, EventLogEntryType.Information, 1000, taskPath, serviceName);
        }

        public static void Success(string message, string taskPath = null, string serviceName = null)
        {
            Write(message, EventLogEntryType.Information, 1001, taskPath, serviceName);
        }

        public static void Error(string message, string taskPath = null, string serviceName = null)
        {
            Write(message, EventLogEntryType.Error, 1002, taskPath, serviceName);
        }

        public static void Warning(string message, string taskPath = null, string serviceName = null)
        {
            Write(message, EventLogEntryType.Warning, 1003, taskPath, serviceName);
        }

        public static List<LogEntryInfo> ReadEntries(int maximum)
        {
            LastError = null;
            List<LogEntryInfo> result = new List<LogEntryInfo>();
            try
            {
                if (!EventLog.Exists(LogName)) return result;
                using (EventLog log = new EventLog(LogName))
                {
                    int count = log.Entries.Count;
                    int start = Math.Max(0, count - maximum);
                    for (int i = count - 1; i >= start; i--)
                    {
                        EventLogEntry entry = log.Entries[i];
                        result.Add(ReadEntry(entry));
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return result;
        }

        private static LogEntryInfo ReadEntry(EventLogEntry entry)
        {
            List<string> messageLines = new List<string>();
            string taskPath = string.Empty;
            string serviceName = string.Empty;
            string[] lines = (entry.Message ?? string.Empty).Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (line.StartsWith("Задание:", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(taskPath))
                    taskPath = line.Substring("Задание:".Length).Trim();
                else if (line.StartsWith("Служба:", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(serviceName))
                    serviceName = line.Substring("Служба:".Length).Trim();
                else
                    messageLines.Add(line.Trim());
            }
            return new LogEntryInfo
            {
                Time = entry.TimeGenerated,
                Level = TypeToRussian(entry.EntryType),
                TaskPath = taskPath,
                ServiceName = serviceName,
                Message = string.Join(" | ", messageLines.ToArray())
            };
        }

        public static string ReadRecent(int maximum)
        {
            List<LogEntryInfo> entries = ReadEntries(maximum);
            return string.Join(Environment.NewLine, entries.Select(entry => string.Format(
                "[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}", entry.Time, entry.Level,
                entry.Message)).ToArray());
        }

        private static string TypeToRussian(EventLogEntryType type)
        {
            switch (type)
            {
                case EventLogEntryType.Error: return "Ошибка";
                case EventLogEntryType.Warning: return "Предупреждение";
                case EventLogEntryType.Information: return "Сведения";
                case EventLogEntryType.SuccessAudit: return "Аудит успеха";
                case EventLogEntryType.FailureAudit: return "Аудит отказа";
                default: return type.ToString();
            }
        }
    }
}
