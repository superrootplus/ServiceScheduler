using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ServiceScheduler
{
    public static class Program
    {
        public const string Version = "2.0";
        private static Mutex guiMutex;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length >= 3 && string.Equals(args[0], "--exec",
                StringComparison.OrdinalIgnoreCase))
                return RunExecutor(args);

            if (!IsAdministrator())
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Assembly.GetExecutingAssembly().Location,
                        Verb = "runas",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Для работы программы нужны права администратора.\n" +
                        ex.Message, "Планировщик служб Windows", MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return 1;
            }

            bool created;
            guiMutex = new Mutex(true, "Local\\ServiceScheduler_WPF_GUI", out created);
            if (!created)
            {
                MessageBox.Show("Программа уже запущена.", "Планировщик служб Windows",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }

            try
            {
                App application = new App();
                application.ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow window = new MainWindow();
                application.Run(window);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("Критическая ошибка GUI: " + ex);
                MessageBox.Show("Критическая ошибка:\n" + ex.Message,
                    "Планировщик служб Windows", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            finally
            {
                try { guiMutex.ReleaseMutex(); } catch { }
                guiMutex.Dispose();
            }
        }

        private static int RunExecutor(string[] args)
        {
            string serviceName = args[1];
            ServiceOperation operation;
            if (!OperationInfo.TryFromCode(args[2], out operation))
            {
                Logger.Error("Неизвестная операция в --exec: " + args[2]);
                return 2;
            }
            bool allowSharedKill = false;
            foreach (string arg in args)
                if (string.Equals(arg, "--allow-shared-kill",
                    StringComparison.OrdinalIgnoreCase)) allowSharedKill = true;

            Logger.Info("Задание запущено: служба=" + serviceName + ", операция=" +
                OperationInfo.ToCode(operation));
            try
            {
                OperationResult result = ServiceOperations.Run(serviceName, operation,
                    allowSharedKill);
                string text = (result.Success ? "УСПЕХ" : "ОШИБКА") + ": служба=" +
                    serviceName + ", операция=" + OperationInfo.ToRussian(operation) +
                    " — " + result.Message;
                if (result.Success) Logger.Success(text);
                else Logger.Error(text);
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Logger.Error("Внутренняя ошибка исполнителя: служба=" + serviceName +
                    " — " + ex);
                return 1;
            }
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }

    public sealed class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("Необработанная ошибка GUI: " + e.Exception);
            MessageBox.Show("Произошла ошибка:\n" + e.Exception.Message,
                "Планировщик служб Windows", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
