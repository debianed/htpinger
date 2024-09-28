using System;
using System.Configuration.Install;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;

namespace HTPinger
{
	static class Program
	{
		/// <summary>
		/// This method starts the service.
		/// </summary>
		static void Main(string[] args)
		{
			if (Environment.UserInteractive) {
            	var Ctl = ServiceController.GetServices().FirstOrDefault(S => S.ServiceName == HTPService.HTServiceName);
   				var Arg = string.Concat(args);
       			switch (Arg) {
        			case "--install":
            			if (Ctl == null) {
   							ManagedInstallerClass.InstallHelper(new[] { Assembly.GetExecutingAssembly().Location });
   						}
            			break;
        			case "--uninstall":
            			if (Ctl != null) {
            				ManagedInstallerClass.InstallHelper(new[] { "/u", Assembly.GetExecutingAssembly().Location });
            			}
            			break;
    			}
    		} else {
				// To run more than one service you have to add them here
				ServiceBase.Run(new ServiceBase[] { new HTPService() });
			}
		}
	}
}
