using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ServiceScheduler
{
    public static class SmokeTests
    {
        private static readonly List<string> Created = new List<string>();
        private static int assertions;

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception("ПРОВАЛ: " + message);
            Console.WriteLine("OK: " + message);
        }

        private static TaskInfo Find(string path)
        {
            return Scheduler.ListTasks(true).FirstOrDefault(t => string.Equals(t.Path, path,
                StringComparison.OrdinalIgnoreCase));
        }

        private static string Add(ScheduleSpec schedule, string executor,
            ServiceOperation operation = ServiceOperation.Restart)
        {
            string path = Scheduler.SaveTask(null, "Spooler", operation, schedule, true,
                false, executor);
            Created.Add(path);
            TaskInfo info = Find(path);
            Check(info != null && info.IsOwn && !info.IsEnabled,
                "создано отключённое своё задание " + path);
            return path;
        }

        public static int Main()
        {
            try
            {
                List<ServiceInfo> services = ServiceOperations.GetServices();
                Check(services.Count > 0, "список служб прочитан: " + services.Count);
                ServiceInfo dcom = services.FirstOrDefault(s => s.Name == "DcomLaunch");
                if (dcom != null) Check(dcom.IsSharedProcess, "DcomLaunch распознан как общий процесс");
                if (dcom != null) Check(dcom.IsWindowsService,
                    "стандартная служба Windows распознана по пути исполняемого файла");
                Check(ServiceOperations.IsWindowsServicePath(
                    @"%SystemRoot%\System32\svchost.exe -k LocalService") &&
                    ServiceOperations.IsWindowsServicePath(@"system32\drivers\example.sys") &&
                    !ServiceOperations.IsWindowsServicePath(
                        @"C:\Program Files\Example\service.exe") &&
                    !ServiceOperations.IsWindowsServicePath(null),
                    "фильтр стандартных служб корректно разбирает пути");

                string legacyService;
                ServiceOperation legacyOperation;
                Check(Scheduler.TryParseLegacyName("SvcMgr_Spooler_hard_restart",
                    out legacyService, out legacyOperation) && legacyService == "Spooler" &&
                    legacyOperation == ServiceOperation.HardRestart,
                    "старое имя hard_restart разобрано правильно");
                Check(Scheduler.TryParseLegacyName("SvcMgr_Spooler_hard_stop",
                    out legacyService, out legacyOperation) &&
                    legacyOperation == ServiceOperation.HardStop,
                    "старое имя hard_stop разобрано правильно");
                ServiceOperation parsedOperation;
                Check(OperationInfo.TryFromCode("hard_stop", out parsedOperation) &&
                    parsedOperation == ServiceOperation.HardStop &&
                    OperationInfo.ToCode(parsedOperation) == "hard_stop" &&
                    OperationInfo.UsesKill(parsedOperation),
                    "жёсткая остановка имеет отдельный код операции");

                string executor = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "ServiceScheduler.exe");
                Check(File.Exists(executor), "тестовый исполнитель находится в workspace");
                int before = Scheduler.ListTasks(true).Count;

                ScheduleSpec daily = new ScheduleSpec
                {
                    Type = ScheduleType.Daily,
                    Time = new TimeSpan(23, 58, 0)
                };
                string first = Add(daily, executor);
                string second = Add(daily, executor);
                Check(!string.Equals(first, second, StringComparison.OrdinalIgnoreCase),
                    "два одинаковых расписания получили разные имена");
                TaskInfo firstInfo = Find(first);
                Check(firstInfo != null && firstInfo.Name.IndexOf("Spooler",
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !firstInfo.Name.StartsWith("SvcMgr_", StringComparison.OrdinalIgnoreCase),
                    "новое задание получило понятное имя");
                Check(firstInfo.ActionArguments.IndexOf("--task", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    firstInfo.ActionArguments.IndexOf(first, StringComparison.OrdinalIgnoreCase) >= 0,
                    "действие содержит путь задания для журнала");
                TaskInfo allViewInfo = Scheduler.ListTasks(false).FirstOrDefault(t =>
                    string.Equals(t.Path, first, StringComparison.OrdinalIgnoreCase));
                Check(allViewInfo != null && allViewInfo.IsOwn,
                    "своё задание распознаётся и в общем списке планировщика");
                Check(Scheduler.ListTasks(true).Count == before + 2,
                    "в списке ровно два новых задания");

                string firstName = first.Substring(Scheduler.FolderPath.Length);
                ScheduleSpec weekly = new ScheduleSpec
                {
                    Type = ScheduleType.Weekly,
                    Time = new TimeSpan(22, 57, 0),
                    DaysOfWeekMask = 2 | 32
                };
                string edited = Scheduler.SaveTask(firstName, "Spooler", ServiceOperation.Stop,
                    weekly, true, false, executor);
                Check(string.Equals(first, edited, StringComparison.OrdinalIgnoreCase),
                    "редактирование сохранило исходное имя");
                Check(Scheduler.ListTasks(true).Count == before + 2,
                    "редактирование не создало дубликат");
                TaskInfo editedInfo = Find(first);
                Check(editedInfo != null && editedInfo.Operation == ServiceOperation.Stop &&
                    editedInfo.Schedule.Type == ScheduleType.Weekly &&
                    editedInfo.Schedule.DaysOfWeekMask == (2 | 32) &&
                    editedInfo.Schedule.Time == weekly.Time,
                    "операция, дни недели и время после редактирования корректны");

                ScheduleSpec monthly = new ScheduleSpec
                {
                    Type = ScheduleType.Monthly,
                    Time = new TimeSpan(21, 56, 0),
                    DayOfMonth = 15
                };
                string monthlyPath = Add(monthly, executor, ServiceOperation.Start);
                TaskInfo monthlyInfo = Find(monthlyPath);
                Check(monthlyInfo.Schedule.Type == ScheduleType.Monthly &&
                    monthlyInfo.Schedule.DayOfMonth == 15 &&
                    monthlyInfo.Schedule.Time == monthly.Time,
                    "ежемесячное расписание прочитано корректно");

                ScheduleSpec once = new ScheduleSpec
                {
                    Type = ScheduleType.Once,
                    Date = DateTime.Today.AddDays(2),
                    Time = new TimeSpan(20, 55, 0)
                };
                string oncePath = Add(once, executor, ServiceOperation.Stop);
                TaskInfo onceInfo = Find(oncePath);
                Check(onceInfo.Schedule.Type == ScheduleType.Once &&
                    onceInfo.Schedule.Date == once.Date && onceInfo.Schedule.Time == once.Time,
                    "однократное расписание прочитано корректно");

                Scheduler.SetEnabled(oncePath, true);
                Check(Find(oncePath).IsEnabled, "задание включается атомарно");
                Scheduler.SetEnabled(oncePath, false);
                Check(!Find(oncePath).IsEnabled, "задание отключается атомарно");

                string startupPath = Add(new ScheduleSpec { Type = ScheduleType.Startup },
                    executor, ServiceOperation.Start);
                Check(Find(startupPath).Schedule.Type == ScheduleType.Startup,
                    "расписание при запуске системы прочитано корректно");

                ScheduleSpec interval = new ScheduleSpec
                {
                    Type = ScheduleType.Interval,
                    Time = new TimeSpan(23, 54, 0),
                    IntervalMinutes = 7
                };
                string intervalPath = Add(interval, executor, ServiceOperation.HardRestart);
                TaskInfo intervalInfo = Find(intervalPath);
                Check(intervalInfo.Schedule.Type == ScheduleType.Interval &&
                    intervalInfo.Schedule.IntervalMinutes == 7 &&
                    intervalInfo.Operation == ServiceOperation.HardRestart &&
                    !intervalInfo.AllowSharedKill,
                    "интервал и операция hard_restart прочитаны корректно");

                string hardStopPath = Add(daily, executor, ServiceOperation.HardStop);
                Check(Find(hardStopPath).Operation == ServiceOperation.HardStop,
                    "жёсткая остановка сохраняется и читается как отдельная операция");
                string logMarker = "Автотест журнала " + Guid.NewGuid().ToString("N");
                Logger.Info(logMarker, hardStopPath, "Spooler");
                LogEntryInfo logEntry = Logger.ReadEntries(30).FirstOrDefault(e =>
                    (e.Message ?? string.Empty).IndexOf(logMarker, StringComparison.Ordinal) >= 0);
                Check(logEntry != null && logEntry.TaskPath == hardStopPath &&
                    logEntry.ServiceName == "Spooler",
                    "журнал Windows связывает запись со службой и заданием");

                string legacyName = "SvcMgr_Spooler_" + Guid.NewGuid().ToString("N")
                    .Substring(0, 8) + "_hard_restart";
                string legacyPath = Scheduler.SaveTask(legacyName, "Spooler",
                    ServiceOperation.HardRestart, daily, true, false, executor);
                Created.Add(legacyPath);
                dynamic service = Activator.CreateInstance(
                    Type.GetTypeFromProgID("Schedule.Service"));
                service.Connect();
                dynamic folder = service.GetFolder("\\ServiceManager");
                dynamic registered = folder.GetTask(legacyName);
                dynamic legacyDefinition = registered.Definition;
                legacyDefinition.Actions.Item(1).Path = Path.Combine(
                    Environment.SystemDirectory, "WindowsPowerShell\\v1.0\\powershell.exe");
                legacyDefinition.Actions.Item(1).Arguments =
                    "-NoProfile -Command \"old wrapper --exec Spooler hard_restart\"";
                folder.RegisterTaskDefinition(legacyName, legacyDefinition, 6,
                    "SYSTEM", null, 5, null);
                Check(ExecutorDeployer.NeedsMigration(Find(legacyPath)),
                    "старый PowerShell-исполнитель требует миграции");
                int migrated = Scheduler.UpdateExecutorForOwnTasks(executor, legacyPath);
                TaskInfo migratedInfo = Find(legacyPath);
                string expectedArguments = Scheduler.BuildExecutorArguments("Spooler",
                    ServiceOperation.HardRestart, false, legacyPath);
                Check(migrated == 1 && migratedInfo.ActionExecutable == executor &&
                    migratedInfo.ActionArguments == expectedArguments,
                    "миграция заменяет и путь, и старые аргументы");
                Check(Scheduler.UpdateExecutorForOwnTasks(executor, legacyPath) == 0,
                    "повторная миграция не перезаписывает исправное задание");

                TaskInfo foreign = Scheduler.ListTasks(false).FirstOrDefault(t => !t.IsOwn);
                if (foreign != null)
                {
                    bool blocked = false;
                    try { Scheduler.DeleteTask(foreign.Path); }
                    catch (ServiceSchedulerException) { blocked = true; }
                    Check(blocked, "удаление чужого системного задания заблокировано");
                    Check(Find(foreign.Path) == null || !foreign.IsOwn,
                        "чужое задание не отмечено как своё");
                }

                Scheduler.DeleteTask(second);
                Created.Remove(second);
                Check(Find(second) == null, "своё тестовое задание удаляется");
                Check(Scheduler.ListTasks(false).Count > 0,
                    "общий список задач планировщика читается");
                Console.WriteLine("ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ: " + assertions);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }
            finally
            {
                foreach (string path in Created.ToArray())
                {
                    try
                    {
                        Scheduler.DeleteTask(path);
                        Console.WriteLine("CLEANUP: " + path);
                    }
                    catch (Exception ex) { Console.WriteLine("CLEANUP ERROR: " + path + " " + ex.Message); }
                }
            }
        }
    }
}
