using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;

namespace HTPinger
{
	public class HTPService : ServiceBase
	{
		public const string HTServiceName = "HyteraRAS";
		public const string HTDescription = "Hytera RAS";
		private MultiPinger Png = null;
		private string[] Addresses = null;
		private string Community = string.Empty;
		private int TimeOut = 3000;
		private int Delay = 30000;
		
		public HTPService()
		{
			InitializeComponent();

			try {
				using (RegistryKey RKey = Registry.LocalMachine.OpenSubKey("Software\\WOW6432Node\\" + HTServiceName)) {
        			if (RKey != null) {
            			string  IPs = RKey.GetValue("IPs", string.Empty).ToString();
						Addresses = IPs != String.Empty ? IPs.Trim().Split(';') : new string[0];
            			Community = RKey.GetValue("Community", string.Empty).ToString();
            			TimeOut = int.Parse(RKey.GetValue("TimeOut", 3000).ToString());
            			Delay = int.Parse(RKey.GetValue("Delay", 30000).ToString());
            		}
    			}
			}
			catch (Exception Ex) {
				// disable once DoNotCallOverridableMethodsInConstructor
				this.EventLog.WriteEntry("Ошибка службы Hytera RAS: " + Ex.Message.ToString(), EventLogEntryType.Error);
				return;
			}
			
			// disable once DoNotCallOverridableMethodsInConstructor
			this.Png = new MultiPinger(this.EventLog);
		}
		
		private void InitializeComponent()
		{
			this.ServiceName = HTServiceName;
			this.EventLog.Source = HTServiceName;
			this.EventLog.Log = HTDescription;
		}
		
		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose(bool disposing)
		{
			// TODO: Add cleanup code here (if required)
			base.Dispose(disposing);
		}
		
		/// <summary>
		/// Start this service.
		/// </summary>
		protected override void OnStart(string[] args)
		{
           	foreach (string Addr in Addresses) {
				this.Png.AddAddress(Addr.Trim(), TimeOut, Delay, Community);
			}

			this.Png.Start();
		}
		
		/// <summary>
		/// Stop this service.
		/// </summary>
		protected override void OnStop()
		{
			this.Png.Stop();
		}
	}
}
