using BDInfoLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;

namespace BDInfo
{
    public class runner
    {
        private BDROM BDROM = null;
        private int CustomPlaylistCount = 0;
        ScanBDROMResult ScanResult = new ScanBDROMResult();
        List<TSStreamFile> streamFiles;
        ScanBDROMState scanState;
         

        #region BDROM Initialization Worker

        private BackgroundWorker InitBDROMWorker = null;

        public void InitBDROM(string path)
        {
            System.Console.WriteLine("Please wait while we scan the disc...");

            CustomPlaylistCount = 0;

            List<String> mplsList = new List<string>(){ "00801.mpls" };

            InitBDROMWork(path);
            
            
            /*ScanBDROM();
            ScanBDROMWork();
            ScanBDROMProgress();
            ScanBDROMCompleted(new List<String>());
             * */
            

        }

        private void InitBDROMWork(String path)
        {
            try
            {
                BDROM = new BDROM(path);
                BDROM.StreamClipFileScanError += new BDROM.OnStreamClipFileScanError(BDROM_StreamClipFileScanError);
                BDROM.StreamFileScanError += new BDROM.OnStreamFileScanError(BDROM_StreamFileScanError);
                BDROM.PlaylistFileScanError += new BDROM.OnPlaylistFileScanError(BDROM_PlaylistFileScanError);
                BDROM.Scan();

                System.Console.WriteLine(BDROM.DirectoryRoot.FullName);

                System.Console.WriteLine(string.Format("Detected BDMV Folder: {0} ({1})", BDROM.DirectoryBDMV.FullName, BDROM.VolumeLabel));

                List<string> features = new List<string>();
                if (BDROM.Is50Hz)
                {
                    features.Add("50Hz Content");
                }
                if (BDROM.IsBDPlus)
                {
                    features.Add("BD+ Copy Protection");
                }
                if (BDROM.IsBDJava)
                {
                    features.Add("BD-Java");
                }
                if (BDROM.Is3D)
                {
                    features.Add("Blu-ray 3D");
                }
                if (BDROM.IsDBOX)
                {
                    features.Add("D-BOX Motion Code");
                }
                if (BDROM.IsPSP)
                {
                    features.Add("PSP Digital Copy");
                }
                if (features.Count > 0)
                {
                    System.Console.WriteLine("Detected Features: " + string.Join(", ", features.ToArray()));
                }

                System.Console.WriteLine(string.Format("Disc Size: {0:N0} bytes{1}", BDROM.Size, Environment.NewLine));
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        protected bool BDROM_PlaylistFileScanError(TSPlaylistFile playlistFile, Exception ex)
        {
            System.Console.WriteLine(string.Format(
                "An error occurred while scanning the playlist file {0}.\n\nThe disc may be copy-protected or damaged.\n\nDo you want to continue scanning the playlist files?", playlistFile.Name));

            return GetYorNdefaultY();
        }

        protected bool BDROM_StreamFileScanError(TSStreamFile streamFile, Exception ex)
        {
            System.Console.WriteLine(string.Format(
                 "An error occurred while scanning the stream file {0}.\n\nThe disc may be copy-protected or damaged.\n\nDo you want to continue scanning the stream files?", streamFile.Name));

            return GetYorNdefaultY();
        }

        protected bool BDROM_StreamClipFileScanError(TSStreamClipFile streamClipFile, Exception ex)
        {
            System.Console.WriteLine(string.Format(
                "An error occurred while scanning the stream clip file {0}.\n\nThe disc may be copy-protected or damaged.\n\nDo you want to continue scanning the stream clip files?", streamClipFile.Name));

            return GetYorNdefaultY();
        }

        #endregion

        #region Scan BDROM

        private BackgroundWorker ScanBDROMWorker = null;

        private class ScanBDROMState
        {
            public long TotalBytes = 0;
            public long FinishedBytes = 0;
            public DateTime TimeStarted = DateTime.Now;
            public TSStreamFile StreamFile = null;
            public Dictionary<string, List<TSPlaylistFile>> PlaylistMap =
                new Dictionary<string, List<TSPlaylistFile>>();
            public Exception Exception = null;
        }
        
        public void AddStreamFilesInPlaylists(List<TSPlaylistFile> playlists)
        {
          
            System.Console.WriteLine("Preparing to the following...");
           
            streamFiles = new List<TSStreamFile>();
            List<string> streamNames = new List<string>();
            
            foreach(TSPlaylistFile playlist in playlists)
            {
                System.Console.WriteLine("Selected {0}", playlist.Name);

                foreach (TSStreamClip clip in playlist.StreamClips)
                {
                    if (!streamFiles.Contains(clip.StreamFile))
                    {
                        streamNames.Add(clip.StreamFile.Name);
                        streamFiles.Add(clip.StreamFile);
                    }
                }

                string delimeter = " + ";
                Console.WriteLine(String.Join(delimeter, streamNames));
            }
        }
         
        private void ScanBDROMWork()
        {
            ScanResult = new ScanBDROMResult();
            ScanResult.ScanException = new Exception("Scan is still running.");

            System.Threading.Timer timer = null;
            try
            {
                this.scanState = new ScanBDROMState();
                foreach (TSStreamFile streamFile in streamFiles)
                {
                    if (BDInfoSettings.EnableSSIF &&
                        streamFile.InterleavedFile != null)
                    {
                        scanState.TotalBytes += streamFile.InterleavedFile.FileInfo.Length;
                    }
                    else
                    {
                        scanState.TotalBytes += streamFile.FileInfo.Length;
                    }

                    if (!scanState.PlaylistMap.ContainsKey(streamFile.Name))
                    {
                        scanState.PlaylistMap[streamFile.Name] = new List<TSPlaylistFile>();
                    }

                    foreach (TSPlaylistFile playlist in BDROM.PlaylistFiles.Values)
                    {
                        playlist.ClearBitrates();

                        foreach (TSStreamClip clip in playlist.StreamClips)
                        {
                            if (clip.Name == streamFile.Name)
                            {
                                if (!scanState.PlaylistMap[streamFile.Name].Contains(playlist))
                                {
                                    scanState.PlaylistMap[streamFile.Name].Add(playlist);
                                }
                            }
                        }
                    }
                }

                //timer = new System.Threading.Timer(ScanBDROMEvent, scanState, 1000, 1000);

                foreach (TSStreamFile streamFile in streamFiles)
                {
                    scanState.StreamFile = streamFile;

                    Thread thread = new Thread(ScanBDROMThread);
                    thread.Start(scanState);
                    while (thread.IsAlive)
                    {
                        /*
                        if (ScanBDROMWorker.CancellationPending)
                        {
                            ScanResult.ScanException = new Exception("Scan was cancelled.");
                            thread.Abort();
                            return;
                        }
                         * */
                        Thread.Sleep(10);
                    }
                    scanState.FinishedBytes += streamFile.FileInfo.Length;
                    if (scanState.Exception != null)
                    {
                        ScanResult.FileExceptions[streamFile.Name] = scanState.Exception;
                    }
                }
                ScanResult.ScanException = null;
            }
            catch (Exception ex)
            {
                ScanResult.ScanException = ex;
            }
            finally
            {
                if (timer != null)
                {
                    timer.Dispose();
                }
            }
        }
        
        private void ScanBDROMThread(object parameter)
        {
            ScanBDROMState scanState = (ScanBDROMState)parameter;
            try
            {
                TSStreamFile streamFile = scanState.StreamFile;
                List<TSPlaylistFile> playlists = scanState.PlaylistMap[streamFile.Name];
                streamFile.Scan(playlists, true);
            }
            catch (Exception ex)
            {
                scanState.Exception = ex;
            }
        }
        
        private void ScanBDROMProgress()
        {
            try
            {
                if (scanState.StreamFile != null)
                {
                    System.Console.WriteLine("Scanning {0}...\r\n", scanState.StreamFile.DisplayName);
                }

                long finishedBytes = scanState.FinishedBytes;
                if (scanState.StreamFile != null)
                {
                    finishedBytes += scanState.StreamFile.Size;
                }

                double progress = ((double)finishedBytes / scanState.TotalBytes);
                int progressValue = (int)Math.Round(progress * 100);
                if (progressValue < 0) progressValue = 0;
                if (progressValue > 100) progressValue = 100;
                System.Console.WriteLine(progressValue);

                TimeSpan elapsedTime = DateTime.Now.Subtract(scanState.TimeStarted);
                TimeSpan remainingTime;
                if (progress > 0 && progress < 1)
                {
                    remainingTime = new TimeSpan(
                        (long)((double)elapsedTime.Ticks / progress) - elapsedTime.Ticks);
                }
                else
                {
                    remainingTime = new TimeSpan(0);
                }
                /*
                labelTimeElapsed.Text = string.Format(
                    "{0:D2}:{1:D2}:{2:D2}",
                    elapsedTime.Hours,
                    elapsedTime.Minutes,
                    elapsedTime.Seconds);

                labelTimeRemaining.Text = string.Format(
                    "{0:D2}:{1:D2}:{2:D2}",
                    remainingTime.Hours,
                    remainingTime.Minutes,
                    remainingTime.Seconds);

                UpdatePlaylistBitrates();
                 * */
            }
            catch { }
        }

        private void ScanBDROMCompleted(List<String> mplsList)
        {
            
            /*
            UpdatePlaylistBitrates();

            labelProgress.Text = "Scan complete.";
            progressBarScan.Value = 100;
            labelTimeRemaining.Text = "00:00:00";
             * */

            if (ScanResult.ScanException != null)
            {
                System.Console.WriteLine(string.Format("{0}", ScanResult.ScanException.Message));
            }
            else
            {
                if (ScanResult.FileExceptions.Count > 0)
                {
                    System.Console.WriteLine("Scan completed with errors (see report).");
                }
                else
                {
                    System.Console.WriteLine("Scan completed successfully.");
                }
                
                GenerateReport(mplsList);
            }
        }       
        
        #endregion
       
        public class ScanBDROMResult
        {
            public Exception ScanException = new Exception("Scan has not been run.");
            public Dictionary<string, Exception> FileExceptions = new Dictionary<string, Exception>();
        }

        private void ScanBDROMEvent(object state)
        {
            try
            {
                if (ScanBDROMWorker.IsBusy &&
                    !ScanBDROMWorker.CancellationPending)
                {
                    ScanBDROMWorker.ReportProgress(0, state);
                }
            }
            catch { }
        }

        static bool GetYorNdefaultY()
        {
            ConsoleKey response;

            do
            {
                while (Console.KeyAvailable) // Flushes the input queue.
                    Console.ReadKey();

                Console.Write("Y/n? ");
                response = Console.ReadKey().Key;
                Console.WriteLine(); // Breaks the line.
            } while (response != ConsoleKey.Y && response != ConsoleKey.N && response != ConsoleKey.Enter);

            /* 
             * Return true if the user responded with 'Y/Enter', otherwise false.
             * 
             * We know the response was either 'Y/Enter' or 'N', so we can assume 
             * the response is 'N' if it is not 'Y/Enter'.
             */
            return (response == ConsoleKey.Y || response == ConsoleKey.Enter);
        }

        static int getIntIndex(int min, int max)
        {
            String response;
            int resp = -1;
            do
            {
                while (Console.KeyAvailable)
                    Console.ReadKey();

                Console.Write("Select: ");
                response = Console.ReadLine();
                Console.WriteLine();

                try
                {
                    resp = int.Parse(response);
                }
                catch (Exception)
                {
                    Console.WriteLine("Invalid Input! ");
                }
            } while (resp > max || resp < min);

            return resp;
        }

        #region Generate report
        private void GenerateReport(List<String> mplsList)
        {
            System.Console.WriteLine("Please wait while we generate the report...");
           
            List<TSPlaylistFile> playlists = new List<TSPlaylistFile>();
            
            foreach (String item in mplsList)
            {
                if (BDROM.PlaylistFiles.ContainsKey(item))
                {
                    playlists.Add(BDROM.PlaylistFiles[item]);
                }
            }

            try
            {
                FormReport report = new FormReport();
                report.Generate(BDROM, playlists, ScanResult);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine(string.Format("{0}", (ex.Message)));
            }

        }
        #endregion 

        #region Play Lists        
        
        public static int ComparePlaylistFiles(TSPlaylistFile x, TSPlaylistFile y)
        {
            if (x == null && y == null)
            {
                return 0;
            }
            else if (x == null && y != null)
            {
                return 1;
            }
            else if (x != null && y == null)
            {
                return -1;
            }
            else
            {
                if (x.TotalLength > y.TotalLength)
                {
                    return -1;
                }
                else if (y.TotalLength > x.TotalLength)
                {
                    return 1;
                }
                else
                {
                    return x.Name.CompareTo(y.Name);
                }
            }
        }

        public TSPlaylistFile SelectPlayList()
        {
            if (BDROM == null) return null;

            bool hasHiddenTracks = false;

            List<List<TSPlaylistFile>> groups = new List<List<TSPlaylistFile>>();

            TSPlaylistFile[] sortedPlaylistFiles = new TSPlaylistFile[BDROM.PlaylistFiles.Count];
            BDROM.PlaylistFiles.Values.CopyTo(sortedPlaylistFiles, 0);
            Array.Sort(sortedPlaylistFiles, ComparePlaylistFiles);

            foreach (TSPlaylistFile playlist1 in sortedPlaylistFiles)
            {
                if (!playlist1.IsValid) continue;

                int matchingGroupIndex = 0;
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    List<TSPlaylistFile> group = groups[groupIndex];
                    foreach (TSPlaylistFile playlist2 in group)
                    {
                        if (!playlist2.IsValid) continue;

                        foreach (TSStreamClip clip1 in playlist1.StreamClips)
                        {
                            foreach (TSStreamClip clip2 in playlist2.StreamClips)
                            {
                                if (clip1.Name == clip2.Name)
                                {
                                    matchingGroupIndex = groupIndex + 1;
                                    break;
                                }
                            }
                            if (matchingGroupIndex > 0) break;
                        }
                        if (matchingGroupIndex > 0) break;
                    }
                    if (matchingGroupIndex > 0) break;
                }
                if (matchingGroupIndex > 0)
                {
                    groups[matchingGroupIndex - 1].Add(playlist1);
                }
                else
                {
                    groups.Add(new List<TSPlaylistFile> { playlist1 });
                }
            }

            System.Console.WriteLine(String.Format("{0,-4}{1,-7}{2,-15}{3,-10}{4,-16}{5,-16}\n", "#", "Group", "Playlist File", "Length", "Esitamted Bytes", "Measured Bytes"));
            int index = 1;
            Dictionary<int,TSPlaylistFile> plsDict = new Dictionary<int,TSPlaylistFile>();

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                List<TSPlaylistFile> group = groups[groupIndex];
                group.Sort(ComparePlaylistFiles);

                foreach (TSPlaylistFile playlist in group)
                {
                    if (!playlist.IsValid) continue;

                    plsDict[index] = playlist;

                    if (playlist.HasHiddenTracks)
                    {
                        hasHiddenTracks = true;
                    }

                 
                    String groupString = (groupIndex + 1).ToString();
                    //playlistIndex.Tag = groupIndex;

                    //String palylistString = playlist.Name);
                    //playlistName.Tag = playlist.Name;

                    TimeSpan playlistLengthSpan = new TimeSpan((long)(playlist.TotalLength * 10000000));
                    String length = string.Format(
                        "{0:D2}:{1:D2}:{2:D2}",
                        playlistLengthSpan.Hours,
                        playlistLengthSpan.Minutes,
                        playlistLengthSpan.Seconds);
                    ///playlistLength.Tag = playlist.TotalLength;

                    String fileSize;
                    if (BDInfoSettings.EnableSSIF &&
                        playlist.InterleavedFileSize > 0)
                    {
                        fileSize = playlist.InterleavedFileSize.ToString("N0");
                        //playlistSize.Tag = playlist.InterleavedFileSize;
                    }
                    else if (playlist.FileSize > 0)
                    {
                        fileSize = playlist.FileSize.ToString("N0");
                        //playlistSize.Tag = playlist.FileSize;
                    }
                    else
                    {
                        fileSize = "-";
                        ///playlistSize.Tag = playlist.FileSize;
                    }

                    String fileSize2;
                    if (playlist.TotalAngleSize > 0)
                    {
                        fileSize2 = (playlist.TotalAngleSize).ToString("N0");
                    }
                    else
                    {
                        fileSize2 = "-";
                    }
                    //playlistSize2.Tag = playlist.TotalAngleSize;

                    System.Console.WriteLine(String.Format("{0,-4:G}{1,-7}{2,-15}{3,-10}{4,-16}{5,-16}", index.ToString(), groupString, playlist.Name, length, fileSize, fileSize2));
                    index++;
                }
            }

            if (hasHiddenTracks)
            {
                System.Console.WriteLine("(*) Some playlists on this disc have hidden tracks. These tracks are marked with an asterisk.");
            }

            return plsDict[getIntIndex(1, index)];
        }

        #endregion
    }
}
