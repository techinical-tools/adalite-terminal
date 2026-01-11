// packages
using Spectre.Console;
// built-in
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

class Program
{
    [DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);


    static void EnableVirtualTerminal()
    {
        const int STD_OUTPUT_HANDLE = -11;
        const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
        GetConsoleMode(handle, out uint mode);
        SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    }

    public static List<string> RequiredMetadataFields = new()
    {
        "name",
        "version2",
        "author",
        "description"
    };

    static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
    {
        DirectoryInfo dir = new DirectoryInfo(sourceDirName);
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException(
                "Source directory does not exist or could not be found: "
                + sourceDirName);
        }
        DirectoryInfo[] dirs = dir.GetDirectories();
        Directory.CreateDirectory(destDirName);
        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string tempPath = Path.Combine(destDirName, file.Name);
            file.CopyTo(tempPath, false);
        }
        if (copySubDirs)
        {
            foreach (DirectoryInfo subdir in dirs)
            {
                string tempPath = Path.Combine(destDirName, subdir.Name);
                DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
            }
        }
    }
    static byte[] RandomBytesGet(int length = 32)
    {
        byte[] bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
    static void RandomByte(int runForInfinity)
    {
        if (runForInfinity == 1)
        {
            while (true)
            {
                byte[] bytes = RandomBytesGet(32);
                Console.WriteLine(Convert.ToHexString(bytes));
            }
        }
        else
        {
            byte[] bytes = RandomBytesGet(32);
            Console.WriteLine(Convert.ToHexString(bytes));
        }
    }
    static void yes(string toPrint)
    {
        while (true)
        {
            Console.WriteLine(toPrint);
        }
    }
    static int ExecuteProgram(string programName, string workingDirectory)
    {
        string programPath;
        try
        {
            programPath = Path.GetFullPath(Path.Combine(workingDirectory, programName));
        }
        catch (Exception)
        {
            Console.WriteLine($"run: invalid file name: {programName}");
            return -1; // error during path resolution
        }

        if (!File.Exists(programPath))
        {
            Console.WriteLine($"run: file not found: {programName}");
            return -1; // file does not exist
        }

        try
        {
            // Handle .NET dlls explicitly (run with `dotnet <path>.dll`)
            string ext = Path.GetExtension(programPath).ToLowerInvariant();
            ProcessStartInfo startInfo;

            if (ext == ".dll")
            {
                // Execute with dotnet host
                startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{programPath}\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = programPath,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                };
            }

            var process = Process.Start(startInfo);
            process?.WaitForExit();
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"run: failed to execute {programName}: {ex.Message}");
            return -1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"run: failed to execute {programName}: {ex.Message}");
            return -1; // error during execution
        }
        return 0; // success
    }

    static int ExecFromPath(string[] argv)
    {
        if (argv == null || argv.Length == 0)
            return 1;

        string cmdName = argv[0];
        string cmdArgs = argv.Length > 1
            ? string.Join(' ', argv.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a))
            : string.Empty;

        // If command contains directory separators or is rooted, try as-is
        if (Path.IsPathRooted(cmdName) || cmdName.Contains(Path.DirectorySeparatorChar) || cmdName.Contains(Path.AltDirectorySeparatorChar))
        {
            try
            {
                string full = Path.GetFullPath(cmdName);
                if (File.Exists(full))
                {
                    try
                    {
                        StartProcessForPath(full, cmdArgs);
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"run: failed to execute {cmdName}: {ex.Message}");
                        return 0;
                    }
                }
            }
            catch
            {
                // fallthrough to PATH search
            }
            return 1;
        }

        // Build list of candidate extensions (Windows uses PATHEXT)
        List<string> exts = new List<string> { string.Empty };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM";
            var parts = pathext.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            exts = new List<string>(parts.Select(p => p.StartsWith(".") ? p : "." + p));
            // ensure we also try the bare name if it already contains an extension
            if (Path.HasExtension(cmdName))
            {
                exts.Insert(0, string.Empty);
            }
        }

        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathDirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs)
        {
            foreach (var ext in exts)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(dir, cmdName + ext);
                }
                catch
                {
                    continue;
                }

                try
                {
                    if (File.Exists(candidate))
                    {
                        try
                        {
                            StartProcessForPath(candidate, cmdArgs);
                            return 0;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"run: failed to execute {cmdName}: {ex.Message}");
                            return 0;
                        }
                    }
                }
                catch
                {
                    // ignore malformed paths
                }
            }
        }

        // Not found on PATH
        return 1;
    }

    // Helper to start a process for a discovered file path.
    // Uses 'dotnet' for .dll, otherwise uses shell execute where appropriate.
    static void StartProcessForPath(string filePath, string args)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        ProcessStartInfo psi;

        if (ext == ".dll")
        {
            psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{filePath}\" {args}".Trim(),
                UseShellExecute = false,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
        }
        else
        {
            // For executables and scripts, let the OS shell handle associations.
            psi = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
        }

        var proc = Process.Start(psi);
        proc?.WaitForExit();
    }

    public static string? GetConfigSetting(string path, string configPath)
    {
        try
        {
            string jsonString = File.ReadAllText(configPath);
            JsonNode? node = JsonNode.Parse(jsonString);

            foreach (string part in path.Split('.'))
            {
                node = node?[part];
                if (node == null)
                    return null;
            }

            return node?.ToString();
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Error parsing JSON: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error reading config: {ex.Message}");
            return null;
        }
    }

    static string ExpandHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (path.StartsWith("~"))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.Substring(1).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return path;
    }


    static void Main(string[] args)
    {
        string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string globalAdleDir = Path.Combine(homeDirectory, ".global-adle");
        InitSessionLog(globalAdleDir);
        EnableVirtualTerminal();

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            Log("FATAL: Unhandled exception");
            Log(e.ExceptionObject?.ToString() ?? "unknown exception");
        };

        Console.Write("\u001b[40m");

        //  ↓ init
        Console.CancelKeyPress += new ConsoleCancelEventHandler(CtrlChandler);

        string version = "1.0.0";
        int currentYear = DateTime.Now.Year;
        bool running = true;

        // useless after bootstrapper puts theses vars in .json config
        string pkgDir = Path.Combine(globalAdleDir, "packages");
        string installDir = Path.Combine(pkgDir, "installed");
        string tempDir = Path.Combine(globalAdleDir, "temporary");
        // useless ^-

        string configPath = Path.Combine(globalAdleDir, "config.json");


        string ?pkgDirN = GetConfigSetting("pkgpath", configPath);
        string ?installDirN = GetConfigSetting("installdir", configPath);
        string ?tempDirN = GetConfigSetting("tempdir", configPath);

        if (pkgDirN == null || installDirN == null || tempDirN == null)
        {
            Console.WriteLine("adle: fatal: config.json is missing required fields");
            Console.WriteLine("adle: run `bootstrap` to regenerate config");
        }

        try
        {
            pkgDirN = ExpandHome(pkgDirN);
            installDirN = ExpandHome(installDirN);
            tempDirN = ExpandHome(tempDirN);
        }
        catch { }

        Directory.SetCurrentDirectory(homeDirectory);
        if (OperatingSystem.IsWindows())
        {
            Console.CursorSize = 100;
        }
        // ^- init
        Console.Write("\u001b[40m");

        Console.WriteLine("Welcome to Adalite Terminal");
        Console.WriteLine($"Copyright (C) 2026-{currentYear} Technical-Tools. All rights Reserved");
        Console.WriteLine("Type 'exit' to quit the terminal. Go to https://github.com/techinical-tools/ and learn more!\n");

        while (running)
        {
            Console.Title = $"Adalite Terminal | Techinical-Tools | Github - {Directory.GetCurrentDirectory()}";
            Console.Write("\u001b[40m");
            string machineName = Environment.MachineName;
            string userName = Environment.UserName;
            string localIdentity = $"{machineName}\\{userName}";
            string currentWorkingDir = Directory.GetCurrentDirectory();

            string displayPath;
            try
            {
                string relative = Path.GetRelativePath(homeDirectory, currentWorkingDir);
                bool isInsideHome = !relative.StartsWith("..") && !Path.IsPathRooted(relative);
                if (isInsideHome)
                {
                    if (string.IsNullOrEmpty(relative) || relative == ".")
                    {
                        displayPath = "~";
                    }
                    else
                    {
                        // Use forward slashes after '~' for a unix-like look: "~/folder/sub"
                        displayPath = "~/" + relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                    }
                }
                else
                {
                    displayPath = currentWorkingDir;
                }
            }
            catch
            {
                displayPath = currentWorkingDir;
            }

            Console.Write($"\u001b[32m[{userName}@{machineName} {displayPath}]\u001b[34m$ ");
            string? input = Console.ReadLine() ?? string.Empty;
            input = input.Trim();

            // Ignore empty input lines
            if (string.IsNullOrEmpty(input))
            {
                continue;
            }

            Console.Write("\u001b[0m");

            // Split input into command and arguments
            string[] arguments = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int numArguments = arguments.Length;
            string cmd = arguments[0];

            if (cmd == "exit")
            {
                running = false;
            }
            if (cmd == "title")
            {
                if (numArguments < 2)
                {
                    Console.WriteLine("title: missing title operand");
                    continue;
                }

                string newTitle = string.Join(' ', arguments.Skip(1));

                Console.Title = newTitle;
            }
            else if (cmd == "clear")
            {
                Console.Clear();
            }
            else if (cmd == "ident")
            {
                Console.WriteLine($"your local identity is: {localIdentity}");
            }
            else if (cmd == "echo")
            {
                Console.WriteLine(numArguments > 1 ? string.Join(' ', arguments.Skip(1)) : string.Empty);
            }
            else if (cmd == "pwd")
            {
                Console.WriteLine(currentWorkingDir);
            }
            else if (cmd == "time")
            {
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss"));
            }
            else if (cmd == "date")
            {
                Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd"));
            }
            else if (cmd == "datetime")
            {
                Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            else if (cmd == "arch")
            {
                Console.WriteLine(RuntimeInformation.OSArchitecture.ToString().ToLower());
            }
            else if (cmd == "uname")
            {
                Console.WriteLine("Linux adalite 6.6.0-arch1-1 #1 SMP PREEMPT_DYNAMIC x86_64 GNU/Linux");
            }
            else if (cmd == "touch")
            {
                string fileName;
                if (numArguments == 1)
                {
                    Console.WriteLine("touch: missing file operand");
                    continue;
                }
                if (numArguments == 2)
                {
                    fileName = arguments[1];
                    if (fileName == "~")
                    {
                        fileName = homeDirectory;
                    }
                    try
                    {
                        fileName = Path.GetFullPath(fileName);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"touch: invalid file name: {arguments[1]}");
                        continue;
                    }
                    try
                    {
                        using (FileStream fs = File.Create(fileName))
                        {
                            // leave empty, no need to show user the output
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.WriteLine("touch: permission denied");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"touch: unexpected error: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("touch: too many arguments");
                    continue;
                }
            }
            else if (cmd == "run")
            {
                // execute any given program in the current working directory i.e run file.exe
                if (numArguments == 1)
                {
                    Console.WriteLine("run: missing file operand executable");
                    continue;
                }
                if (numArguments == 2)
                {
                    // run the given executable
                    ExecuteProgram(arguments[1], currentWorkingDir);
                }
            }
            // Replace your existing 'ls' logic with this:
            else if (cmd == "ls")
            {
                var table = new Table();
                table.AddColumn("[yellow]Name[/]");
                table.AddColumn("[blue]Type[/]");
                table.AddColumn("[green]Size[/]");

                var items = Directory.GetFileSystemEntries(Directory.GetCurrentDirectory());
                foreach (var item in items)
                {
                    var info = new FileInfo(item);
                    bool isDir = (info.Attributes & FileAttributes.Directory) == FileAttributes.Directory;

                    table.AddRow(
                        info.Name,
                        isDir ? "[blue]Directory[/]" : "[grey]File[/]",
                        isDir ? "-" : info.Length.ToString() + " bytes"
                    );
                }
                AnsiConsole.Write(table);
            }
            else if (cmd == "mkdir")
            {
                if (numArguments == 1)
                {
                    Console.WriteLine("mkdir: missing operand");
                    continue;
                }
                if (numArguments > 2)
                {
                    Console.WriteLine("mkdir: too many arguments");
                    continue;
                }

                // Resolve the target path
                string path = arguments[1];
                if (path == "~")
                {
                    path = homeDirectory;
                }

                try
                {
                    path = Path.GetFullPath(path);
                }
                catch (Exception)
                {
                    Console.WriteLine($"mkdir: invalid path: {arguments[1]}");
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("mkdir: permission denied");
                }
                catch (ArgumentException)
                {
                    Console.WriteLine($"mkdir: invalid directory name: {arguments[1]}");
                }
                catch (NotSupportedException)
                {
                    Console.WriteLine($"mkdir: invalid directory name: {arguments[1]}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"mkdir: unexpected error: {ex.Message}");
                }
            }
            else if (cmd == "cd")
            {
                string path;

                if (numArguments == 1)
                {
                    path = homeDirectory;
                }
                else if (numArguments == 2)
                {
                    path = arguments[1];

                    if (path == "~")
                    {
                        path = homeDirectory;
                    }

                    try
                    {
                        path = Path.GetFullPath(path);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"cd: invalid path: {arguments[1]}");
                        continue;
                    }
                }
                else
                {
                    Console.WriteLine("cd: too many arguments");
                    continue;
                }

                try
                {
                    Directory.SetCurrentDirectory(path);
                }
                catch (DirectoryNotFoundException)
                {
                    Console.WriteLine($"cd: no such file or directory: {path}");
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("cd: permission denied");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"cd: unexpected error: {ex.Message}");
                }
            }
            else if (cmd == "rm")
            {
                if (numArguments == 1)
                {
                    Console.WriteLine("rm: missing operand");
                    continue;
                }

                bool recursive = false;
                string target;

                if (arguments[1] == "-r")
                {
                    recursive = true;
                    if (numArguments < 3)
                    {
                        Console.WriteLine("rm: missing operand after -r");
                        continue;
                    }
                    if (numArguments > 3)
                    {
                        Console.WriteLine("rm: too many arguments");
                        continue;
                    }
                    target = arguments[2];
                }
                else
                {
                    if (numArguments > 2)
                    {
                        Console.WriteLine("rm: too many arguments");
                        continue;
                    }
                    target = arguments[1];
                }

                if (target == "~")
                {
                    target = homeDirectory;
                }

                try
                {
                    target = Path.GetFullPath(target);
                }
                catch (Exception)
                {
                    Console.WriteLine($"rm: invalid path: {target}");
                    continue;
                }

                if (File.Exists(target))
                {
                    try
                    {
                        File.Delete(target);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.WriteLine("rm: permission denied");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"rm: unexpected error: {ex.Message}");
                    }
                }
                else if (Directory.Exists(target))
                {
                    try
                    {
                        if (recursive)
                        {
                            Directory.Delete(target, recursive: true);
                        }
                        else
                        {
                            if (Directory.EnumerateFileSystemEntries(target).Any())
                            {
                                Console.WriteLine($"rm: directory not empty: {target}");
                            }
                            else
                            {
                                Directory.Delete(target);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.WriteLine("rm: permission denied");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"rm: unexpected error: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"rm: no such file or directory: {target}");
                }
            }
            else if (cmd == "reboot")
            {
                // reboot the system
                // NOTE: this is cross-platform compatible
                try
                {
                    var process = new System.Diagnostics.Process();
                    if (OperatingSystem.IsWindows()) // checks which os it is running
                    {
                        process.StartInfo.FileName = "shutdown";
                        process.StartInfo.Arguments = "/r /t 0";
                    }
                    else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    {
                        process.StartInfo.FileName = "sudo";
                        process.StartInfo.Arguments = "reboot";
                    }
                    else
                    {
                        Console.WriteLine("reboot: unsupported operating system");
                        continue; // inform the user about unsupported os
                    }
                    process.StartInfo.UseShellExecute = false;
                    process.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"reboot: failed to reboot the system: {ex.Message}");
                }
            }
            else if (cmd == "yes")
            {
                string stringToPrint;
                if (numArguments > 2 || numArguments < 1)
                {
                    Console.WriteLine("yes: too many arguments");
                    continue;
                }
                if (numArguments == 2)
                {
                    stringToPrint = arguments[1];
                }
                else
                {
                    stringToPrint = "y";
                }
                yes(stringToPrint);
            }
            else if (cmd == "random-bytes")
            {
                int runforinfinite;

                if (numArguments == 2)
                {
                    runforinfinite = 1;
                }
                else
                {
                    runforinfinite = 0;
                }

                RandomByte(runforinfinite);
            }
            else if (cmd == "ps")
            {
                // shows process list
                /*
                    Process Name        PID
                    process.exe         1337
                */
                Console.WriteLine("{0,-30} {1,6}", "Process Name", "PID");
                Console.WriteLine(new string('-', 38));

                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        Console.WriteLine("{0,-30} {1,6}",
                            process.ProcessName,
                            process.Id);
                    }
                    catch
                    {
                        // Some system processes deny access — safely ignore
                    }
                }
            }
            else if (cmd == "kill")
            {
                if (numArguments > 2)
                {
                    Console.WriteLine("kill: too many arguments");
                    continue;
                }

                if (numArguments < 2)
                {
                    Console.WriteLine("kill: process operand not provided");
                    continue;
                }

                string target = arguments[1];

                // Try PID first
                if (int.TryParse(target, out int pid))
                {
                    try
                    {
                        Process process = Process.GetProcessById(pid);
                        process.Kill();
                        Console.WriteLine($"Killed process {pid}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"kill: {ex.Message}");
                    }
                }
                else
                {
                    // Kill by name
                    Process[] processes = Process.GetProcessesByName(target);

                    if (processes.Length == 0)
                    {
                        Console.WriteLine($"kill: no process named '{target}'");
                        continue;
                    }

                    foreach (Process process in processes)
                    {
                        try
                        {
                            process.Kill();
                            Console.WriteLine($"Killed {process.ProcessName} ({process.Id})");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"kill: {process.ProcessName} ({process.Id}): {ex.Message}");
                        }
                    }
                }
            }
            else if (cmd == "cat")
            {
                // opens a file and displays its content even if it is a .bin file
                if (numArguments != 2)
                {
                    Console.WriteLine("cat: invalid number of arguments");
                    continue;
                }

                string filePath = arguments[1];

                if (filePath == "~")
                {
                    filePath = homeDirectory;
                }

                try
                {
                    filePath = Path.GetFullPath(filePath);
                }
                catch (Exception)
                {
                    Console.WriteLine($"cat: invalid file name: {arguments[1]}");
                    continue;
                }

                try
                {
                    using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    using (StreamReader reader = new StreamReader(fs))
                    {
                        while (!reader.EndOfStream)
                        {
                            Console.WriteLine(reader.ReadLine());
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine($"cat: file not found: {filePath}");
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("cat: permission denied");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"cat: unexpected error: {ex.Message}");
                }
            }
            else if (cmd == "cp")
            {
                if (numArguments < 3)
                {
                    Console.WriteLine("adalite: cp: missing file operand");
                    continue;
                }

                string sourcePath = arguments[1];
                string destinationPath = arguments[2];

                // Expand ~ for both paths if they start with it
                if (sourcePath.StartsWith("~"))
                    sourcePath = sourcePath.Replace("~", homeDirectory);
                if (destinationPath.StartsWith("~"))
                    destinationPath = destinationPath.Replace("~", homeDirectory);

                try
                {
                    string finalDest = destinationPath;

                    // The Directory Trap Fix:
                    // If destination is a folder, we append the source's filename to the path
                    if (Directory.Exists(destinationPath))
                    {
                        string fileName = Path.GetFileName(sourcePath);
                        finalDest = Path.Combine(destinationPath, fileName);
                    }

                    File.Copy(sourcePath, finalDest, true);
                }
                catch (Exception ex)
                {
                    // Prevents the terminal from crashing if the file is missing or locked
                    Console.WriteLine($"adalite: cp: {ex.Message}");
                }
            }
            else if (cmd == "setenv")
            {
                // sets an environment variable like "setenv VAR_NAME VAR_VALUE"
                if (numArguments != 3)
                {
                    Console.WriteLine("setenv: invalid number of arguments");
                    continue;
                }

                string varName = arguments[1];
                string varValue = arguments[2];

                try
                {
                    Environment.SetEnvironmentVariable(varName, varValue);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"setenv: unexpected error: {ex.Message}");
                }
            }
            else if (cmd == "getenv")
            {
                // gets an environment variable like "getenv VAR_NAME"
                if (numArguments != 2)
                {
                    Console.WriteLine("getenv: invalid number of arguments");
                    continue;
                }
                string varName = arguments[1];
                try
                {
                    string? varValue = Environment.GetEnvironmentVariable(varName);
                    if (varValue != null)
                    {
                        Console.WriteLine(varValue);
                    }
                    else
                    {
                        Console.WriteLine($"getenv: variable '{varName}' not found");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"getenv: unexpected error: {ex.Message}");
                }
            }
            else if (cmd == "get")
            {
                // 'get' command: download a URL to the current working directory or to an optional destination path
                if (numArguments < 2 || numArguments > 3)
                {
                    Console.WriteLine("get: usage: get <url> [destination]");
                    continue;
                }

                string url = arguments[1];
                string destinationPath;

                try
                {
                    // Determine filename from URL
                    string urlFileName = Path.GetFileName(new Uri(url).LocalPath);
                    if (string.IsNullOrEmpty(urlFileName))
                        urlFileName = "index.html";

                    if (numArguments == 3)
                    {
                        string destArg = arguments[2];
                        if (destArg == "~")
                            destArg = homeDirectory;

                        // If destArg is a directory (existing or looks like a directory), place the url file name inside it
                        string destFullCandidate = Path.IsPathRooted(destArg) ? Path.GetFullPath(destArg) : Path.Combine(currentWorkingDir, destArg);

                        if (Directory.Exists(destFullCandidate) || destArg.EndsWith(Path.DirectorySeparatorChar.ToString()) || destArg.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                        {
                            // destination is a directory
                            Directory.CreateDirectory(destFullCandidate);
                            destinationPath = Path.Combine(destFullCandidate, urlFileName);
                        }
                        else
                        {
                            // destination is a file path (create parent if needed)
                            string? parent = Path.GetDirectoryName(destFullCandidate);
                            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                                Directory.CreateDirectory(parent);
                            destinationPath = destFullCandidate;
                        }
                    }
                    else
                    {
                        destinationPath = Path.Combine(currentWorkingDir, urlFileName);
                    }

                    using var httpClient = new HttpClient();
                    using var response = httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();

                    using var contentStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    using var fileStream = File.Create(destinationPath);
                    contentStream.CopyToAsync(fileStream).GetAwaiter().GetResult();

                    Console.WriteLine($"Downloaded {Path.GetFileName(destinationPath)} to {Path.GetDirectoryName(destinationPath) ?? currentWorkingDir}");
                }
                catch (UriFormatException)
                {
                    Console.WriteLine($"get: invalid URL: {url}");
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"get: HTTP error downloading from {url}: {ex.Message}");
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("get: permission denied writing file");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"get: failed to download from {url}: {ex.Message}");
                }


            }
            else if (cmd == "whoami")
            {
                Console.WriteLine(userName);
            }
            else if (cmd == "ping")
            {
                if (numArguments != 2)
                {
                    Console.WriteLine("ping: invalid number of arguments");
                    continue;
                }
                string host = arguments[1];
                try
                {
                    using (var ping = new System.Net.NetworkInformation.Ping())
                    {
                        var reply = ping.Send(host);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                        {
                            Console.WriteLine($"Pinging {host} [{reply.Address}] with {reply.Buffer.Length} bytes of data:");
                            Console.WriteLine($"Reply from {reply.Address}: bytes={reply.Buffer?.Length ?? 0} time={reply.RoundtripTime}ms TTL={reply.Options?.Ttl ?? 0}");
                        }
                        else
                        {
                            Console.WriteLine($"ping: cannot reach {host}: {reply.Status}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ping: error pinging {host}: {ex.Message}");
                }
            }
            else if (cmd == "mv")
            {
                if (numArguments < 3)
                {
                    Console.WriteLine("adalite: mv: missing file operand");
                    continue;
                }

                string sourcePath = arguments[1];
                string destinationPath = arguments[2];

                if (sourcePath.StartsWith("~")) sourcePath = sourcePath.Replace("~", homeDirectory);
                if (destinationPath.StartsWith("~")) destinationPath = destinationPath.Replace("~", homeDirectory);

                try
                {
                    string finalDest = destinationPath;
                    if (Directory.Exists(destinationPath))
                    {
                        finalDest = Path.Combine(destinationPath, Path.GetFileName(sourcePath));
                    }

                    // Logic check: Is it a file or a directory?
                    if (Directory.Exists(sourcePath))
                    {
                        Directory.Move(sourcePath, finalDest);
                    }
                    else
                    {
                        File.Move(sourcePath, finalDest, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"adalite: mv: {ex.Message}");
                }
            }
            else if (cmd == "find")
            {
                // finds a file/folder for eg. "find file.txt <folder-to-search-from>" or "find foldername <folder-to-search-from>" or "find file.txt" to search from current dir
                if (numArguments < 2 || numArguments > 3)
                {
                    Console.WriteLine("find: invalid number of arguments");
                    continue;
                }

                string targetName = arguments[1];
                string searchDirectory = numArguments == 3 ? arguments[2] : currentWorkingDir;

                if (searchDirectory == "~")
                {
                    searchDirectory = homeDirectory;
                }

                try
                {
                    searchDirectory = Path.GetFullPath(searchDirectory);
                }
                catch (Exception)
                {
                    Console.WriteLine($"find: invalid path: {searchDirectory}");
                    continue;
                }

                try
                {
                    var results = Directory.EnumerateFileSystemEntries(searchDirectory, "*", SearchOption.AllDirectories)
                        .Where(path => Path.GetFileName(path).Equals(targetName, StringComparison.OrdinalIgnoreCase));
                    foreach (var result in results)
                    {
                        Console.WriteLine(result);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("find: permission denied");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"find: unexpected error: {ex.Message}");
                }
            }
            else if (cmd == "hostname")
            {
                Console.WriteLine(machineName);
            }
            else if (cmd == "which")
            {
                // finds command in PATH
                if (numArguments != 2)
                {
                    Console.WriteLine("which: invalid number of arguments");
                    continue;
                }

                string commandToFind = arguments[1];
                string? commandPath = FindCommandInPath(commandToFind);

                if (commandPath != null)
                {
                    Console.WriteLine(commandPath);
                }
                else
                {
                    Console.WriteLine($"which: command not found: {commandToFind}");
                }
            }
            else if (cmd == "sleepms")
            {
                // sleeps for given milliseconds
                if (numArguments != 2)
                {
                    Console.WriteLine("sleepms: invalid number of arguments");
                    continue;
                }

                if (int.TryParse(arguments[1], out int milliseconds))
                {
                    if (milliseconds < 0)
                    {
                        Console.WriteLine("sleepms: duration must be non-negative");
                        continue;
                    }
                    System.Threading.Thread.Sleep(milliseconds);
                }
                else
                {
                    Console.WriteLine("sleepms: invalid duration");
                }
            }
            else if (cmd == "sleeps")
            {
                // sleeps for given seconds
                if (numArguments != 2)
                {
                    Console.WriteLine("sleeps: invalid number of arguments");
                    continue;
                }

                if (int.TryParse(arguments[1], out int seconds))
                {
                    if (seconds < 0)
                    {
                        Console.WriteLine("sleeps: duration must be non-negative");
                        continue;
                    }
                    System.Threading.Thread.Sleep(seconds * 1000);
                }
                else
                {
                    Console.WriteLine("sleeps: invalid duration");
                }
            }

            // proffesinoal utils
            else if (cmd == "sysinfo")
            {
                // prints system info like this:
                /*
                 OS: Windows 11 (build 22631)
                Kernel: NT 10.0
                Arch: x64
                CPU: 12 cores
                RAM: 31.8 GB
                Uptime: 3d 4h 22m
                Shell: Adalite 1
                 */

                Console.WriteLine($"OS: {GetOSInfo()}");
                Console.WriteLine($"Kernel: {GetKernelVersion()}");
                Console.WriteLine($"Arch: {RuntimeInformation.OSArchitecture.ToString().ToLower()}");
                Console.WriteLine($"CPU: {GetCPUInfo()}");
                Console.WriteLine($"RAM: {GetRAMInfo()}");
                Console.WriteLine($"Uptime: {GetUptimeInfo()}");
                Console.WriteLine($"Shell: Adalite Terminal {version}");
            }
            else if (cmd == "adle")
            {
                // requires flags
                if (numArguments < 2)
                {
                    Console.WriteLine("adle: missing flag");
                    continue;
                }
                string Flag = arguments[1];

                if (Flag == "installurl")
                {
                    if (numArguments != 4)
                    {
                        Console.WriteLine("adle installurl: usage: adle installurl <url> <destination path>");
                        continue;
                    }
                    string url = arguments[2];
                    string destinationPath = arguments[3];
                    try
                    {
                        using var httpClient = new HttpClient();
                        using var response = httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                        response.EnsureSuccessStatusCode();
                        using var contentStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                        using var fileStream = File.Create(destinationPath);
                        contentStream.CopyToAsync(fileStream).GetAwaiter().GetResult();
                        Console.WriteLine($"Installed file from {url} to {destinationPath}");
                    }
                    catch (UriFormatException)
                    {
                        Console.WriteLine($"adle installurl: invalid URL: {url}");
                    }
                    catch (HttpRequestException ex)
                    {
                        Console.WriteLine($"adle installurl: HTTP error downloading from {url}: {ex.Message}");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.WriteLine("adle installurl: permission denied writing file");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"adle installurl: failed to download from {url}: {ex.Message}");
                    }
                }
                else if (Flag == "installgit")
                {
                    // installs from git repo

                    if (numArguments != 4)
                    {
                        Console.WriteLine("adle installgit: usage: adle installgit <git-repo-url> <destination path>");
                        continue;
                    }

                    string gitRepoUrl = arguments[2];
                    string destinationPath = arguments[3];

                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "git",
                            Arguments = $"clone {gitRepoUrl} \"{destinationPath}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false
                        };
                        var proc = Process.Start(psi);
                        if (proc == null)
                        {
                            Console.WriteLine("adle installgit: failed to start git process");
                            continue;
                        }
                        string output = proc.StandardOutput?.ReadToEnd() ?? string.Empty;
                        string errorOutput = proc.StandardError?.ReadToEnd() ?? string.Empty;
                        proc.WaitForExit();
                        if (proc.ExitCode == 0)
                        {
                            Console.WriteLine($"Installed git repository from {gitRepoUrl} to {destinationPath}");
                        }
                        else
                        {
                            Console.WriteLine($"adle installgit: git clone failed with exit code {proc.ExitCode}");
                            if (!string.IsNullOrEmpty(errorOutput))
                            {
                                Console.WriteLine($"Error: {errorOutput}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"adle installgit: failed to clone from {gitRepoUrl}: {ex.Message}");
                    }
                }
                else if (Flag == "pkg")
                {
                    if (numArguments < 7)
                    {
                        Console.WriteLine(
                            "adle pkg: usage:\n" +
                            "  adle pkg <folder> <package-name> <version> <author> <description> [-extras <extra1> <extra2> ...]"
                        );
                        continue;
                    }

                    string sourceFolder = arguments[2];
                    string packageName = arguments[3];
                    string version2 = arguments[4];
                    string author = arguments[5];
                    string description = arguments[6];

                    // Normalize source path
                    try
                    {
                        sourceFolder = Path.GetFullPath(sourceFolder);
                    }
                    catch
                    {
                        Console.WriteLine("adle pkg: invalid source folder");
                        continue;
                    }

                    if (!Directory.Exists(sourceFolder))
                    {
                        Console.WriteLine($"adle pkg: source folder not found: {sourceFolder}");
                        continue;
                    }

                    // Parse extras (flexible)
                    List<string> extraFolders = new();
                    int extrasIndex = Array.IndexOf(arguments, "-extras");

                    if (extrasIndex != -1)
                    {
                        for (int i = extrasIndex + 1; i < numArguments; i++)
                        {
                            extraFolders.Add(arguments[i]);
                        }
                    }

                    string packageDir = Path.Combine(Directory.GetCurrentDirectory(), packageName);

                    if (Directory.Exists(packageDir))
                    {
                        Console.WriteLine($"adle pkg: package '{packageName}' already exists");
                        continue;
                    }

                    try
                    {
                        // Create base structure
                        Directory.CreateDirectory(packageDir);

                        // src/
                        string srcDir = Path.Combine(packageDir, "src");
                        DirectoryCopy(sourceFolder, srcDir, true);

                        // .adle/metadata.json
                        string metaDir = Path.Combine(packageDir, ".adle");
                        Directory.CreateDirectory(metaDir);

                        string metadataPath = Path.Combine(metaDir, "metadata.json");

                        var metadata = new
                        {
                            name = packageName,
                            version2,
                            author,
                            description,
                            created = DateTime.UtcNow.ToString("o")
                        };

                        string metadataJson = System.Text.Json.JsonSerializer.Serialize(
                            metadata,
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                        );

                        File.WriteAllText(metadataPath, metadataJson);

                        // extras/
                        if (extraFolders.Count > 0)
                        {
                            string extrasDir = Path.Combine(packageDir, "extras");
                            Directory.CreateDirectory(extrasDir);

                            foreach (string extra in extraFolders)
                            {
                                string extraPath = Path.GetFullPath(extra);

                                if (!Directory.Exists(extraPath))
                                {
                                    Console.WriteLine($"adle pkg: extra folder not found: {extra}");
                                    continue;
                                }

                                string dest = Path.Combine(extrasDir, Path.GetFileName(extraPath));
                                DirectoryCopy(extraPath, dest, true);
                            }
                        }

                        Console.WriteLine($"adle pkg: package '{packageName}' created successfully");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"adle pkg: failed: {ex.Message}");
                    }
                }
                else if (Flag == "unpkg")
                {
                    if (numArguments != 3)
                    {
                        Console.WriteLine("adle unpkg: usage: adle unpkg <pkg-folder>");
                        continue;
                    }

                    string pkgFolder = arguments[2];

                    if (!Directory.Exists(pkgFolder))
                    {
                        Console.WriteLine($"adle unpkg: package folder not found: {pkgFolder}");
                        continue;
                    }

                    string extractedDir = pkgFolder + "-extracted";

                    if (Directory.Exists(extractedDir))
                    {
                        Console.WriteLine($"adle unpkg: output already exists: {extractedDir}");
                        continue;
                    }

                    int boldchoice;

                    if (!Verif(pkgFolder, out string error))
                    {
                        Console.WriteLine(error);
                        boldchoice = 0;
                    }
                    else
                    {
                        boldchoice = 1;
                    }

                    if (boldchoice == 1)
                    {
                        try
                        {
                            Directory.CreateDirectory(extractedDir);

                            string srcDir = Path.Combine(pkgFolder, "src");
                            string extrasDir = Path.Combine(pkgFolder, "extras");

                            if (Directory.Exists(srcDir))
                                DirectoryCopy(srcDir, extractedDir, true);

                            if (Directory.Exists(extrasDir))
                                DirectoryCopy(extrasDir, extractedDir, true);

                            Console.WriteLine($"adle unpkg: extracted to '{extractedDir}'");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"adle unpkg: failed: {ex.Message}");
                        }
                    }
                }
                else if (Flag == "verif")
                {
                    if (numArguments != 3)
                    {
                        Console.WriteLine("verif: usage: adle verif <package-folder>");
                        continue;
                    }

                    string pkgFolder = arguments[2];

                    if (pkgFolder == "~")
                        pkgFolder = homeDirectory;

                    try
                    {
                        pkgFolder = Path.GetFullPath(pkgFolder);
                    }
                    catch
                    {
                        Console.WriteLine($"verif: invalid path: {arguments[1]}");
                        continue;
                    }

                    if (!Directory.Exists(pkgFolder))
                    {
                        Console.WriteLine($"verif: package folder not found: {pkgFolder}");
                        continue;
                    }

                    if (!Verif(pkgFolder, out string error))
                    {
                        Console.WriteLine(error);
                        continue;
                    }

                    Console.WriteLine("verif: OK");
                }
                else if (Flag == "install")
                {

                    if (arguments.Length < 3)
                    {
                        Console.WriteLine("adle install: missing package name");
                        continue;
                    }

                    string packageName = arguments[2];
                    string packageTarget = Path.Combine(installDirN!, packageName);
                    string anyerror;

                    bool res = Verif(packageName, out anyerror);

                    if (res == false)
                    {
                        Console.WriteLine($"adle install: package verification failed: {anyerror}");
                        continue;
                    }

                    if (!Directory.Exists(packageName))
                    {
                        Console.WriteLine($"adle install: package not found: {packageName}");
                        continue;
                    }

                    if (Directory.Exists(packageTarget))
                    {
                        Console.WriteLine($"adle install: package already installed: {packageName}");
                        continue;
                    }

                    try
                    {
                        DirectoryCopy(packageName, packageTarget, true);
                        Console.WriteLine($"adle install: installed {packageName}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"adle install: failed: {ex.Message}");
                    }
                }
                else if (Flag == "remove")
                { 

                    if (arguments.Length < 3)
                    {
                        Console.WriteLine("adle remove: missing package name");
                        continue;
                    }

                    string packageName = arguments[2];

                    if (installDirN == null)
                    {
                        Console.WriteLine("adle remove: install directory not configured");
                        continue;
                    }

                    installDirN = ExpandHome(installDirN);

                    string installedPackagePath = Path.Combine(installDirN, packageName);

                    if (!Directory.Exists(installedPackagePath))
                    {
                        Console.WriteLine($"adle remove: package not installed: {packageName}");
                        continue;
                    }

                    try
                    {
                        Directory.Delete(installedPackagePath, recursive: true);
                        Console.WriteLine($"adle remove: removed {packageName}");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.WriteLine("adle remove: permission denied");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"adle remove: failed: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"adle: unknown flag {Flag}");
                }
            }
            else if (cmd == "bootstrap") 
            {
                // there is no ~/.global-adle directory, create it and set up basic config
                // no need for numArgument
                if (File.Exists(globalAdleDir))
                {
                    Console.WriteLine("first-time-startup: already initialized.");
                    continue;
                }


                try
                {
                    if (!Directory.Exists(globalAdleDir))
                    {
                        Directory.CreateDirectory(globalAdleDir);
                    }
                    if (!Directory.Exists(pkgDir))
                    {
                        Directory.CreateDirectory(pkgDir);
                    }
                    if (!Directory.Exists(installDir))
                    {
                        Directory.CreateDirectory(installDir);
                    }
                    if (!Directory.Exists(tempDir))
                    {
                        Directory.CreateDirectory(tempDir);
                    }

                    var defaultConfig = new
                    {
                        username = userName,
                        machineName = machineName,
                        pkgpath = pkgDir,
                        installdir = installDir,
                        tempdir = tempDir
                    };
                    string configJson = System.Text.Json.JsonSerializer.Serialize(
                        defaultConfig,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                    );
                    File.WriteAllText(configPath, configJson);
                    Console.WriteLine("first-time-startup: global Adle configuration created successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"first-time-startup: failed: {ex.Message}");
                }
            }
            else if (cmd == "hexdump")
            {
                // displays the hex content of a file
                if (numArguments != 2)
                {
                    Console.WriteLine("hexdump: invalid number of arguments");
                    continue;
                }
                string filePath = arguments[1];
                if (filePath == "~")
                {
                    filePath = homeDirectory;
                }
                try
                {
                    filePath = Path.GetFullPath(filePath);
                }
                catch (Exception)
                {
                    Console.WriteLine($"hexdump: invalid file name: {arguments[1]}");
                    continue;
                }
                try
                {
                    using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        int bytesRead;
                        byte[] buffer = new byte[16];
                        long offset = 0;
                        while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            Console.Write($"{offset:X8}  ");
                            for (int i = 0; i < bytesRead; i++)
                            {
                                Console.Write($"{buffer[i]:X2} ");
                            }
                            for (int i = bytesRead; i < 16; i++)
                            {
                                Console.Write("   ");
                            }
                            Console.Write(" |");
                            for (int i = 0; i < bytesRead; i++)
                            {
                                char c = (char)buffer[i];
                                Console.Write(char.IsControl(c) ? '.' : c);
                            }
                            Console.WriteLine("|");
                            offset += bytesRead;
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine($"hexdump: file not found: {filePath}");
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("hexdump: permission denied");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"hexdump: unexpected error: {ex.Message}");
                }
            }
            else if (cmd == "ip")
            {
                // linux style ip addr show command
                if (numArguments < 2)
                {
                    Console.WriteLine("ip: invalid number of arguments");
                    continue;
                }

                string Flag = arguments[1];

                if (Flag == "addr" || Flag == "address" || Flag == "-a")
                {
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in host.AddressList)
                    {
                        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            Console.WriteLine($"IPv4 Address: {ip}");
                        }
                        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                        {
                            Console.WriteLine($"IPv6 Address: {ip}");
                        }
                    }
                }
                else if (Flag == "route")
                {
                    if (OperatingSystem.IsWindows())
                    {
                        ShowWindowsRoutes();
                    }
                    else
                    {
                        ShowUnixRoutes();
                    }
                }
                else
                {
                    Console.WriteLine($"ip: unknown flag {Flag}");
                }
            }
            else
            {
                int res = ExecFromPath(arguments);
                if (res == 1)
                {
                    Console.WriteLine($"error: command not found >> {arguments[0]} <<");
                }
            }
            
            
            }
        LogWriter?.Dispose();
    }
    

    private static void CtrlChandler(object? sender, ConsoleCancelEventArgs e)
    {
        if (e.SpecialKey == ConsoleSpecialKey.ControlC)
        {
            Console.WriteLine("\nOperation cancelled by user.");
            e.Cancel = true;
        }
    }


    static bool Verif(string pkgFolder, out string error)
    {
        error = string.Empty;

        try
        {
            JsonDocument doc;
            pkgFolder = Path.GetFullPath(pkgFolder);

            // Rule 1: .adle folder exists
            string adleDir = Path.Combine(pkgFolder, ".adle");
            if (!Directory.Exists(adleDir))
            {
                error = "verif: missing .adle folder";
                return false;
            }

            // Rule 2: metadata.json exists
            string metadataPath = Path.Combine(adleDir, "metadata.json");
            if (!File.Exists(metadataPath))
            {
                error = "verif: missing metadata.json";
                return false;
            }

            try
            {
                doc = JsonDocument.Parse(File.ReadAllText(metadataPath));
            }
            catch (JsonException ex)
            {
                // doc.Dispose();
                error = $"verif: invalid metadata.json (JSON parse error): {ex.Message}";
                return false;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "verif: metadata.json root must be an object";
                doc.Dispose();
                return false;
            }

            // Rule 3: required fields exist and are sane
            foreach (string field in RequiredMetadataFields)
            {
                if (!doc.RootElement.TryGetProperty(field, out JsonElement value))
                {
                    error = $"verif: missing required field '{field}'";
                    doc.Dispose();
                    return false;
                }

                if (value.ValueKind != JsonValueKind.String)
                {
                    error = $"verif: field '{field}' must be a string";
                    doc.Dispose();
                    return false;
                }

                string str = value.GetString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(str))
                {
                    error = $"verif: field '{field}' is empty";
                    doc.Dispose();
                    return false;
                }


                foreach (char c in str)
                {
                    if (char.IsControl(c) && c != '\n' && c != '\t')
                    {
                        error = $"verif: field '{field}' contains control characters";
                        doc.Dispose();
                        return false;
                    }
                }
            }

            doc.Dispose();

            // Rule 4: src exists
            string srcDir = Path.Combine(pkgFolder, "src");
            if (!Directory.Exists(srcDir))
            {
                error = "verif: missing src directory";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"verif: unexpected error: {ex.Message}";
            return false;
        }
    }

    static StreamWriter? LogWriter;

    static void Log(string message)
    {
        try
        {
            LogWriter?.WriteLine($"[{DateTime.UtcNow:O}] {message}");
        }
        catch
        {
            // never crash because logging failed
        }
    }

    static void InitSessionLog(string globalAdleDir)
    {
        Directory.CreateDirectory(globalAdleDir);

        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        string logPath = Path.Combine(
            globalAdleDir,
            $"temporary.logfile-{timestamp}.log"
        );

        LogWriter = new StreamWriter(
            new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read)
        )
        {
            AutoFlush = true
        };

        Log("Adle session started");
    }

    private static string GetOSInfo()
    {
        if (OperatingSystem.IsWindows())
        {
            return $"Windows {Environment.OSVersion.VersionString}";
        }
        else if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }
        else if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }
        else
        {
            return "Unknown OS";
        }
    }

    private static string GetKernelVersion()
    {
        return Environment.OSVersion.Version.ToString();
    }

    private static string GetCPUInfo()
    {
        int coreCount = Environment.ProcessorCount;
        return $"{coreCount} cores";
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    private static string GetRAMInfo()
    { 
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (GetPhysicallyInstalledSystemMemory(out ulong memKb))
                {
                    double totalMemoryInGB = memKb / (1024.0 * 1024.0);
                    return $"{totalMemoryInGB:F1} GB";
                }
            }
            catch
            {
                // fall through to unknown
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                string memInfo = File.ReadAllText("/proc/meminfo");
                var lines = memInfo.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:"))
                    {
                        var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && ulong.TryParse(parts[1], out ulong memKb))
                        {
                            double totalMemoryInGB = memKb / (1024.0 * 1024.0);
                            return $"{totalMemoryInGB:F1} GB";
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return "Unknown RAM";
    }

    private static string GetUptimeInfo()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
            }
            else if (OperatingSystem.IsLinux())
            {
                string uptimeContent = File.ReadAllText("/proc/uptime");
                var parts = uptimeContent.Split(' ');
                if (parts.Length >= 1 && double.TryParse(parts[0], out double uptimeSeconds))
                {
                    var uptime = TimeSpan.FromSeconds(uptimeSeconds);
                    return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
                }
            }
        }
        catch
        {
        }
        return "Unknown Uptime";
    }

    static void ShowWindowsRoutes()
    {
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            var props = ni.GetIPProperties();

            // Print default gateways for interface
            foreach (var gw in props.GatewayAddresses)
            {
                var addr = gw?.Address;
                if (addr != null && addr.ToString() != "0.0.0.0")
                {
                    Console.WriteLine($"default via {addr} dev {ni.Name}");
                }
            }

            // Print assigned unicast addresses
            foreach (var unicast in props.UnicastAddresses)
            {
                var addr = unicast.Address;
                if (addr != null)
                {
                    Console.WriteLine($"{addr} dev {ni.Name}");
                }
            }
        }
    }

    static void ShowUnixRoutes()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ip",
                Arguments = "route",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            var proc = Process.Start(psi);
            if (proc == null)
            {
                Console.WriteLine("adalite: unable to retrieve routing table");
                return;
            }

            string output = proc.StandardOutput?.ReadToEnd() ?? string.Empty;
            Console.Write(output);
            proc.WaitForExit();
        }
        catch
        {
            Console.WriteLine("adalite: unable to retrieve routing table");
        }
    }


    private static string? FindCommandInPath(string commandName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv == null)
        {
            return null;
        }

        string[] paths = pathEnv.Split(Path.PathSeparator);
        foreach (string path in paths)
        {
            string fullPath = Path.Combine(path, commandName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
            // On Windows, check for common executable extensions
            if (OperatingSystem.IsWindows())
            {
                string[] extensions = { ".exe", ".bat", ".cmd" };
                foreach (string ext in extensions)
                {
                    string candidate = Path.Combine(path, commandName + ext);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        return null;
    }

}