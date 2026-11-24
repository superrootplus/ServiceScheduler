using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ServiceScheduler
{
    public static class WindowsEditors
    {
        private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan PropertiesTimeout = TimeSpan.FromSeconds(10);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder className,
            int maximum);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text,
            int maximum);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        public static void OpenService(ServiceInfo service)
        {
            if (service == null || string.IsNullOrWhiteSpace(service.Name))
                throw new ServiceSchedulerException("Не указана служба для редактирования.");
            RunSta(delegate { OpenServiceCore(service); });
        }

        private static void RunSta(ThreadStart action)
        {
            Exception error = null;
            Thread thread = new Thread(new ThreadStart(delegate
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            }));
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(60)))
                throw new ServiceSchedulerException(
                    "Оснастка Windows не ответила. Повторите открытие свойств.");
            if (error != null) throw error;
        }

        private static void OpenServiceCore(ServiceInfo service)
        {
            Type type = Type.GetTypeFromProgID("MMC20.Application");
            if (type == null)
                throw new ServiceSchedulerException(
                    "Автоматизация MMC недоступна в этой версии Windows.");
            dynamic application = Activator.CreateInstance(type);
            application.Load("services.msc");
            application.UserControl = 1;
            dynamic view = application.Document.ActiveView;
            string displayName = string.IsNullOrWhiteSpace(service.DisplayName)
                ? service.Name : service.DisplayName;
            dynamic item = WaitForItem(view, displayName);
            if (item == null)
                throw new ServiceSchedulerException(
                    "Оснастка Windows открыта, но служба «" + displayName +
                    "» не найдена. Обновите список и повторите попытку.");
            ShowProperties(view, item, displayName);
        }

        private static dynamic WaitForItem(dynamic view, string exactName)
        {
            DateTime deadline = DateTime.UtcNow.Add(ListTimeout);
            do
            {
                dynamic items = view.ListItems;
                int count = Convert.ToInt32(items.Count);
                for (int index = 1; index <= count; index++)
                {
                    dynamic item = items.Item(index);
                    string name = Convert.ToString(item.Name);
                    string cell = Convert.ToString(view.CellContents(item, 1));
                    if (string.Equals(name, exactName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(cell, exactName, StringComparison.OrdinalIgnoreCase))
                        return item;
                }
                Thread.Sleep(300);
            } while (DateTime.UtcNow < deadline);
            return null;
        }

        private static void ShowProperties(dynamic view, dynamic item, string exactName)
        {
            HashSet<IntPtr> existing = new HashSet<IntPtr>(TopLevelWindows());
            view.Select(item);
            Thread.Sleep(250);
            view.DisplaySelectionPropertySheet();
            IntPtr properties = WaitForProperties(existing, exactName, PropertiesTimeout);
            SetForegroundWindow(properties);
        }

        private static IntPtr[] TopLevelWindows()
        {
            List<IntPtr> result = new List<IntPtr>();
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                result.Add(window);
                return true;
            }, IntPtr.Zero);
            return result.ToArray();
        }

        private static IntPtr WaitForProperties(HashSet<IntPtr> existing, string exactName,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            do
            {
                IntPtr found = IntPtr.Zero;
                EnumWindows(delegate(IntPtr window, IntPtr parameter)
                {
                    if (existing.Contains(window)) return true;
                    StringBuilder className = new StringBuilder(128);
                    GetClassName(window, className, className.Capacity);
                    if (className.ToString() != "#32770") return true;
                    StringBuilder title = new StringBuilder(1024);
                    GetWindowText(window, title, title.Capacity);
                    if (title.ToString().IndexOf(exactName,
                        StringComparison.OrdinalIgnoreCase) < 0) return true;
                    found = window;
                    return false;
                }, IntPtr.Zero);
                if (found != IntPtr.Zero) return found;
                Thread.Sleep(250);
            } while (DateTime.UtcNow < deadline);
            throw new ServiceSchedulerException(
                "Не удалось подтвердить открытие свойств «" + exactName +
                "». Проверьте выбранную строку в открытой оснастке.");
        }
    }
}
