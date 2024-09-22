using System;
using System.IO;
using System.Xml.Linq;

public class XmlHelper
{
    private string filePath;

    public XmlHelper()
    {
        filePath = "timetracking.xml";

        // Ensure the XML file exists or create an empty structure
        if (!File.Exists(filePath))
        {
            XDocument doc = new XDocument(new XElement("TimeTrackingSessions"));
            doc.Save(filePath);
        }
    }

    // Save a session to the XML file
    public void SaveSession(string sessionDate, double activeTime, double idleTime)
    {
        XDocument doc = XDocument.Load(filePath);
        XElement root = doc.Element("TimeTrackingSessions");

        XElement newSession = new XElement("Session",
            new XElement("Date", sessionDate),
            new XElement("ActiveTime", activeTime),
            new XElement("IdleTime", idleTime)
        );

        root.Add(newSession);
        doc.Save(filePath);
    }

    // Retrieve all sessions
    public XDocument GetSessions()
    {
        return XDocument.Load(filePath);
    }
}
