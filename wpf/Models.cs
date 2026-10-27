using System;
using System.Collections.Generic;

namespace ServiceScheduler
{
    public enum ServiceOperation
    {
        Start,
        Stop,
        Restart,
        HardRestart
    }

    public static class OperationInfo
    {
        public static string ToCode(ServiceOperation operation)
        {
            switch (operation)
            {
                case ServiceOperation.Start: return "start";
                case ServiceOperation.Stop: return "stop";
                case ServiceOperation.Restart: return "restart";
                case ServiceOperation.HardRestart: return "hard_restart";
                default: throw new ArgumentOutOfRangeException("operation");
            }
        }

        public static bool TryFromCode(string code, out ServiceOperation operation)
        {
            switch ((code ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "start": operation = ServiceOperation.Start; return true;
                case "stop": operation = ServiceOperation.Stop; return true;
                case "restart": operation = ServiceOperation.Restart; return true;
                case "hard_restart": operation = ServiceOperation.HardRestart; return true;
                default: operation = ServiceOperation.Start; return false;
            }
        }

        public static string ToRussian(ServiceOperation operation)
        {
            switch (operation)
            {
                case ServiceOperation.Start: return "Запуск";
                case ServiceOperation.Stop: return "Остановка";
                case ServiceOperation.Restart: return "Перезапуск";
                case ServiceOperation.HardRestart: return "Жёсткий перезапуск (kill)";
                default: return operation.ToString();
            }
        }

        public static IEnumerable<ServiceOperation> All
        {
            get
            {
                yield return ServiceOperation.Start;
                yield return ServiceOperation.Stop;
                yield return ServiceOperation.Restart;
                yield return ServiceOperation.HardRestart;
            }
        }
    }

    public sealed class ServiceInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Status { get; set; }
        public bool IsRunning { get; set; }
        public bool IsSharedProcess { get; set; }
        public bool ProcessTypeKnown { get; set; }

        public string Display
        {
            get { return string.Format("{0}  ({1})", DisplayName, Name); }
        }

        public string SearchText
        {
            get { return string.Format("{0} {1}", DisplayName, Name); }
        }

        public override string ToString()
        {
            return Display;
        }
    }

    public enum ScheduleType
    {
        Daily,
        Weekly,
        Monthly,
        Once,
        Startup,
        Interval,
        Other
    }

    public sealed class ScheduleSpec
    {
        public ScheduleType Type { get; set; }
        public TimeSpan Time { get; set; }
        public DateTime Date { get; set; }
        public int DaysOfWeekMask { get; set; }
        public int DayOfMonth { get; set; }
        public int IntervalMinutes { get; set; }
        public int TriggerCount { get; set; }
        public string OtherDescription { get; set; }

        public ScheduleSpec()
        {
            Type = ScheduleType.Daily;
            Time = new TimeSpan(9, 0, 0);
            Date = DateTime.Today;
            DayOfMonth = 1;
            IntervalMinutes = 60;
            DaysOfWeekMask = 2;
            TriggerCount = 1;
        }

        public ScheduleSpec Clone()
        {
            return (ScheduleSpec)MemberwiseClone();
        }
    }

    public sealed class TaskInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string FolderPath { get; set; }
        public string ServiceName { get; set; }
        public ServiceOperation? Operation { get; set; }
        public string OperationText { get; set; }
        public string ScheduleText { get; set; }
        public ScheduleSpec Schedule { get; set; }
        public string LastRunText { get; set; }
        public string NextRunText { get; set; }
        public string StateText { get; set; }
        public string LastResultText { get; set; }
        public bool IsOwn { get; set; }
        public bool IsEnabled { get; set; }
        public string KindText { get; set; }
        public string Description { get; set; }
        public string ActionExecutable { get; set; }
        public string ActionArguments { get; set; }
        public bool AllowSharedKill { get; set; }
    }

    public sealed class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        public OperationResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }
    }

    public sealed class ServiceSchedulerException : Exception
    {
        public ServiceSchedulerException(string message) : base(message) { }
        public ServiceSchedulerException(string message, Exception inner)
            : base(message, inner) { }
    }
}
