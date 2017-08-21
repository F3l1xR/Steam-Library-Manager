﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Steam_Library_Manager.Definitions
{
    public class Game
    {
        public int AppID { get; set; }
        public string AppName { get; set; }
        public DirectoryInfo InstallationPath;
        public long SizeOnDisk { get; set; }
        public bool IsCompressed { get; set; }
        public bool IsSteamBackup { get; set; }
        public Library InstalledLibrary { get; set; }
        public DateTime LastUpdated { get; set; }

        public string GameHeaderImage
        {
            get => $"http://cdn.akamai.steamstatic.com/steam/apps/{AppID}/header.jpg";
        }

        public string PrettyGameSize
        {
            get => Functions.FileSystem.FormatBytes(SizeOnDisk);
        }

        public DirectoryInfo CommonFolder
        {
            get => new DirectoryInfo(Path.Combine(InstalledLibrary.CommonFolder.FullName, InstallationPath.Name));
        }

        public DirectoryInfo DownloadFolder
        {
            get => new DirectoryInfo(Path.Combine(InstalledLibrary.DownloadFolder.FullName, InstallationPath.Name));
        }

        public DirectoryInfo WorkShopPath
        {
            get => new DirectoryInfo(Path.Combine(InstalledLibrary.WorkshopFolder.FullName, "content", AppID.ToString()));
        }

        public FileInfo CompressedArchiveName
        {
            get => new FileInfo(Path.Combine(InstalledLibrary.SteamAppsFolder.FullName, AppID + ".zip"));
        }

        public FileInfo FullAcfPath
        {
            get => new FileInfo(Path.Combine(InstalledLibrary.SteamAppsFolder.FullName, AcfName));
        }

        public FileInfo WorkShopAcfPath
        {
            get => new FileInfo(Path.Combine(InstalledLibrary.WorkshopFolder.FullName, WorkShopAcfName));
        }

        public string AcfName
        {
            get => $"appmanifest_{AppID}.acf";
        }

        public string WorkShopAcfName
        {
            get => $"appworkshop_{AppID}.acf";
        }

        public Framework.AsyncObservableCollection<FrameworkElement> ContextMenuItems
        {
            get => GenerateRightClickMenuItems();
        }

        public Framework.AsyncObservableCollection<FrameworkElement> GenerateRightClickMenuItems()
        {
            Framework.AsyncObservableCollection<FrameworkElement> rightClickMenu = new Framework.AsyncObservableCollection<FrameworkElement>();
            try
            {
                foreach (ContextMenuItem cItem in List.GameCMenuItems.Where(x => x.IsActive))
                {
                    if (IsSteamBackup && cItem.ShowToSteamBackup == Enums.MenuVisibility.NotVisible)
                        continue;
                    else if (InstalledLibrary.IsBackup && cItem.ShowToSLMBackup == Enums.MenuVisibility.NotVisible)
                        continue;
                    else if (IsCompressed && cItem.ShowToCompressed == Enums.MenuVisibility.NotVisible)
                        continue;
                    else if (cItem.ShowToNormal == Enums.MenuVisibility.NotVisible)
                        continue;

                    if (cItem.IsSeparator)
                        rightClickMenu.Add(new Separator());
                    else
                    {
                        MenuItem slmItem = new MenuItem()
                        {
                            Tag = this,
                            Header = string.Format(cItem.Header, AppName, AppID, Functions.FileSystem.FormatBytes(SizeOnDisk))
                        };
                        slmItem.Tag = cItem.Action;
                        slmItem.Icon = Functions.FAwesome.GetAwesomeIcon(cItem.Icon, cItem.IconColor);
                        slmItem.HorizontalContentAlignment = HorizontalAlignment.Left;
                        slmItem.VerticalContentAlignment = VerticalAlignment.Center;

                        rightClickMenu.Add(slmItem);
                    }
                }

                return rightClickMenu;
            }
            catch (FormatException ex)
            {
                MessageBox.Show($"An error happened while parsing context menu, most likely happened duo typo on color name.\n\n{ex}");
                Functions.Logger.LogToFile(Functions.Logger.LogType.SLM, $"[{AppName}][{AppID}][{AcfName}] {ex}");

                return rightClickMenu;
            }
        }

        public void ParseMenuItemAction(string Action)
        {
            switch (Action.ToLowerInvariant())
            {
                default:
                    if (string.IsNullOrEmpty(SLM.userSteamID64))
                        return;

                    System.Diagnostics.Process.Start(string.Format(Action, AppID, SLM.userSteamID64));
                    break;
                case "disk":
                    if (CommonFolder.Exists)
                        System.Diagnostics.Process.Start(CommonFolder.FullName);
                    break;
                case "acffile":
                    System.Diagnostics.Process.Start(FullAcfPath.FullName);
                    break;
                case "deletegamefilesslm":

                    DeleteFiles();
                    break;
            }
        }

        public List<FileSystemInfo> GetFileList(bool includeDownloads = true, bool includeWorkshop = true)
        {
            List<FileSystemInfo> FileList = new List<FileSystemInfo>();

            if (IsCompressed)
            {
                FileList.Add(CompressedArchiveName);
            }
            else
            {
                if (CommonFolder.Exists)
                {
                    FileList.AddRange(GetCommonFiles());
                }

                if (includeDownloads && DownloadFolder.Exists)
                {
                    FileList.AddRange(GetDownloadFiles());
                    FileList.AddRange(GetPatchFiles());
                }

                if (includeWorkshop && WorkShopPath.Exists)
                {
                    FileList.AddRange(GetWorkshopFiles());
                }

                if (FullAcfPath.Exists)
                {
                    FileList.Add(FullAcfPath);
                }

                if (WorkShopAcfPath.Exists)
                {
                    FileList.Add(WorkShopAcfPath);
                }
            }

            return FileList;
        }

        public List<FileSystemInfo> GetCommonFiles()
        {
            return CommonFolder.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Where(x => x is FileInfo).ToList();
        }

        public List<FileSystemInfo> GetDownloadFiles()
        {
            return DownloadFolder.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Where(x => x is FileInfo).ToList();
        }

        public List<FileSystemInfo> GetPatchFiles()
        {
            return InstalledLibrary.DownloadFolder.EnumerateFileSystemInfos($"*{AppID}*.patch", SearchOption.TopDirectoryOnly).Where(x => x is FileInfo).ToList();
        }

        public List<FileSystemInfo> GetWorkshopFiles()
        {
            return WorkShopPath.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Where(x => x is FileInfo).ToList();
        }

        public void CopyGameFiles(List.TaskList CurrentTask, CancellationToken cancellationToken)
        {
            LogToTM($"[{AppName}] Populating file list, please wait");
            Functions.Logger.LogToFile(Functions.Logger.LogType.Game, "Populating file list", this);

            ConcurrentBag<string> CopiedFiles = new ConcurrentBag<string>();
            ConcurrentBag<string> CreatedDirectories = new ConcurrentBag<string>();
            List<FileSystemInfo> GameFiles = GetFileList();
            CurrentTask.TotalFileCount = GameFiles.Count;

            try
            {
                long TotalFileSize = 0;
                ParallelOptions parallelOptions = new ParallelOptions()
                {
                    CancellationToken = cancellationToken
                };

                Parallel.ForEach(GameFiles, parallelOptions, file =>
                {
                    Interlocked.Add(ref TotalFileSize, (file as FileInfo).Length);
                });

                CurrentTask.TotalFileSize = TotalFileSize;
                CurrentTask.ElapsedTime.Start();

                LogToTM($"[{AppName}] File list populated, total files to move: {GameFiles.Count} - total size to move: {Functions.FileSystem.FormatBytes(TotalFileSize)}");
                Functions.Logger.LogToFile(Functions.Logger.LogType.Game, $"File list populated, total files to move: {GameFiles.Count} - total size to move: {Functions.FileSystem.FormatBytes(TotalFileSize)}", this);

                // If the game is not compressed and user would like to compress it
                if (!IsCompressed && CurrentTask.Compress)
                {
                    FileInfo compressedArchive = new FileInfo(CompressedArchiveName.FullName.Replace(InstalledLibrary.SteamAppsFolder.FullName, CurrentTask.TargetLibrary.SteamAppsFolder.FullName));

                    if (compressedArchive.Exists)
                        compressedArchive.Delete();

                    using (ZipArchive compressed = ZipFile.Open(compressedArchive.FullName, ZipArchiveMode.Create))
                    {
                        CopiedFiles.Add(compressedArchive.FullName);

                        foreach (FileSystemInfo currentFile in GameFiles)
                        {
                            string newFileName = currentFile.FullName.Substring(InstalledLibrary.SteamAppsFolder.FullName.Length + 1);

                            compressed.CreateEntryFromFile(currentFile.FullName, newFileName, CompressionLevel.Optimal);

                            //CopiedFiles.Add(newFileName);
                            CurrentTask.MovenFileSize += (currentFile as FileInfo).Length;

                            if (CurrentTask.ReportFileMovement)
                            {
                                LogToTM($"[{AppName}][{CopiedFiles.Count}/{CurrentTask.TotalFileCount}] Moven file: {newFileName}");
                            }

                            Functions.Logger.LogToFile(Functions.Logger.LogType.Game, $"[{CopiedFiles.Count}/{CurrentTask.TotalFileCount}] Moven file: {newFileName}", this);

                            if (cancellationToken.IsCancellationRequested)
                                throw new OperationCanceledException(cancellationToken);
                        }
                    }
                }
                // If the game is compressed and user would like to decompress it
                else if (IsCompressed && !CurrentTask.Compress)
                {
                    foreach (ZipArchiveEntry currentFile in ZipFile.OpenRead(CompressedArchiveName.FullName).Entries)
                    {
                        FileInfo newFile = new FileInfo(Path.Combine(CurrentTask.TargetLibrary.SteamAppsFolder.FullName, currentFile.FullName));

                        if (!newFile.Directory.Exists)
                        {
                            newFile.Directory.Create();
                            CreatedDirectories.Add(newFile.Directory.FullName);
                        }

                        currentFile.ExtractToFile(newFile.FullName, true);

                        CopiedFiles.Add(newFile.FullName);
                        CurrentTask.MovenFileSize += currentFile.Length;

                        if (CurrentTask.ReportFileMovement)
                        {
                            LogToTM($"[{AppName}][{CopiedFiles.Count}/{CurrentTask.TotalFileCount}] Moven file: {newFile.FullName}");
                        }

                        Functions.Logger.LogToFile(Functions.Logger.LogType.Game, $"[{CopiedFiles.Count}/{CurrentTask.TotalFileCount}] Moven file: {newFile.FullName}", this);

                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException(cancellationToken);
                    }
                }
                // Everything else
                else
                {
                    parallelOptions.MaxDegreeOfParallelism = 1;

                    Parallel.ForEach(GameFiles.Where(x => (x as FileInfo).Length > Properties.Settings.Default.ParallelAfterSize * 1000000).OrderByDescending(x => (x as FileInfo).Length), parallelOptions, currentFile =>
                    {
                        FileInfo newFile = new FileInfo(currentFile.FullName.Replace(InstalledLibrary.SteamAppsFolder.FullName, CurrentTask.TargetLibrary.SteamAppsFolder.FullName));

                        if (!newFile.Exists || (newFile.Length != (currentFile as FileInfo).Length || newFile.LastWriteTime != (currentFile as FileInfo).LastWriteTime))
                        {
                            if (!newFile.Directory.Exists)
                            {
                                newFile.Directory.Create();
                                CreatedDirectories.Add(newFile.Directory.FullName);
                            }

                            (currentFile as FileInfo).CopyTo(newFile.FullName, true);
                        }

                        CopiedFiles.Add(newFile.FullName);
                        CurrentTask.MovenFileSize += (currentFile as FileInfo).Length;

                        if (CurrentTask.ReportFileMovement)
                        {
                            LogToTM($"[{AppName}][{CopiedFiles.Count}/{CurrentTask.TotalFileCount}] Moven file: {newFile.FullName}");
                        }

                        Functions.Logger.LogToFile(Functions.Logger.LogType.Game, $"[{CopiedFiles.Count}/{CurrentTask.TotalFileCount}] Moven file: {newFile.FullName}", this);
                    });

                    parallelOptions.MaxDegreeOfParallelism = -1;

                    Parallel.ForEach(GameFiles.Where(x => (x as FileInfo).Length <= Properties.Settings.Default.ParallelAfterSize * 1000000).OrderByDescending(x => (x as FileInfo).Length), parallelOptions, currentFile =>
                    {
                        FileInfo newFile = new FileInfo(currentFile.FullName.Replace(InstalledLibrary.SteamAppsFolder.FullName, CurrentTask.TargetLibrary.SteamAppsFolder.FullName));

                        if (!newFile.Exists || (newFile.Length != (currentFile as FileInfo).Length || newFile.LastWriteTime != (currentFile as FileInfo).LastWriteTime))
                        {
                            if (!newFile.Directory.Exists)
                            {
                                newFile.Directory.Create();
                                CreatedDirectories.Add(newFile.Directory.FullName);
                            }

                            (currentFile as FileInfo).CopyTo(newFile.FullName, true);
                        }

                        CopiedFiles.Add(newFile.FullName);
                        CurrentTask.MovenFileSize += (currentFile as FileInfo).Length;

                        if (CurrentTask.ReportFileMovement)
                        {
                            LogToTM($"[{AppName}][{CopiedFiles.Count}/{CurrentTask.TotalFileCount}] Moven file: {newFile.FullName}");
                        }

                        Functions.Logger.LogToFile(Functions.Logger.LogType.Game, $"[{CopiedFiles.Count}/{CurrentTask.TotalFileCount}] Moven file: {newFile.FullName}", this);
                    });

                }

                CurrentTask.ElapsedTime.Stop();

                LogToTM($"[{AppName}] Time elapsed: {CurrentTask.ElapsedTime.Elapsed} - Average speed: {Math.Round(((TotalFileSize / 1024f) / 1024f) / CurrentTask.ElapsedTime.Elapsed.TotalSeconds, 3)} MB/sec - Average file size: {Functions.FileSystem.FormatBytes(TotalFileSize / (long)CurrentTask.TotalFileCount)}");
                Functions.Logger.LogToFile(Functions.Logger.LogType.Game, $"Movement completed in {CurrentTask.ElapsedTime.Elapsed} with Average Speed of {Math.Round(((TotalFileSize / 1024f) / 1024f) / CurrentTask.ElapsedTime.Elapsed.TotalSeconds, 3)} MB/sec - Average file size: {Functions.FileSystem.FormatBytes(TotalFileSize / (long)CurrentTask.TotalFileCount)}", this);
            }
            catch (OperationCanceledException)
            {
                Framework.TaskManager.Stop();
                CurrentTask.Moving = false;
                CurrentTask.Completed = true;

                MessageBoxResult removeMovenFiles = MessageBox.Show($"[{AppName}] Game movement cancelled. Would you like to remove files that already moven?", "Remove moven files?", MessageBoxButton.YesNo);

                if (removeMovenFiles == MessageBoxResult.Yes)
                    Functions.FileSystem.RemoveGivenFiles(CopiedFiles, CreatedDirectories);

                LogToTM($"[{AppName}] Operation cancelled by user. Time Elapsed: {CurrentTask.ElapsedTime.Elapsed}");
                Functions.Logger.LogToFile(Functions.Logger.LogType.Game, $"Operation cancelled by user. Time Elapsed: {CurrentTask.ElapsedTime.Elapsed}", this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                MessageBoxResult removeMovenFiles = MessageBox.Show($"[{AppName}] An error happened while moving game files. Would you like to remove files that already moven?", "Remove moven files?", MessageBoxButton.YesNo);

                if (removeMovenFiles == MessageBoxResult.Yes)
                    Functions.FileSystem.RemoveGivenFiles(CopiedFiles, CreatedDirectories);

                Main.Accessor.TaskManager_Logs.Add($"[{AppName}] An error happened while moving game files. Time Elapsed: {CurrentTask.ElapsedTime.Elapsed}");
                Functions.Logger.LogToFile(Functions.Logger.LogType.SLM, $"[{AppName}][{AppID}][{AcfName}] {ex}");
            }
        }

        public void LogToTM(string TextToLog)
        {
            try
            {
                Main.Accessor.TaskManager_Logs.Add($"[{DateTime.Now}] {TextToLog}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                Functions.Logger.LogToFile(Functions.Logger.LogType.SLM, $"[{AppName}][{AppID}][{AcfName}] {ex}");
            }
        }

        public bool DeleteFiles()
        {
            try
            {
                if (IsCompressed)
                {
                    CompressedArchiveName.Delete();
                }
                else if (IsSteamBackup)
                {
                    if (InstallationPath.Exists)
                        InstallationPath.Delete(true);
                }
                else
                {
                    List<FileSystemInfo> gameFiles = GetFileList();

                    Parallel.ForEach(gameFiles, currentFile =>
                    {
                        if (currentFile.Exists)
                            currentFile.Delete();
                    }
                    );

                    // common folder, if exists
                    if (CommonFolder.Exists)
                        CommonFolder.Delete(true);

                    // downloading folder, if exists
                    if (DownloadFolder.Exists)
                        DownloadFolder.Delete(true);

                    // workshop folder, if exists
                    if (WorkShopPath.Exists)
                        WorkShopPath.Delete(true);

                    // game .acf file
                    if (FullAcfPath.Exists)
                        FullAcfPath.Delete();

                    // workshop .acf file
                    if (WorkShopAcfPath.Exists)
                        WorkShopAcfPath.Delete();
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                Functions.Logger.LogToFile(Functions.Logger.LogType.SLM, $"[{AppName}][{AppID}][{AcfName}] {ex}");

                return false;
            }
        }
    }
}
