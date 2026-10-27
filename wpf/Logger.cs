using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ServiceScheduler
{
    public static class Logger
    {
        public const string LogName = "ServiceScheduler";
        public const string SourceName = "ServiceScheduler";
        public static string LastError { get; private set; }

        public static bool Write(string message, EventLogEntryType type, int eventId)
        {
            try
            {
                if (!EventLog.SourceExists(SourceName))
                    EventLog.CreateEventSource(SourceName, LogName);
                EventLog.WriteEntry(SourceName, message ?? string.Empty, type, eventId);
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

        public static void Info(string message)
        {
            Write(message, EventLogEntryType.Information, 1000);
        }

        public static void Success(string message)
        {
            Write(message, EventLogEntryType.Information, 1001);
        }

        public static void Error(string message)
        {
            Write(message, EventLogEntryType.Error, 1002);
        }

        public static void Warning(string message)
        {
            Write(message, EventLogEntryType.Warning, 1003);
        }

        public static string ReadRecent(int maximum)
        {
            LastError = null;
            try
            {
                if (!EventLog.Exists(LogName))
                    return string.Empty;
                List<string> lines = new List<string>();
                using (EventLog log = new EventLog(LogName))
                {
                    int count = log.Entries.Count;
                    int start = Math.Max(0, count - maximum);
                    for (int i = count - 1; i >= start; i--)
                    {
                        EventLogEntry entry = log.Entries[i];
                        string message = (entry.Message ?? string.Empty)
                            .Replace("\r\n", " | ").Replace("\n", " | ");
                        lines.Add(string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}",
                            entry.TimeGenerated, TypeToRussian(entry.EntryType), message));
                    }
                }
                return string.Join(Environment.NewLine, lines.ToArray());
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return string.Empty;
            }
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
