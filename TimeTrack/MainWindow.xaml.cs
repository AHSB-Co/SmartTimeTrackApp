using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Drawing;

namespace TimeTrack
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer idleTimer;
        private DispatcherTimer realTimeUpdateTimer;
        private DispatcherTimer saveTimer;
        private DateTime lastActivityTime;
        private TimeSpan totalActiveTime;
        private TimeSpan totalIdleTime;
        private bool isIdle;
        private DateTime currentDate;
        private XmlHelper xmlHelper;

        public MainWindow()
        {
            InitializeComponent();
            xmlHelper = new XmlHelper();

            // Initialize the times for today from the last session
            InitializeTimesForToday();

            // Set the current date to check for day transitions
            currentDate = DateTime.Now.Date;

            StartIdleTimer();
            StartRealTimeUpdateTimer();
            StartAutoSaveTimer(); // Automatically save every minute
        }

        // Initialize total active and idle times by reading the last session for today from XML
        private void InitializeTimesForToday()
        {
            totalActiveTime = TimeSpan.Zero;
            totalIdleTime = TimeSpan.Zero;
            lastActivityTime = DateTime.Now; // Initially set real-time tracking to zero

            string today = DateTime.Now.ToString("yyyy-MM-dd");
            XDocument sessions = xmlHelper.GetSessions();

            // Get the last session for today, if available
            var todaySession = sessions.Descendants("Session")
                .Where(s => s.Element("Date").Value.Contains(today))
                .OrderByDescending(s => s.Element("Date").Value)
                .FirstOrDefault();

            if (todaySession != null)
            {
                // Initialize total active and idle times with today's session
                double activeHours = double.Parse(todaySession.Element("ActiveTime").Value);
                double idleHours = double.Parse(todaySession.Element("IdleTime").Value);

                totalActiveTime = TimeSpan.FromHours(activeHours);
                totalIdleTime = TimeSpan.FromHours(idleHours);
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

        private void RealTimeUpdate_Tick(object sender, EventArgs e)
        {
            if (isIdle)
            {
                UpdateIdleTimeUI();
            }
            else
            {
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

            lblActiveTime.Content = string.Format("Active Time: {0:F2} hours", totalActiveTime.TotalHours);
            lblRealTimeActive.Content = string.Format("Real-Time Active Time: {0:hh\\:mm\\:ss}", realTimeActive);
            lblIdleTime.Content = string.Format("Idle Time: {0:F2} hours", totalIdleTime.TotalHours);
            lblRealTimeIdle.Content = "Real-Time Idle Time: 0:00:00";  // Reset idle time when active
        }

        private void UpdateIdleTimeUI()
        {
            TimeSpan realTimeIdle = DateTime.Now - lastActivityTime;

            lblIdleTime.Content = string.Format("Idle Time: {0:F2} hours", totalIdleTime.TotalHours);
            lblRealTimeIdle.Content = string.Format("Real-Time Idle Time: {0:hh\\:mm\\:ss}", realTimeIdle);
            lblActiveTime.Content = string.Format("Active Time: {0:F2} hours", totalActiveTime.TotalHours);
            lblRealTimeActive.Content = "Real-Time Active Time: 0:00:00";  // Reset active time when idle
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
        }

        private void ShowSessions_Click(object sender, RoutedEventArgs e)
        {
            XDocument sessions = xmlHelper.GetSessions();
            var sessionList = new List<Session>();

            foreach (XElement session in sessions.Descendants("Session"))
            {
                string date = session.Element("Date").Value;
                string activeTime = session.Element("ActiveTime").Value;
                string idleTime = session.Element("IdleTime").Value;
                sessionList.Add(new Session { Date = date, ActiveTime = activeTime, IdleTime = idleTime });
            }

            dataGridSessions.ItemsSource = sessionList;
        }

        private void ExportToCsv_Click(object sender, RoutedEventArgs e)
        {
            XDocument sessions = xmlHelper.GetSessions();
            string csv = "Date,Active Time,Idle Time\n";

            foreach (XElement session in sessions.Descendants("Session"))
            {
                string date = session.Element("Date").Value;
                string activeTime = session.Element("ActiveTime").Value;
                string idleTime = session.Element("IdleTime").Value;
                csv += string.Format("{0},{1},{2}\n", date, activeTime, idleTime);
            }

            File.WriteAllText("TimeTrackingSessions.csv", csv);
            System.Windows.MessageBox.Show("Data exported to TimeTrackingSessions.csv");
        }

        private void ShowNotification(string title, string message)
        {
            NotifyIcon notifyIcon = new NotifyIcon();
            notifyIcon.Visible = true;
            notifyIcon.Icon = SystemIcons.Information;
            notifyIcon.BalloonTipTitle = title;
            notifyIcon.BalloonTipText = message;
            notifyIcon.ShowBalloonTip(3000);  // Show for 3 seconds
        }

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
        public string ActiveTime { get; set; }
        public string IdleTime { get; set; }
    }
}
