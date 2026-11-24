using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;

namespace ServiceScheduler
{
    public static class ServiceOperations
    {
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);

        private sealed class WmiService
        {
            public string Name;
            public string DisplayName;
            public uint ProcessId;
            public string PathName;
        }

        public static List<ServiceInfo> GetServices()
        {
            List<ServiceInfo> result = new List<ServiceInfo>();
            Dictionary<string, WmiService> wmiByName =
                new Dictionary<string, WmiService>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (WmiService service in GetWmiServices())
                    if (!string.IsNullOrWhiteSpace(service.Name)) wmiByName[service.Name] = service;
            }
            catch
            {
                // Список служб остаётся доступным и без WMI. Неизвестное происхождение
                // не считаем стандартным, чтобы фильтр случайно ничего не скрыл.
            }

            foreach (ServiceController controller in ServiceController.GetServices())
            {
                using (controller)
                {
                    ServiceInfo info = new ServiceInfo
                    {
                        Name = controller.ServiceName,
                        DisplayName = string.IsNullOrWhiteSpace(controller.DisplayName)
                            ? controller.ServiceName : controller.DisplayName,
                        IsRunning = controller.Status == ServiceControllerStatus.Running,
                        Status = StatusToRussian(controller.Status),
                        ProcessTypeKnown = true
                    };
                    WmiService wmi;
                    if (wmiByName.TryGetValue(info.Name, out wmi))
                        info.IsWindowsService = IsWindowsServicePath(wmi.PathName);
                    try
                    {
                        info.IsSharedProcess =
                            (controller.ServiceType & ServiceType.Win32ShareProcess) != 0;
                    }
                    catch
                    {
                        // Если тип не удалось определить, для kill считаем службу общей.
                        info.ProcessTypeKnown = false;
                        info.IsSharedProcess = true;
                    }
                    result.Add(info);
                }
            }
            return result.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static bool IsWindowsServicePath(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return false;
            string path = Environment.ExpandEnvironmentVariables(imagePath.Trim());
            if (path.StartsWith(@"\\??\", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(4);
            if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("System32\\", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("SysWOW64\\", StringComparison.OrdinalIgnoreCase)) return true;

            string windowsDirectory = Environment.GetEnvironmentVariable("SystemRoot");
            if (string.IsNullOrWhiteSpace(windowsDirectory)) return false;
            windowsDirectory = windowsDirectory.TrimEnd('\\');
            return path.StartsWith(windowsDirectory + "\\", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, windowsDirectory, StringComparison.OrdinalIgnoreCase);
        }

        public static ServiceController OpenService(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ServiceSchedulerException("Не указано имя службы.");
            ServiceController controller = new ServiceController(serviceName);
            try
            {
                ServiceControllerStatus state = controller.Status;
                return controller;
            }
            catch (Exception ex)
            {
                controller.Dispose();
                throw new ServiceSchedulerException(
                    "Служба не найдена или недоступна: " + serviceName, ex);
            }
        }

        public static string StatusToRussian(ServiceControllerStatus status)
        {
            switch (status)
            {
                case ServiceControllerStatus.Running: return "Работает";
                case ServiceControllerStatus.Stopped: return "Остановлена";
                case ServiceControllerStatus.StartPending: return "Запускается";
                case ServiceControllerStatus.StopPending: return "Останавливается";
                case ServiceControllerStatus.Paused: return "Приостановлена";
                case ServiceControllerStatus.PausePending: return "Приостанавливается";
                case ServiceControllerStatus.ContinuePending: return "Возобновляется";
                default: return status.ToString();
            }
        }

        private static List<WmiService> GetWmiServices()
        {
            List<WmiService> result = new List<WmiService>();
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT Name, DisplayName, ProcessId, PathName FROM Win32_Service"))
            using (ManagementObjectCollection items = searcher.Get())
            {
                foreach (ManagementObject item in items)
                {
                    using (item)
                    {
                        result.Add(new WmiService
                        {
                            Name = Convert.ToString(item["Name"]),
                            DisplayName = Convert.ToString(item["DisplayName"]),
                            ProcessId = Convert.ToUInt32(item["ProcessId"]),
                            PathName = Convert.ToString(item["PathName"])
                        });
                    }
                }
            }
            return result;
        }

        public static int GetProcessId(string serviceName)
        {
            WmiService service = GetWmiServices().FirstOrDefault(
                s => string.Equals(s.Name, serviceName, StringComparison.OrdinalIgnoreCase));
            if (service == null)
                throw new ServiceSchedulerException("Не удалось получить PID службы " + serviceName);
            return service.ProcessId > int.MaxValue ? 0 : (int)service.ProcessId;
        }

        public static List<string> GetServicesOnSameProcess(string serviceName)
        {
            List<WmiService> services = GetWmiServices();
            WmiService target = services.FirstOrDefault(
                s => string.Equals(s.Name, serviceName, StringComparison.OrdinalIgnoreCase));
            if (target == null || target.ProcessId == 0)
                return new List<string>();
            return services.Where(s => s.ProcessId == target.ProcessId)
                .Select(s => string.IsNullOrWhiteSpace(s.DisplayName) ? s.Name : s.DisplayName)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static bool IsSharedProcess(ServiceController controller)
        {
            try
            {
                controller.Refresh();
                return (controller.ServiceType & ServiceType.Win32ShareProcess) != 0;
            }
            catch
            {
                return true; // Неизвестный тип — безопаснее считать общим.
            }
        }

        private static List<string> GetRunningDependents(ServiceController controller)
        {
            List<string> result = new List<string>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectRunningDependents(controller, result, visited, 0);
            return result;
        }

        private static void CollectRunningDependents(ServiceController controller,
            List<string> result, HashSet<string> visited, int depth)
        {
            if (depth > 12) return;
            try
            {
                foreach (ServiceController dependent in controller.DependentServices)
                {
                    using (dependent)
                    {
                        if (!visited.Add(dependent.ServiceName) ||
                            dependent.Status == ServiceControllerStatus.Stopped) continue;
                        // Родитель идёт раньше зависимого: восстанавливать будем в том же порядке.
                        result.Add(dependent.ServiceName);
                        CollectRunningDependents(dependent, result, visited, depth + 1);
                    }
                }
            }
            catch { }
        }

        private static OperationResult StartInternal(ServiceController controller)
        {
            try
            {
                controller.Refresh();
                if (controller.Status == ServiceControllerStatus.Running)
                    return new OperationResult(true, "Служба уже запущена");
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, StartTimeout);
                return new OperationResult(true, "Служба запущена");
            }
            catch (Exception ex)
            {
                return new OperationResult(false, "Не удалось запустить службу: " + ex.Message);
            }
        }

        private static OperationResult StopInternal(ServiceController controller, TimeSpan timeout)
        {
            return StopInternal(controller, timeout, 0);
        }

        private static OperationResult StopInternal(ServiceController controller,
            TimeSpan timeout, int depth)
        {
            if (depth > 12)
                return new OperationResult(false, "Слишком глубокое дерево зависимых служб");
            try
            {
                controller.Refresh();
                if (controller.Status == ServiceControllerStatus.Stopped)
                    return new OperationResult(true, "Служба уже остановлена");

                // В .NET 4.0 нет Stop(bool): останавливаем зависимые службы сами,
                // затем целевую. При перезапуске ранее работавшие будут восстановлены.
                foreach (ServiceController dependent in controller.DependentServices)
                {
                    using (dependent)
                    {
                        if (dependent.Status != ServiceControllerStatus.Stopped)
                        {
                            OperationResult child = StopInternal(dependent, timeout, depth + 1);
                            if (!child.Success)
                                return new OperationResult(false,
                                    "Не удалось остановить зависимую службу " +
                                    dependent.ServiceName + ": " + child.Message);
                        }
                    }
                }
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
                return new OperationResult(true, "Служба остановлена");
            }
            catch (Exception ex)
            {
                return new OperationResult(false, "Не удалось остановить службу: " + ex.Message);
            }
        }

        private static string StartDependents(List<string> dependents)
        {
            List<string> errors = new List<string>();
            foreach (string name in dependents)
            {
                try
                {
                    using (ServiceController dependent = OpenService(name))
                    {
                        if (dependent.Status != ServiceControllerStatus.Running)
                        {
                            dependent.Start();
                            dependent.WaitForStatus(ServiceControllerStatus.Running,
                                TimeSpan.FromSeconds(30));
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(name + ": " + ex.Message);
                }
            }
            return errors.Count == 0 ? string.Empty :
                "Ошибки запуска зависимых служб: " + string.Join("; ", errors.ToArray());
        }

        private static string DependentsNote(List<string> dependents)
        {
            return dependents.Count == 0 ? string.Empty :
                string.Format(" Зависимые службы ({0}) перезапущены.", dependents.Count);
        }

        public static OperationResult Run(string serviceName, ServiceOperation operation,
            bool allowSharedKill)
        {
            using (ServiceController controller = OpenService(serviceName))
            {
                switch (operation)
                {
                    case ServiceOperation.Start:
                        return StartInternal(controller);
                    case ServiceOperation.Stop:
                        return StopInternal(controller, StopTimeout);
                    case ServiceOperation.Restart:
                        return RestartInternal(controller, false, allowSharedKill);
                    case ServiceOperation.HardRestart:
                        return RestartInternal(controller, true, allowSharedKill);
                    case ServiceOperation.HardStop:
                        return HardStopInternal(controller, allowSharedKill);
                    default:
                        return new OperationResult(false, "Неизвестная операция");
                }
            }
        }

        private static OperationResult HardStopInternal(ServiceController controller,
            bool allowSharedKill)
        {
            controller.Refresh();
            if (controller.Status == ServiceControllerStatus.Stopped)
                return new OperationResult(true, "Служба уже остановлена");

            bool shared = IsSharedProcess(controller);
            List<string> sameProcess = shared ?
                GetServicesOnSameProcess(controller.ServiceName) : new List<string>();
            if (shared && !allowSharedKill)
                return new OperationResult(false,
                    "Принудительный kill запрещён: процесс является общим или его тип " +
                    "не удалось определить. Затронутые службы: " +
                    string.Join(", ", sameProcess.ToArray()));

            int pid = GetProcessId(controller.ServiceName);
            if (pid <= 0)
            {
                controller.Refresh();
                if (controller.Status == ServiceControllerStatus.Stopped)
                    return new OperationResult(true, "Служба уже остановлена");
                return new OperationResult(false,
                    "Не удалось получить PID работающей службы для taskkill");
            }

            if (shared)
                Logger.Warning("Разрешён kill общего процесса PID " + pid +
                    ". Службы процесса: " + string.Join(", ", sameProcess.ToArray()));
            OperationResult kill = KillProcess(pid);
            if (!kill.Success) return kill;
            try
            {
                controller.WaitForStatus(ServiceControllerStatus.Stopped,
                    TimeSpan.FromSeconds(10));
                return new OperationResult(true, kill.Message + "; служба остановлена");
            }
            catch (Exception ex)
            {
                return new OperationResult(false,
                    "Процесс завершён, но служба не перешла в остановленное состояние: " +
                    ex.Message);
            }
        }

        private static OperationResult RestartInternal(ServiceController controller,
            bool hard, bool allowSharedKill)
        {
            List<string> messages = new List<string>();
            List<string> dependents = GetRunningDependents(controller);
            controller.Refresh();
            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                OperationResult startStopped = StartInternal(controller);
                return new OperationResult(startStopped.Success, startStopped.Message);
            }

            OperationResult stop = StopInternal(controller, StopTimeout);
            if (stop.Success)
            {
                messages.Add("Служба остановлена штатно");
            }
            else if (!hard)
            {
                return new OperationResult(false, stop.Message);
            }
            else
            {
                bool shared = IsSharedProcess(controller);
                List<string> sameProcess = shared ? GetServicesOnSameProcess(controller.ServiceName)
                    : new List<string>();
                if (shared && !allowSharedKill)
                {
                    return new OperationResult(false,
                        "Служба не остановилась штатно. Принудительный kill запрещён: " +
                        "процесс является общим или его тип не удалось определить. " +
                        "Затронутые службы: " + string.Join(", ", sameProcess.ToArray()));
                }

                int pid = GetProcessId(controller.ServiceName);
                if (pid <= 0)
                {
                    controller.Refresh();
                    if (controller.Status != ServiceControllerStatus.Stopped)
                        return new OperationResult(false,
                            "Не удалось получить PID работающей службы для taskkill");
                }
                else
                {
                    if (shared)
                        Logger.Warning("Разрешён kill общего процесса PID " + pid +
                            ". Службы процесса: " + string.Join(", ", sameProcess.ToArray()));
                    OperationResult kill = KillProcess(pid);
                    messages.Add(kill.Message);
                    if (!kill.Success)
                        return kill;
                    try
                    {
                        controller.WaitForStatus(ServiceControllerStatus.Stopped,
                            TimeSpan.FromSeconds(10));
                    }
                    catch (Exception ex)
                    {
                        return new OperationResult(false,
                            "Процесс завершён, но служба не перешла в остановленное состояние: " +
                            ex.Message);
                    }
                }
            }

            OperationResult start = StartInternal(controller);
            messages.Add(start.Message);
            if (start.Success)
            {
                string dependentErrors = StartDependents(dependents);
                if (!string.IsNullOrEmpty(dependentErrors))
                {
                    messages.Add(dependentErrors);
                    return new OperationResult(false, string.Join("; ", messages.ToArray()));
                }
                messages.Add(DependentsNote(dependents));
            }
            return new OperationResult(start.Success, string.Join("; ", messages.ToArray()));
        }

        private static OperationResult KillProcess(int pid)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/F /PID " + pid,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (!process.WaitForExit(30000))
                    {
                        try { process.Kill(); } catch { }
                        return new OperationResult(false, "taskkill не завершился за 30 секунд");
                    }
                    string output = process.StandardOutput.ReadToEnd() +
                        process.StandardError.ReadToEnd();
                    if (process.ExitCode != 0)
                        return new OperationResult(false,
                            "taskkill завершился с ошибкой: " + output.Trim());
                }
                return new OperationResult(true,
                    string.Format("Процесс службы (PID {0}) принудительно завершён", pid));
            }
            catch (Exception ex)
            {
                return new OperationResult(false, "Ошибка taskkill: " + ex.Message);
            }
        }
    }
}
