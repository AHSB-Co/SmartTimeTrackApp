using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Xml.Linq;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Reflection; // For loading the embedded resource
using IWshRuntimeLibrary;
using WpfApplication = System.Windows.Application;

namespace TimeTrack
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer idleTimer;
        private DispatcherTimer realTimeUpdateTimer;
        private DispatcherTimer saveTimer;
        private DispatcherTimer retryTimer;  // Retry timer for syncing failed sessions
        private DateTime lastActivityTime;
        private TimeSpan totalActiveTime;
        private TimeSpan totalIdleTime;
        private bool isIdle;
        private DateTime currentDate;
        private XmlHelper xmlHelper;
        private HttpClient _httpClient;
        private string _username = "user1";  // Dynamic user for multiple users
        private const int MinutesInDay = 1440;  // 24 hours * 60 minutes
        private double minuteWidth;  // Width for each minute on the canvas
        private DispatcherTimer updateTimer;  // Timer to auto-update visualization every minute
        private NotifyIcon _notifyIcon;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTrayIcon();
            EnsureAutoStart();

            minuteWidth = TimeCanvas.ActualWidth / MinutesInDay;
            xmlHelper = new XmlHelper();
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("http://100.99.99.12/")  // Backend server IP
            };
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Initialize the times for today from the last session
            InitializeTimesForToday();

            // Set the current date to check for day transitions
            currentDate = DateTime.Now.Date;

            StartIdleTimer();
            StartRealTimeUpdateTimer();
            StartAutoSaveTimer(); // Automatically save every minute
            StartRetryTimer();    // Retry unsent sessions every few minutes
            StartAutoUpdateTimer();// Start the timer to automatically update the visualization every minute
        }

        private void EnsureAutoStart()
        {
            string startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string shortcutPath = System.IO.Path.Combine(startupFolderPath, "TimeTrackApp.lnk");


            if (!System.IO.File.Exists(shortcutPath))
            {
                CreateShortcut(shortcutPath);
            }
        }

        private void CreateShortcut(string shortcutPath)
        {
            string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
            shortcut.Description = "TimeTrack Application";
            shortcut.TargetPath = appPath;
            shortcut.Save();
        }

        // Initialize the system tray icon
        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Visible = true,
                Text = "TimeTrack"
            };

            // Set the context menu with an option to show and exit
            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            _notifyIcon.ContextMenuStrip.Items.Add("Show", null, ShowApp);
            _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, ExitApp);

            // Double-clicking the tray icon shows the window
            _notifyIcon.DoubleClick += (sender, args) => ShowApp(sender, args);
        }

        // Load the icon for the tray from resources
        private System.Drawing.Icon LoadTrayIcon()
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = "TimeTrack.AppIcon.ico"; // Adjust based on your namespace and resource path
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    return new System.Drawing.Icon(stream);
                }
                else
                {
                    throw new FileNotFoundException("Icon resource not found.");
                }
            }
        }

        // Hide the window and minimize to tray
        private void HideButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _notifyIcon.Visible = true;
        }

        // Show the application when the tray icon is double-clicked or "Show" is clicked in the context menu
        private void ShowApp(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
        }

        // Exit the application
        private void ExitApp(object sender, EventArgs e)
        {
            _notifyIcon.Visible = false; // Hide tray icon
            WpfApplication.Current.Shutdown();
        }

        // Override closing behavior to minimize to tray
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Minimize to tray instead of closing the window
            e.Cancel = true;
            this.Hide();
        }

        private async void InitializeTimesForToday()
        {
            totalActiveTime = TimeSpan.Zero;
            totalIdleTime = TimeSpan.Zero;
            lastActivityTime = DateTime.Now; // Initially set real-time tracking to zero

            // Get today's date in "yyyy-MM-dd" format
            string today = DateTime.Now.ToString("yyyy-MM-dd");

            try
            {
                // Attempt to load from server first
                var sessionData = await GetTodaySessionFromServer();
                if (sessionData != null)
                {
                    // If the session is from today, use the values from the server
                    totalActiveTime = TimeSpan.FromHours(sessionData.ActiveTime);
                    totalIdleTime = TimeSpan.FromHours(sessionData.IdleTime);
                }
                else
                {
                    // Load from local XML if no session found on the server
                    XDocument sessions = xmlHelper.GetSessions();

                    // Get the last session from the XML, if available
                    var lastSession = sessions.Descendants("Session")
                        .OrderByDescending(s => s.Element("Date").Value)
                        .FirstOrDefault();

                    if (lastSession != null)
                    {
                        // Check if the last session's date matches today
                        string sessionDate = lastSession.Element("Date").Value.Substring(0, 10); // Extract date part
                        if (sessionDate == today)
                        {
                            // If the session is from today, use the values from the XML
                            double activeHours = double.Parse(lastSession.Element("ActiveTime").Value);
                            double idleHours = double.Parse(lastSession.Element("IdleTime").Value);

                            totalActiveTime = TimeSpan.FromHours(activeHours);
                            totalIdleTime = TimeSpan.FromHours(idleHours);
                        }
                        else
                        {
                            // If the session is not from today, initialize active/idle times to zero
                            totalActiveTime = TimeSpan.Zero;
                            totalIdleTime = TimeSpan.Zero;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error initializing times: " + ex.Message);
            }
        }

        private void StartIdleTimer()
        {
            idleTimer = new DispatcherTimer();
            idleTimer.Interval = TimeSpan.FromSeconds(1); // Check idle state every second
            idleTimer.Tick += CheckIdleTime;
            idleTimer.Start();
        }

        private void StartRealTimeUpdateTimer()
        {
            realTimeUpdateTimer = new DispatcherTimer();
            realTimeUpdateTimer.Interval = TimeSpan.FromSeconds(1); // Update real-time display every second
            realTimeUpdateTimer.Tick += RealTimeUpdate_Tick;
            realTimeUpdateTimer.Start();
        }

        private void StartAutoSaveTimer()
        {
            saveTimer = new DispatcherTimer();
            saveTimer.Interval = TimeSpan.FromMinutes(1); // Automatically save session data every 1 minute
            saveTimer.Tick += SaveAutomatically;
            saveTimer.Start();
        }

        private void StartRetryTimer()
        {
            retryTimer = new DispatcherTimer();
            retryTimer.Interval = TimeSpan.FromMinutes(5); // Retry syncing failed sessions every 5 minutes
            retryTimer.Tick += RetryFailedSyncs;
            retryTimer.Start();
        }

        private void RealTimeUpdate_Tick(object sender, EventArgs e)
        {
            if (isIdle)
            {
                UpdateIdleTimeUI();
            }
            else
            {
                // Check if at least 1 minute has passed since last activity update
                TimeSpan timeSinceLastActivity = DateTime.Now - lastActivityTime;
                if (timeSinceLastActivity.TotalMinutes >= 1)
                {
                    totalActiveTime += TimeSpan.FromMinutes(1); // Add 1 minute to total active time
                    lastActivityTime = DateTime.Now;  // Reset the last activity time to now
                }

                UpdateActiveTimeUI();
            }

            // Check if a new day has started and reset active/idle times
            CheckForNewDay();
        }

        // Check if the current date has changed (i.e., the day has ended) and reset the times if necessary
        private void CheckForNewDay()
        {
            if (DateTime.Now.Date != currentDate)
            {
                currentDate = DateTime.Now.Date; // Update the current date
                ResetTimesForNewDay(); // Reset times
            }
        }

        // Reset active and idle times to zero when a new day starts
        private void ResetTimesForNewDay()
        {
            totalActiveTime = TimeSpan.Zero;
            totalIdleTime = TimeSpan.Zero;
            lastActivityTime = DateTime.Now;

            UpdateActiveTimeUI();
            UpdateIdleTimeUI();
        }

        private void CheckIdleTime(object sender, EventArgs e)
        {
            LASTINPUTINFO lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

            if (GetLastInputInfo(ref lastInputInfo))
            {
                uint idleTime = (uint)Environment.TickCount - lastInputInfo.dwTime;
                if (idleTime >= 10000)  // Mark idle if no activity for 10 seconds (10000 ms)
                {
                    OnIdleDetected();
                }
                else if (isIdle)
                {
                    OnInputDetected();
                }
            }
        }

        private void OnIdleDetected()
        {
            if (!isIdle)
            {
                isIdle = true;

                // Stop tracking active time
                if (lastActivityTime != DateTime.MinValue)
                {
                    totalActiveTime += DateTime.Now - lastActivityTime;
                }

                lastActivityTime = DateTime.Now;  // Mark the time user went idle
                UpdateIdleTimeUI();
            }
        }

        private void OnInputDetected()
        {
            if (isIdle)
            {
                // Stop tracking idle time
                totalIdleTime += DateTime.Now - lastActivityTime;

                isIdle = false;  // Mark user as active again
            }

            lastActivityTime = DateTime.Now;  // Mark the time user became active
            UpdateActiveTimeUI();
        }

        private void UpdateActiveTimeUI()
        {
            TimeSpan realTimeActive = DateTime.Now - lastActivityTime;
            
            lblTotalActiveTime.Content = string.Format("Active Time: {0:F2} hours", totalActiveTime.TotalHours);
            lblTotalIdleTime.Content = string.Format("Real-Time Active Time: {0:hh\\:mm\\:ss}", realTimeActive);
        }

        private void UpdateIdleTimeUI()
        {
            TimeSpan realTimeIdle = DateTime.Now - lastActivityTime;

            lblTotalActiveTime.Content = string.Format("Idle Time: {0:F2} hours", totalIdleTime.TotalHours);
            lblTotalIdleTime.Content = string.Format("Real-Time Idle Time: {0:hh\\:mm\\:ss}", realTimeIdle);
        }

        private void SaveAutomatically(object sender, EventArgs e)
        {
            SaveSession();
        }

        private void SaveSession()
        {
            string sessionDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Save current accumulated time
            double activeTime = Math.Round(totalActiveTime.TotalHours, 2);
            double idleTime = Math.Round(totalIdleTime.TotalHours, 2);

            xmlHelper.SaveSession(sessionDate, activeTime, idleTime);

            // Try to sync with backend server
            try
            {
                SyncSessionWithServer(sessionDate, activeTime, idleTime).ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        Console.WriteLine("Session data synced successfully with server.");
                    }
                    else
                    {
                        // If the sync fails, save the session to the retry queue
                        xmlHelper.SaveToRetryQueue(sessionDate, activeTime, idleTime);
                        Console.WriteLine("Session data saved to retry queue due to sync failure.");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error syncing session: " + ex.Message);
                // Save the session to the retry queue in case of any failure
                xmlHelper.SaveToRetryQueue(sessionDate, activeTime, idleTime);
            }
        }

        private async Task SyncSessionWithServer(string sessionDate, double activeTime, double idleTime)
        {
            try
            {
                var sessionData = new
                {
                    Username = _username,
                    Date = sessionDate,
                    ActiveTime = activeTime,
                    IdleTime = idleTime
                };

                string jsonData = JsonConvert.SerializeObject(sessionData);
                StringContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync("api/timesessions", content);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(string.Format("Failed to sync session with server. Status code: {0}", response.StatusCode));
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine("HTTP request error: " + ex.Message);
                throw;  // Re-throw to ensure the task can be caught in SaveSession's ContinueWith.
            }
            catch (Exception ex)
            {
                Console.WriteLine("General error occurred while syncing session: " + ex.Message);
                throw;
            }
        }

        private async Task<TimeSession> GetTodaySessionFromServer()
        {
            try
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                HttpResponseMessage response = await _httpClient.GetAsync(string.Format("api/timesessions/today?username={0}", _username));

                if (response.IsSuccessStatusCode)
                {
                    string responseData = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<TimeSession>(responseData);
                }
                else
                {
                    Console.WriteLine("Error retrieving session data from server: " + response.StatusCode);
                    return null;
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine("HTTP request error: " + ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("General error occurred while retrieving session: " + ex.Message);
                return null;
            }
        }

        // Retry syncing failed sessions
        private void RetryFailedSyncs(object sender, EventArgs e)
        {
            var failedSessions = xmlHelper.GetRetryQueue();
            foreach (var session in failedSessions)
            {
                SyncSessionWithServer(session.Date, session.ActiveTime, session.IdleTime).ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        // If successfully synced, remove from retry queue
                        xmlHelper.RemoveFromRetryQueue(session);
                        //Console.WriteLine($"Successfully retried sync for session on {session.Date}");
                    }
                    else
                    {
                        //Console.WriteLine($"Failed to retry sync for session on {session.Date}");
                    }
                });
            }
        }

        private void StartAutoUpdateTimer()
        {
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromMinutes(1); // Update every minute
            updateTimer.Tick += (s, e) => UpdateSessionData(); // On tick, update the session data
            updateTimer.Start(); // Start the timer immediately

            // Initial call to show data without waiting for the first tick
            UpdateSessionData();
        }

        private void UpdateSessionData()
        {
            // Parse the session data from XML
            var sessions = ParseSessionsFromXml("TimeTrackSessions.xml");

            // Create arrays to track active and idle minutes
            bool[] activeMinutes = new bool[MinutesInDay];
            bool[] idleMinutes = new bool[MinutesInDay];

            // Loop through each session and calculate the active/idle times
            for (int i = 1; i < sessions.Length; i++)
            {
                // Convert the session's start time to the minute of the day
                DateTime previousSessionTime = DateTime.Parse(sessions[i - 1].Date);
                DateTime currentSessionTime = DateTime.Parse(sessions[i].Date);

                int currentSessionMinute = currentSessionTime.Hour * 60 + currentSessionTime.Minute;
                double activeDifference = sessions[i].ActiveTime - sessions[i - 1].ActiveTime;
                double idleDifference = sessions[i].IdleTime - sessions[i - 1].IdleTime;

                // If both active and idle time increased, choose the larger difference
                if (activeDifference > 0 && idleDifference > 0)
                {
                    if (activeDifference > idleDifference)
                    {
                        activeMinutes[currentSessionMinute] = true;
                    }
                    else
                    {
                        idleMinutes[currentSessionMinute] = true;
                    }
                }
                // If only active time increased, mark as active
                else if (activeDifference > 0)
                {
                    activeMinutes[currentSessionMinute] = true;
                }
                // If only idle time increased, mark as idle
                else if (idleDifference > 0)
                {
                    idleMinutes[currentSessionMinute] = true;
                }
            }

            // Visualize the active and idle times on the canvas
            VisualizeTimeSlots(activeMinutes, idleMinutes);
        }

        private void VisualizeTimeSlots(bool[] activeMinutes, bool[] idleMinutes)
        {
            // Clear the canvas before drawing new session data
            TimeCanvas.Children.Clear();

            // Get canvas width and calculate the width for each minute slot
            minuteWidth = TimeCanvas.ActualWidth / MinutesInDay;

            // Loop through each minute of the day and draw corresponding rectangles
            for (int minute = 0; minute < MinutesInDay; minute++)
            {
                Rectangle rect = new Rectangle
                {
                    Width = minuteWidth,
                    Height = TimeCanvas.ActualHeight
                };

                // Set color based on whether the minute is active or idle
                if (activeMinutes[minute])
                {
                    rect.Fill = Brushes.Green;  // Active session
                }
                else if (idleMinutes[minute])
                {
                    rect.Fill = Brushes.Red;  // Idle session
                }
                else
                {
                    rect.Fill = Brushes.Black;  // Missing data (no session)
                }

                // Add the rectangle to the canvas at the appropriate position (left to right)
                Canvas.SetLeft(rect, minute * minuteWidth);  // Position the rectangle from left to right
                TimeCanvas.Children.Add(rect);  // Add to the canvas
            }
        }

        private Session[] ParseSessionsFromXml(string filePath)
        {
            XDocument doc = XDocument.Load(filePath);
            return doc.Descendants("Session")
                      .Select(s => new Session
                      {
                          Date = s.Element("Date").Value,
                          ActiveTime = double.Parse(s.Element("ActiveTime").Value),
                          IdleTime = double.Parse(s.Element("IdleTime").Value)
                      }).ToArray();
        }

        // Method to load session from local XML file
        private void LoadSessionFromLocalXml(ref TimeSpan totalActiveTime, ref TimeSpan totalIdleTime)
        {
            try
            {
                XDocument sessions = xmlHelper.GetSessions();
                string today = DateTime.Now.ToString("yyyy-MM-dd");

                // Find session for today
                var todaySession = sessions.Descendants("Session")
                    .Where(s => s.Element("Date").Value.Contains(today))
                    .OrderByDescending(s => s.Element("Date").Value)
                    .FirstOrDefault();

                if (todaySession != null)
                {
                    // Get total active and idle times from XML
                    double activeHours = double.Parse(todaySession.Element("ActiveTime").Value);
                    double idleHours = double.Parse(todaySession.Element("IdleTime").Value);

                    totalActiveTime = TimeSpan.FromHours(activeHours);
                    totalIdleTime = TimeSpan.FromHours(idleHours);
                }
                else
                {
                    // If no session data is found, initialize to zero
                    totalActiveTime = TimeSpan.Zero;
                    totalIdleTime = TimeSpan.Zero;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading session data from XML: " + ex.Message);
            }
        }


        //private void ShowNotification(string title, string message)
        //{
        //    NotifyIcon notifyIcon = new NotifyIcon();
        //    notifyIcon.Visible = true;
        //    notifyIcon.Icon = SystemIcons.Information;
        //    notifyIcon.BalloonTipTitle = title;
        //    notifyIcon.BalloonTipText = message;
        //    notifyIcon.ShowBalloonTip(3000);  // Show notification for 3 seconds
        //}

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }
    }

    public class Session
    {
        public string Date { get; set; }
        public double ActiveTime { get; set; }
        public double IdleTime { get; set; }
    }

    public class TimeSession
    {
        public string Username { get; set; }
        public string Date { get; set; }
        public double ActiveTime { get; set; }
        public double IdleTime { get; set; }
    }
}
