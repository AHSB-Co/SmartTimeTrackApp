using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TimeTrack
{
    public class XmlHelper
    {
        private readonly string xmlFilePath = "TimeTrackSessions.xml";
        private readonly string retryQueueFilePath = "RetryQueue.xml";

        public XmlHelper()
        {
            if (!File.Exists(xmlFilePath))
            {
                XDocument doc = new XDocument(new XElement("Sessions"));
                doc.Save(xmlFilePath);
            }

            if (!File.Exists(retryQueueFilePath))
            {
                XDocument doc = new XDocument(new XElement("RetryQueue"));
                doc.Save(retryQueueFilePath);
            }
        }

        // Save session data to XML
        public void SaveSession(string sessionDate, double activeTime, double idleTime)
        {
            XDocument doc = XDocument.Load(xmlFilePath);
            XElement root = doc.Element("Sessions");

            root.Add(new XElement("Session",
                new XElement("Date", sessionDate),
                new XElement("ActiveTime", activeTime),
                new XElement("IdleTime", idleTime)
            ));

            doc.Save(xmlFilePath);
        }

        // Save a failed session to the retry queue
        public void SaveToRetryQueue(string sessionDate, double activeTime, double idleTime)
        {
            XDocument doc = XDocument.Load(retryQueueFilePath);
            XElement root = doc.Element("RetryQueue");

            root.Add(new XElement("Session",
                new XElement("Date", sessionDate),
                new XElement("ActiveTime", activeTime),
                new XElement("IdleTime", idleTime)
            ));

            doc.Save(retryQueueFilePath);
        }

        // Retrieve all sessions
        public XDocument GetSessions()
        {
            return XDocument.Load(xmlFilePath);
        }

        // Retrieve the retry queue
        public List<Session> GetRetryQueue()
        {
            XDocument doc = XDocument.Load(retryQueueFilePath);
            return doc.Descendants("Session")
                .Select(s => new Session
                {
                    Date = s.Element("Date").Value,
                    ActiveTime = double.Parse(s.Element("ActiveTime").Value),
                    IdleTime = double.Parse(s.Element("IdleTime").Value)
                })
                .ToList();
        }

        // Remove a session from the retry queue once it has been successfully synced
        public void RemoveFromRetryQueue(Session session)
        {
            XDocument doc = XDocument.Load(retryQueueFilePath);
            var sessionElement = doc.Descendants("Session")
                .FirstOrDefault(s => s.Element("Date").Value == session.Date &&
                                     double.Parse(s.Element("ActiveTime").Value) == session.ActiveTime &&
                                     double.Parse(s.Element("IdleTime").Value) == session.IdleTime);

            if (sessionElement != null)
            {
                sessionElement.Remove();
                doc.Save(retryQueueFilePath);
            }
        }
    }
}
