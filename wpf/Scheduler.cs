using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace ServiceScheduler
{
    public static class Scheduler
    {
        public const string FolderPath = "\\ServiceManager\\";
        private const string MetadataMarker = "ServiceScheduler|v2";
        private const string LegacyPrefix = "SvcMgr_";

        private static dynamic CreateConnection()
        {
            Type type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null)
                throw new ServiceSchedulerException("Планировщик заданий Windows недоступен.");
            dynamic service = Activator.CreateInstance(type);
            service.Connect();
            return service;
        }

        private static dynamic GetFolder(dynamic service, bool create)
        {
            try
            {
                // COM ITaskService::GetFolder не принимает завершающий '\' у подпапки.
                return service.GetFolder(FolderPath.TrimEnd('\\'));
            }
            catch (Exception ex)
            {
                if (!create || !(ex is COMException || ex is System.IO.FileNotFoundException))
                    throw;
                dynamic root = service.GetFolder("\\");
                return root.CreateFolder("ServiceManager");
            }
        }

        public static List<TaskInfo> ListTasks(bool ownOnly)
        {
            dynamic service = CreateConnection();
            List<TaskInfo> result = new List<TaskInfo>();
            if (ownOnly)
            {
                try
                {
                    dynamic folder = GetFolder(service, false);
                    ReadFolderTasks(folder, FolderPath, result);
                }
                catch (Exception ex)
                {
                    if (!(ex is COMException || ex is System.IO.FileNotFoundException))
                        throw;
                    return result;
                }
            }
            else
            {
                dynamic root = service.GetFolder("\\");
                ReadFolderRecursive(root, result);
            }
            return result;
        }

        private static void ReadFolderRecursive(dynamic folder, List<TaskInfo> result)
        {
            string path = Convert.ToString(folder.Path);
            ReadFolderTasks(folder, path, result);
            dynamic folders = folder.GetFolders(0);
            int count = folders.Count;
            for (int i = 1; i <= count; i++)
            {
                try { ReadFolderRecursive(folders.Item(i), result); }
                catch (COMException) { }
            }
        }

        private static void ReadFolderTasks(dynamic folder, string folderPath,
            List<TaskInfo> result)
        {
            dynamic tasks = folder.GetTasks(1); // включая скрытые
            int count = tasks.Count;
            for (int i = 1; i <= count; i++)
            {
                try { result.Add(ReadTask(tasks.Item(i), folderPath)); }
                catch (COMException) { }
            }
        }

        private static TaskInfo ReadTask(dynamic task, string folderPath)
        {
            string name = Convert.ToString(task.Name);
            string description = string.Empty;
            string executable = string.Empty;
            string arguments = string.Empty;
            ScheduleSpec schedule;
            string scheduleText;

            dynamic definition = task.Definition;
            try { description = Convert.ToString(definition.RegistrationInfo.Description); }
            catch { }
            try
            {
                if (definition.Actions.Count > 0)
                {
                    dynamic action = definition.Actions.Item(1);
                    executable = Convert.ToString(action.Path);
                    arguments = Convert.ToString(action.Arguments);
                }
            }
            catch { }
            schedule = ReadSchedule(definition.Triggers, out scheduleText);

            string metadataService;
            ServiceOperation metadataOperation;
            bool metadataAllow;
            bool hasMetadata = TryReadMetadata(description, out metadataService,
                out metadataOperation, out metadataAllow);
            string legacyService;
            ServiceOperation legacyOperation;
            bool hasLegacy = TryParseLegacyName(name, out legacyService, out legacyOperation);
            bool executorMarker = (arguments ?? string.Empty).IndexOf("--exec",
                StringComparison.OrdinalIgnoreCase) >= 0;
            bool own = string.Equals(folderPath, FolderPath, StringComparison.OrdinalIgnoreCase)
                && executorMarker && (hasMetadata || hasLegacy);

            TaskInfo info = new TaskInfo
            {
                Name = name,
                Path = Convert.ToString(task.Path),
                FolderPath = folderPath,
                Description = description,
                ActionExecutable = executable,
                ActionArguments = arguments,
                IsOwn = own,
                KindText = own ? "Своя" : "Системная",
                IsEnabled = Convert.ToBoolean(task.Enabled),
                StateText = StateToRussian(Convert.ToInt32(task.State)),
                Schedule = schedule,
                ScheduleText = scheduleText,
                ServiceName = hasMetadata ? metadataService :
                    (hasLegacy ? legacyService : string.Empty),
                Operation = hasMetadata ? metadataOperation :
                    (hasLegacy ? (ServiceOperation?)legacyOperation : null),
                AllowSharedKill = hasMetadata ? metadataAllow :
                    (arguments ?? string.Empty).IndexOf("--allow-shared-kill",
                        StringComparison.OrdinalIgnoreCase) >= 0
            };
            info.OperationText = info.Operation.HasValue
                ? OperationInfo.ToRussian(info.Operation.Value) : string.Empty;
            DateTime last = ReadComDate(task.LastRunTime);
            DateTime next = ReadComDate(task.NextRunTime);
            info.LastRunText = FormatTaskDate(last);
            info.NextRunText = FormatTaskDate(next);
            int resultCode = Convert.ToInt32(task.LastTaskResult);
            info.LastResultText = last == DateTime.MinValue ? "—" : FormatResult(resultCode);
            return info;
        }

        private static DateTime ReadComDate(dynamic value)
        {
            try
            {
                DateTime date = Convert.ToDateTime(value);
                return date.Year > 2000 && date.Year < 9000 ? date : DateTime.MinValue;
            }
            catch { return DateTime.MinValue; }
        }

        private static string FormatTaskDate(DateTime date)
        {
            return date == DateTime.MinValue ? "—" : date.ToString("dd.MM.yyyy HH:mm:ss");
        }

        private static string FormatResult(int code)
        {
            string text = string.Format("0x{0:X}", code);
            if (code == 0) return "Успех (0x0)";
            if (code == 0x41301) return "Выполняется (0x41301)";
            if (code == 0x41303) return "Не запускалось (0x41303)";
            if (code == 0x41306) return "Прервано (0x41306)";
            return text;
        }

        private static string StateToRussian(int state)
        {
            switch (state)
            {
                case 1: return "Отключено";
                case 2: return "В очереди";
                case 3: return "Готово";
                case 4: return "Выполняется";
                default: return "Неизвестно";
            }
        }

        private static bool TryReadMetadata(string description, out string service,
            out ServiceOperation operation, out bool allowSharedKill)
        {
            service = null;
            operation = ServiceOperation.Start;
            allowSharedKill = false;
            if (string.IsNullOrEmpty(description) ||
                !description.StartsWith(MetadataMarker, StringComparison.OrdinalIgnoreCase))
                return false;
            bool operationFound = false;
            string[] lines = description.Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (line.StartsWith("Service=", StringComparison.OrdinalIgnoreCase))
                    service = line.Substring("Service=".Length);
                else if (line.StartsWith("Operation=", StringComparison.OrdinalIgnoreCase))
                    operationFound = OperationInfo.TryFromCode(
                        line.Substring("Operation=".Length), out operation);
                else if (line.StartsWith("AllowSharedKill=", StringComparison.OrdinalIgnoreCase))
                    allowSharedKill = line.EndsWith("1", StringComparison.Ordinal);
            }
            return !string.IsNullOrWhiteSpace(service) && operationFound;
        }

        public static bool TryParseLegacyName(string taskName, out string service,
            out ServiceOperation operation)
        {
            service = null;
            operation = ServiceOperation.Start;
            if (string.IsNullOrEmpty(taskName) || !taskName.StartsWith(LegacyPrefix))
                return false;
            string rest = taskName.Substring(LegacyPrefix.Length);
            // Сначала длинный суффикс: hard_restart не должен превращаться в restart.
            string[] codes = { "hard_restart", "restart", "start", "stop" };
            foreach (string code in codes)
            {
                string suffix = "_" + code;
                if (rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                    rest.Length > suffix.Length)
                {
                    service = rest.Substring(0, rest.Length - suffix.Length);
                    operation = (ServiceOperation)Enum.Parse(typeof(ServiceOperation),
                        code == "hard_restart" ? "HardRestart" :
                        code == "restart" ? "Restart" :
                        code == "start" ? "Start" : "Stop");
                    return true;
                }
            }
            return false;
        }

        private static ScheduleSpec ReadSchedule(dynamic triggers, out string text)
        {
            ScheduleSpec spec = new ScheduleSpec { TriggerCount = triggers.Count };
            text = string.Empty;
            if (triggers.Count == 0)
            {
                text = "Нет триггера";
                return spec;
            }
            dynamic trigger = triggers.Item(1);
            int type = Convert.ToInt32(trigger.Type);
            DateTime start = ParseTriggerDate(Convert.ToString(trigger.StartBoundary));
            spec.Date = start == DateTime.MinValue ? DateTime.Today : start.Date;
            spec.Time = start == DateTime.MinValue ? new TimeSpan(9, 0, 0) : start.TimeOfDay;
            int repetitionMinutes = ReadRepetitionMinutes(trigger);

            switch (type)
            {
                case 1:
                    spec.Type = repetitionMinutes > 0 ? ScheduleType.Interval : ScheduleType.Once;
                    spec.IntervalMinutes = repetitionMinutes > 0 ? repetitionMinutes : 60;
                    break;
                case 2:
                    spec.Type = ScheduleType.Daily;
                    break;
                case 3:
                    spec.Type = ScheduleType.Weekly;
                    spec.DaysOfWeekMask = Convert.ToInt32(trigger.DaysOfWeek);
                    break;
                case 4:
                    spec.Type = ScheduleType.Monthly;
                    int mask = Convert.ToInt32(trigger.DaysOfMonth);
                    spec.DayOfMonth = MaskToDay(mask);
                    break;
                case 8:
                    spec.Type = ScheduleType.Startup;
                    break;
                default:
                    spec.Type = ScheduleType.Other;
                    spec.OtherDescription = TriggerTypeToRussian(type);
                    break;
            }
            text = DescribeSchedule(spec);
            if (spec.TriggerCount > 1)
                text += string.Format("  (+{0} триггера)", spec.TriggerCount - 1);
            return spec;
        }

        private static int ReadRepetitionMinutes(dynamic trigger)
        {
            try
            {
                string interval = Convert.ToString(trigger.Repetition.Interval);
                if (string.IsNullOrWhiteSpace(interval)) return 0;
                TimeSpan duration = XmlConvert.ToTimeSpan(interval);
                return (int)Math.Round(duration.TotalMinutes);
            }
            catch { return 0; }
        }

        private static DateTime ParseTriggerDate(string value)
        {
            DateTime result;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out result))
                return result;
            return DateTime.MinValue;
        }

        private static int MaskToDay(int mask)
        {
            for (int day = 1; day <= 31; day++)
                if ((mask & (1 << (day - 1))) != 0) return day;
            return 1;
        }

        private static string TriggerTypeToRussian(int type)
        {
            switch (type)
            {
                case 5: return "Ежемесячно по дням недели";
                case 6: return "При простое";
                case 7: return "При регистрации";
                case 9: return "При входе в систему";
                case 11: return "При смене состояния сеанса";
                case 12: return "По событию";
                default: return "Другой триггер (" + type + ")";
            }
        }

        public static string DescribeSchedule(ScheduleSpec spec)
        {
            string time = spec.Time.ToString(@"hh\:mm");
            switch (spec.Type)
            {
                case ScheduleType.Daily:
                    return "Ежедневно в " + time;
                case ScheduleType.Weekly:
                    return "Еженедельно (" + DaysOfWeekToString(spec.DaysOfWeekMask) + ") в " + time;
                case ScheduleType.Monthly:
                    return string.Format("Ежемесячно, {0}-е число в {1}", spec.DayOfMonth, time);
                case ScheduleType.Once:
                    return string.Format("Однократно {0:dd.MM.yyyy} в {1}", spec.Date, time);
                case ScheduleType.Startup:
                    return "При запуске системы";
                case ScheduleType.Interval:
                    return string.Format("Каждые {0} мин, начиная с {1}", spec.IntervalMinutes, time);
                default:
                    return spec.OtherDescription ?? "Другое расписание";
            }
        }

        private static string DaysOfWeekToString(int mask)
        {
            List<string> days = new List<string>();
            if ((mask & 2) != 0) days.Add("Пн");
            if ((mask & 4) != 0) days.Add("Вт");
            if ((mask & 8) != 0) days.Add("Ср");
            if ((mask & 16) != 0) days.Add("Чт");
            if ((mask & 32) != 0) days.Add("Пт");
            if ((mask & 64) != 0) days.Add("Сб");
            if ((mask & 1) != 0) days.Add("Вс");
            return days.Count == 0 ? "дни не выбраны" : string.Join(", ", days.ToArray());
        }

        public static string SaveTask(string originalName, string serviceName,
            ServiceOperation operation, ScheduleSpec schedule, bool disabled,
            bool allowSharedKill, string executorPath)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ServiceSchedulerException("Не выбрана служба.");
            if (!string.IsNullOrEmpty(originalName) &&
                (originalName.Contains("\\") || originalName.Contains("/")))
                throw new ServiceSchedulerException("Некорректное имя исходного задания.");

            dynamic service = CreateConnection();
            dynamic folder = GetFolder(service, true);
            string name = string.IsNullOrEmpty(originalName) ?
                LegacyPrefix + Guid.NewGuid().ToString("N").Substring(0, 12) : originalName;
            dynamic definition = service.NewTask(0);
            definition.RegistrationInfo.Author = "ServiceScheduler";
            definition.RegistrationInfo.Description = BuildMetadata(serviceName, operation,
                allowSharedKill);

            definition.Principal.UserId = "SYSTEM";
            definition.Principal.LogonType = 5;
            definition.Principal.RunLevel = 1;
            definition.Settings.Enabled = !disabled;
            definition.Settings.StartWhenAvailable = true;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.RunOnlyIfNetworkAvailable = false;
            definition.Settings.ExecutionTimeLimit = "PT0M";
            definition.Settings.MultipleInstances = 2; // не запускать второй экземпляр
            definition.Settings.Hidden = false;

            dynamic action = definition.Actions.Create(0);
            action.Path = executorPath;
            action.Arguments = BuildExecutorArguments(serviceName, operation, allowSharedKill);

            AddTrigger(definition.Triggers, schedule);
            folder.RegisterTaskDefinition(name, definition, 6, "SYSTEM", null, 5, null);
            return FolderPath + name;
        }

        public static string BuildExecutorArguments(string serviceName,
            ServiceOperation operation, bool allowSharedKill)
        {
            return string.Format("--exec \"{0}\" {1}{2}", serviceName,
                OperationInfo.ToCode(operation), allowSharedKill ? " --allow-shared-kill" : "");
        }

        private static string BuildMetadata(string serviceName, ServiceOperation operation,
            bool allowSharedKill)
        {
            return MetadataMarker + "\nService=" + serviceName + "\nOperation=" +
                OperationInfo.ToCode(operation) + "\nAllowSharedKill=" +
                (allowSharedKill ? "1" : "0");
        }

        private static void AddTrigger(dynamic triggers, ScheduleSpec schedule)
        {
            int comType;
            switch (schedule.Type)
            {
                case ScheduleType.Daily: comType = 2; break;
                case ScheduleType.Weekly: comType = 3; break;
                case ScheduleType.Monthly: comType = 4; break;
                case ScheduleType.Startup: comType = 8; break;
                default: comType = 1; break;
            }
            dynamic trigger = triggers.Create(comType);
            trigger.Enabled = true;
            if (schedule.Type != ScheduleType.Startup)
            {
                DateTime start = schedule.Type == ScheduleType.Once
                    ? schedule.Date.Date.Add(schedule.Time)
                    : DateTime.Today.Add(schedule.Time);
                trigger.StartBoundary = start.ToString("yyyy-MM-ddTHH:mm:ss",
                    CultureInfo.InvariantCulture);
            }

            switch (schedule.Type)
            {
                case ScheduleType.Daily:
                    trigger.DaysInterval = 1;
                    break;
                case ScheduleType.Weekly:
                    trigger.WeeksInterval = 1;
                    trigger.DaysOfWeek = schedule.DaysOfWeekMask;
                    break;
                case ScheduleType.Monthly:
                    trigger.MonthsOfYear = 4095;
                    trigger.DaysOfMonth = 1 << (schedule.DayOfMonth - 1);
                    break;
                case ScheduleType.Interval:
                    trigger.Repetition.Interval = "PT" + schedule.IntervalMinutes + "M";
                    trigger.Repetition.Duration = ""; // бессрочно
                    trigger.Repetition.StopAtDurationEnd = false;
                    break;
            }
        }

        private static string ValidateOwnPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(FolderPath,
                StringComparison.OrdinalIgnoreCase))
                throw new ServiceSchedulerException("Разрешены только задания папки " + FolderPath);
            string name = path.Substring(FolderPath.Length);
            if (string.IsNullOrWhiteSpace(name) || name.Contains("\\"))
                throw new ServiceSchedulerException("Некорректное имя задания.");
            return name;
        }

        private static dynamic OpenOwnTask(dynamic service, dynamic folder, string path)
        {
            string name = ValidateOwnPath(path);
            return folder.GetTask(name);
        }

        public static void DeleteTask(string path)
        {
            dynamic service = CreateConnection();
            dynamic folder = GetFolder(service, false);
            string name = ValidateOwnPath(path);
            folder.DeleteTask(name, 0);
        }

        public static void RunNow(string path)
        {
            dynamic service = CreateConnection();
            dynamic folder = GetFolder(service, false);
            dynamic task = OpenOwnTask(service, folder, path);
            task.Run(null);
        }

        public static void SetEnabled(string path, bool enabled)
        {
            dynamic service = CreateConnection();
            dynamic folder = GetFolder(service, false);
            dynamic task = OpenOwnTask(service, folder, path);
            task.Enabled = enabled;
        }

        public static int UpdateExecutorForOwnTasks(string executorPath, string onlyPath = null)
        {
            dynamic service = CreateConnection();
            dynamic folder;
            try { folder = GetFolder(service, false); }
            catch (Exception ex)
            {
                if (ex is COMException || ex is System.IO.FileNotFoundException) return 0;
                throw;
            }
            int updated = 0;
            dynamic tasks = folder.GetTasks(1);
            int count = tasks.Count;
            for (int i = 1; i <= count; i++)
            {
                dynamic task = tasks.Item(i);
                TaskInfo info = ReadTask(task, FolderPath);
                if (!string.IsNullOrEmpty(onlyPath) && !string.Equals(info.Path, onlyPath,
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (!info.IsOwn || !info.Operation.HasValue) continue;
                string currentPath = (info.ActionExecutable ?? string.Empty).Trim().Trim('"');
                string expectedArguments = BuildExecutorArguments(info.ServiceName,
                    info.Operation.Value, info.AllowSharedKill);
                if (string.Equals(currentPath, executorPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((info.ActionArguments ?? string.Empty).Trim(), expectedArguments,
                        StringComparison.Ordinal)) continue;
                dynamic definition = task.Definition;
                definition.Actions.Item(1).Path = executorPath;
                definition.Actions.Item(1).Arguments = expectedArguments;
                folder.RegisterTaskDefinition(info.Name, definition, 6, "SYSTEM", null, 5, null);
                updated++;
            }
            return updated;
        }
    }
}
