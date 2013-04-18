// EventID service
// Version 1.0
// Licensed under GPL V3
// Compile as service.exe and bundle with our installer
// or compile and use the SC command to install as a service
// if installing as a service manually make sure all registry entries exist before starting service.


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.ServiceProcess;
using System.Xml;
using System.Net;
using System.Net.NetworkInformation;
using System.Linq;
using System.Xml.Linq;
using System.IO;
using Microsoft.Win32;
using System.Threading;
using System.Text;




namespace EID
{
    public partial class EID : ServiceBase
    {
        public EID()
        {
            InitializeComponent();
        }
        // Global variable keeprunning
        // volatile so it can be accessed across threads
        // while true service is running
        private volatile bool keeprunning;

        protected override void OnStart(string[] args)
        {
            //
            // Startup service
            // spin off new thread for service to run in
            //
            ThreadStart ts = new ThreadStart( this.start_main);
            Thread m_thread = new Thread(ts);
            m_thread.Start();
        }

        protected override void OnStop()
        {
            //
            // Stopping service
            // setting global variable keeprunning to false.
            //
            keeprunning = false;
        }

        public static string Getdomain()
        {
            // returns domain name of this computer
            var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            return string.Format("{0}", ipProperties.DomainName);
        }
        public static string Gethost()
        {
            // returns host name of this computer
            var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            return string.Format("{0}", ipProperties.HostName);
        }
        public string[] check_reg(string host, string domain, int interval)
        {
            // startup check
            // read values out of registry
            // if they do not exist let's create them
            // with default values
            string[] returnval = new string[8];
            const string keyroot = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Squidworks\\EventID";
            try
            {
                string temp = Registry.GetValue(keyroot, "hostname", "").ToString();
                if (temp != "")
                {
                    returnval[0] = temp;
                }
                temp = Registry.GetValue(keyroot, "domain", "").ToString();
                if (temp != "")
                {
                    returnval[1] = temp;
                }
                temp = Registry.GetValue(keyroot, "interval", "").ToString();
                if (temp != "")
                {
                    returnval[2] = temp;
                }
                temp = Registry.GetValue(keyroot, "username", "").ToString();
                if (temp != "")
                {
                    returnval[3] = temp;
                }
                temp = Registry.GetValue(keyroot, "password", "").ToString();
                if (temp != "")
                {
                    returnval[4] = temp;
                }
                temp = Registry.GetValue(keyroot, "debug", "").ToString();
                if (temp != "")
                {
                    returnval[5] = temp;
                }
                temp = Registry.GetValue(keyroot, "debug log path", "").ToString();
                if (temp != "")
                {
                    returnval[6] = temp;
                }
                temp = Registry.GetValue(keyroot, "Event ID URL", "").ToString();
                if (temp != "")
                {
                    returnval[7] = temp;
                }
            }
            catch
            {
                returnval[0] = host;
                returnval[1] = domain;
                returnval[2] = interval.ToString();
                returnval[3] = "";
                returnval[4] = "";
                returnval[5] = "false";
                returnval[6] = @"c:\temp\eid.txt";
                returnval[7] = @"";
            }
            return returnval;
        }
        public void start_main()
        {
            string cs = "EventID";
            bool debugging = false;
            string debugpath = @"c:\temp\eid.txt";
            string eidusername = "";
            string eidpassword = "";
            string url = @"";
            if (!EventLog.SourceExists(cs))
                EventLog.CreateEventSource(cs, "Application"); 
            string host = Gethost();
            string domain = "";
            int interval = 15;   
            domain = Getdomain();
            if (domain == "")
            {
                domain = "Workgroup";
            }
            // 0 = host
            // 1 = domain
            // 2 = interval for event scanning
            // 3 = username override
            // 4 = password override
            // 5 = debugging true/false
            // 6 = debug log path
            // 7 = url override
            string[] nfo = check_reg(host, domain, interval);
            host = nfo[0];
            domain = nfo[1];
            interval = int.Parse(nfo[2]);
            double tempdouble = (double)interval;
            DateTime last_run = DateTime.Now.AddMinutes(-tempdouble);
            DateTime nextrun = DateTime.Now.AddMinutes(tempdouble);
            if (nfo[3] != "") { eidusername = nfo[3]; }
            if (nfo[4] != "") { eidpassword = nfo[4]; }
            if (nfo[5].ToUpper() == "TRUE")
            {
                debugging = true;
                if (nfo[6] != "") {debugpath = nfo[6];}
            }
            if (nfo[7] != "") { url = nfo[7]; }
            if (debugging)
            {
                update_debug_log("NEXT RUN " + nextrun.ToString(),debugpath);
                update_debug_log("LAST RUN " + last_run.ToString(), debugpath);
                update_debug_log("URL " + url, debugpath);
                update_debug_log("Username " + eidusername, debugpath);
                update_debug_log("Password " + eidpassword, debugpath);
            }
            if (nfo[7] != "") { url = nfo[7]; }
            keeprunning = true;
            while (keeprunning)
            {
                if (DateTime.Now > nextrun)
                {
                    if (debugging) { update_debug_log("Starting scan", debugpath); }
                    DateTime startscantime = DateTime.Now;
                    EventLog eventlog = new EventLog();
                    EventLog[] log = EventLog.GetEventLogs();
                    List<string> eventid = new List<string>();
                    List<string> source = new List<string>();
                    List<string> message = new List<string>();
                    List<string> datetimes = new List<string>();
                    List<string> logname = new List<string>();
                    for (int x = 0; x < log.Length; x++)
                    {
                        try
                        {
                            eventlog.Log = log[x].LogDisplayName.ToString();
                            eventlog.MachineName = ".";
                            var eventList = new List<EventLogEntry>();
                            foreach (EventLogEntry entry in eventlog.Entries)
                            {
                                eventList.Add(entry);
                            }
                            eventList.Reverse();
                            for (int y = 0; y < eventList.Count; y++)
                            {
                                DateTime testdate = eventList[y].TimeGenerated;
                                if (last_run > testdate)
                                {
                                    if (debugging) { update_debug_log("BREAK " + last_run.ToString() + " is larger than " + testdate.ToString(), debugpath); }
                                    break;
                                }
                                if ((eventList[y].EntryType != EventLogEntryType.Information) && (eventList[y].EntryType != EventLogEntryType.SuccessAudit))
                                {
                                    eventid.Add(eventList[y].EventID.ToString());
                                    source.Add(eventList[y].Source.ToString());
                                    message.Add(eventList[y].Message.ToString());
                                    datetimes.Add(eventList[y].TimeGenerated.ToString());
                                    logname.Add(log[x].LogDisplayName.ToString());
                                }
                            }
                        }
                        catch(Exception e)
                        {
                            if (debugging)
                            {
                                update_debug_log("Failed to read event log  # " + x.ToString(),debugpath);
                                update_debug_log("Execption message " + e.Message.ToString(),debugpath);
                            }
                        }
                    }
                    eventlog.Dispose();
                    XmlDocument doc = new XmlDocument();
                    XmlNode docNode = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
                    doc.AppendChild(docNode);
                    XmlNode reportNode = doc.CreateElement("report");
                    doc.AppendChild(reportNode);
                    for (int i = 0; i < eventid.Count; i++)
                    {
                        XmlNode eventnode = doc.CreateElement("event");
                        XmlAttribute eid = doc.CreateAttribute("ID");
                        eid.Value = eventid[i];
                        XmlAttribute src = doc.CreateAttribute("SOURCE");
                        src.Value = source[i];
                        XmlAttribute log1 = doc.CreateAttribute("LOG");
                        log1.Value = logname[i];
                        XmlAttribute tme = doc.CreateAttribute("LOGTIME");
                        tme.Value = datetimes[i];
                        XmlAttribute msg = doc.CreateAttribute("MESSAGE");
                        msg.Value = message[i];
                        XmlAttribute hst = doc.CreateAttribute("HOST");
                        hst.Value = host;
                        XmlAttribute dmn = doc.CreateAttribute("DOMAIN");
                        dmn.Value = domain;
                        eventnode.Attributes.Append(eid);
                        eventnode.Attributes.Append(src);
                        eventnode.Attributes.Append(log1);
                        eventnode.Attributes.Append(tme);
                        eventnode.Attributes.Append(hst);
                        eventnode.Attributes.Append(dmn);
                        eventnode.Attributes.Append(msg);
                        reportNode.AppendChild(eventnode);
                    }
                    string xmldoc = doc.InnerXml.ToString();
                    if (debugging)
                    {
                        string tempfile = debugpath + ".xml";
                        doc.Save(tempfile);
                    }
                    try
                    {
                        const string keyroot = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Squidworkds\\EventID";
                        WebClient wc1 = new WebClient();
                        wc1.Credentials = new NetworkCredential(eidusername, eidpassword);
                        wc1.Headers["Accept"] = "/";
                        wc1.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
                        string url_return = wc1.UploadString(url, xmldoc);
                        nextrun = DateTime.Now.AddMinutes(tempdouble);
                        last_run = startscantime;
                        if (debugging) { update_debug_log("Uploaded XML to " + url, debugpath); update_debug_log("RETURN " + url_return, debugpath); }
                        if (url_return.Contains("not a client")) { remove_self(); }
                        if (url_return.Contains("domain="))
                        {
                            string[] tempvals = url_return.Split('=');
                            Registry.SetValue(keyroot, "domain", tempvals[1]);
                            domain = tempvals[1];
                        }
                        if (url_return.Contains("hostname="))
                        {
                            string[] tempvals = url_return.Split('=');
                            Registry.SetValue(keyroot, "hostname", tempvals[1]);
                            host = tempvals[1];
                        }
                        if (url_return.Contains("interval="))
                        {
                            string[] tempvals = url_return.Split('=');
                            Registry.SetValue(keyroot, "interval", tempvals[1]);
                            interval = int.Parse(tempvals[1]);
                        }
                    }
                    catch
                    {
                        if (debugging)
                        {
                            update_debug_log("An error occured while uploading to " + url, debugpath);
                            update_debug_log("Username " + eidusername, debugpath);
                            update_debug_log("Password " + eidpassword, debugpath);
                        }
                    }
                }
                Thread.Sleep(1000);
            }
        }
        public void update_debug_log(string text, string filename)
        {
            try
            {
                TextWriter debug = new StreamWriter(filename, true);
                debug.WriteLine(DateTime.Now.ToString() + "  -  " + text);
                debug.Close();
            }
            catch { }
        }
        public void remove_self()
        {
            // this routine should remove this service
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "sc.exe";
            startInfo.Arguments = "delete EventID";
            process.StartInfo = startInfo;
            process.Start();
            startInfo.FileName = "sc.exe";
            startInfo.Arguments = "stop EventID";
            process.StartInfo = startInfo;
            process.Start();
        }
    }
}
