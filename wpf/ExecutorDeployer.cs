using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace ServiceScheduler
{
    public static class ExecutorDeployer
    {
        public static string ProtectedPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                    "ServiceScheduler", "ServiceScheduler.exe");
            }
        }

        public static bool NeedsMigration(TaskInfo task)
        {
            if (task == null || !task.IsOwn || !task.Operation.HasValue) return false;
            string executable = (task.ActionExecutable ?? string.Empty).Trim().Trim('"');
            bool wrongPath = !string.Equals(
                Path.GetFullPath(executable).TrimEnd('\\'),
                Path.GetFullPath(ProtectedPath).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
            string expectedArguments = Scheduler.BuildExecutorArguments(task.ServiceName,
                task.Operation.Value, task.AllowSharedKill, task.Path);
            bool wrongArguments = !string.Equals((task.ActionArguments ?? string.Empty).Trim(),
                expectedArguments, StringComparison.Ordinal);
            return wrongPath || wrongArguments;
        }

        public static string EnsureProtectedExecutor()
        {
            string source = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string target = ProtectedPath;
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target),
                StringComparison.OrdinalIgnoreCase))
                return target;

            string directory = Path.GetDirectoryName(target);
            Directory.CreateDirectory(directory);
            SecureDirectory(directory);

            if (File.Exists(target) && FilesEqual(source, target))
            {
                SecureFile(target);
                return target;
            }

            string temporary = target + ".new";
            try
            {
                File.Copy(source, temporary, true);
                if (File.Exists(target))
                {
                    string backup = target + ".old";
                    File.Copy(target, backup, true);
                    try
                    {
                        File.Copy(temporary, target, true);
                        File.Delete(backup);
                    }
                    catch
                    {
                        File.Copy(backup, target, true);
                        throw;
                    }
                }
                else
                {
                    File.Move(temporary, target);
                }
                SecureFile(target);
                return target;
            }
            catch (Exception ex)
            {
                throw new ServiceSchedulerException(
                    "Не удалось подготовить защищённый исполнитель заданий:\n" +
                    target + "\n\n" + ex.Message, ex);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        private static bool FilesEqual(string first, string second)
        {
            FileInfo a = new FileInfo(first);
            FileInfo b = new FileInfo(second);
            if (a.Length != b.Length) return false;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hashA;
                byte[] hashB;
                using (FileStream stream = File.OpenRead(first))
                    hashA = sha.ComputeHash(stream);
                using (FileStream stream = File.OpenRead(second))
                    hashB = sha.ComputeHash(stream);
                if (hashA.Length != hashB.Length) return false;
                for (int i = 0; i < hashA.Length; i++)
                    if (hashA[i] != hashB[i]) return false;
                return true;
            }
        }

        private static void SecureDirectory(string path)
        {
            DirectorySecurity security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(path, security);
        }

        private static void SecureFile(string path)
        {
            FileSecurity security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));
            File.SetAccessControl(path, security);
        }
    }
}
