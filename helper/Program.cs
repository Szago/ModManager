using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ModManager.FileApplier
{
    internal static class Program
    {
        private const int RetryCount = 80;
        private const int RetryDelayMilliseconds = 250;

        private static int Main(string[] args)
        {
            if (args.Length != 2 || !int.TryParse(args[0], out int processId))
                return 2;

            string pendingPath;
            try { pendingPath = Encoding.UTF8.GetString(Convert.FromBase64String(args[1])); }
            catch { return 2; }

            WaitForProcessExit(processId);
            Thread.Sleep(300);
            if (!File.Exists(pendingPath))
                return 0;

            string[] operations;
            try { operations = File.ReadAllLines(pendingPath, Encoding.UTF8); }
            catch { return 3; }

            bool allApplied = true;
            foreach (string operation in operations)
            {
                if (TryParseOperation(operation, out string source, out string destination))
                    allApplied &= ApplyMove(source, destination);
            }

            if (allApplied)
            {
                try { File.Delete(pendingPath); }
                catch { }
            }

            return allApplied ? 0 : 4;
        }

        private static void WaitForProcessExit(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    process.WaitForExit();
            }
            catch { }
        }

        private static bool ApplyMove(string source, string destination)
        {
            for (int attempt = 0; attempt < RetryCount; attempt++)
            {
                try
                {
                    if (!File.Exists(source))
                        return File.Exists(destination);
                    if (File.Exists(destination))
                        return false;

                    string directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    File.Move(source, destination);
                    return true;
                }
                catch (IOException) { Thread.Sleep(RetryDelayMilliseconds); }
                catch (UnauthorizedAccessException) { Thread.Sleep(RetryDelayMilliseconds); }
            }

            return false;
        }

        private static bool TryParseOperation(string line, out string source, out string destination)
        {
            source = null;
            destination = null;
            string[] parts = line.Split('\t');
            if (parts.Length != 2)
                return false;

            try
            {
                source = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                destination = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                return !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(destination);
            }
            catch { return false; }
        }
    }
}
