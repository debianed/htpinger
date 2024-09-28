using System;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace HTPinger
{
	[RunInstaller(true)]
	public class ProjectInstaller : Installer
	{
		private readonly ServiceProcessInstaller serviceProcessInstaller;
		private readonly ServiceInstaller serviceInstaller;
		
		public ProjectInstaller()
		{
			this.AfterInstall += this.ProjectInstaller_AfterInstall;
			this.BeforeUninstall += ProjectInstaller_BeforeUninstall;
			
			serviceProcessInstaller = new ServiceProcessInstaller();
			serviceProcessInstaller.Account = ServiceAccount.NetworkService;
			
			serviceInstaller = new ServiceInstaller();
			serviceInstaller.ServiceName = HTPService.HTServiceName;
			serviceInstaller.Description = HTPService.HTDescription;
			serviceInstaller.DelayedAutoStart = true;
			serviceInstaller.StartType = ServiceStartMode.Automatic;

			// Replace default event source installer 			
            foreach (var Inst in serviceInstaller.Installers)
            {
				var eventLogInstaller = Inst as EventLogInstaller;
                if (eventLogInstaller != null)
                {
                	eventLogInstaller.Source = HTPService.HTServiceName;
                	eventLogInstaller.Log = HTPService.HTDescription;
					eventLogInstaller.UninstallAction = UninstallAction.Remove;
                    break;
                }
            }
			
			this.Installers.AddRange(new Installer[] { serviceProcessInstaller, serviceInstaller });
		}

		private void ProjectInstaller_AfterInstall(object sender, InstallEventArgs e)
		{
			try
			{
				//Set failure actions
				ServiceConfigurator.SetRecoveryOptions(HTPService.HTServiceName);
			
				//Start service
				var SC = new ServiceController(HTPService.HTServiceName);
        		SC.Start();
			// disable once EmptyGeneralCatchClause
			} catch {
			}
		}

		private void ProjectInstaller_BeforeUninstall(object sender, InstallEventArgs e)
		{
			//Start service
			var SC = new ServiceController(HTPService.HTServiceName);
			try
			{
        		SC.Stop();
        		SC.WaitForStatus(ServiceControllerStatus.Stopped, new TimeSpan(0, 1, 0));
			// disable once EmptyGeneralCatchClause
			} catch {
			}
		}
	}
	
	public static class ServiceConfigurator
	{
	    private const int SERVICE_CONFIG_FAILURE_ACTIONS = 0x2;
    	private const int ERROR_ACCESS_DENIED = 5;

	    /* sc_action constants */
    	private const int SC_ACTION_RESTART = 1;
	    private const int DELAY_IN_MILLISECONDS = 0;

    	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	    private struct SERVICE_FAILURE_ACTIONS
    	{
    	    public int dwResetPeriod;
    	    [MarshalAs(UnmanagedType.LPWStr)]
    	    public string lpRebootMsg;
    	    [MarshalAs(UnmanagedType.LPWStr)]
    	    public string lpCommand;
    	    public int cActions;
    	    public IntPtr lpsaActions;
    	}

    	[DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2")]
    	private static extern bool ChangeServiceFailureActions(IntPtr hService, int dwInfoLevel, [MarshalAs(UnmanagedType.Struct)] ref SERVICE_FAILURE_ACTIONS lpInfo);

    	[DllImport("kernel32.dll")]
    	private static extern int GetLastError();

	    public static void SetRecoveryOptions(String serviceName, int pDaysToResetFailureCount = 0)
    	{
    	    var svcController = new ServiceController(serviceName);
    	    IntPtr _ServiceHandle = svcController.ServiceHandle.DangerousGetHandle();

    	    int NUM_ACTIONS = 3;
    	    var arrActions = new int[NUM_ACTIONS * 2];
    	    int index = 0;
    	    arrActions[index++] = SC_ACTION_RESTART;
    	    arrActions[index++] = DELAY_IN_MILLISECONDS;
    	    arrActions[index++] = (int)SC_ACTION_RESTART;
    	    arrActions[index++] = DELAY_IN_MILLISECONDS;
    	    arrActions[index++] = (int)SC_ACTION_RESTART;
    	    arrActions[index++] = DELAY_IN_MILLISECONDS;

    	    IntPtr tmpBuff = Marshal.AllocHGlobal(NUM_ACTIONS * 8);

 	       try
    	    {
        	    Marshal.Copy(arrActions, 0, tmpBuff, NUM_ACTIONS * 2);
            	var sfa = new SERVICE_FAILURE_ACTIONS();
	            sfa.cActions = 3;
    	        sfa.dwResetPeriod = pDaysToResetFailureCount;
    	        sfa.lpCommand = null;
    	        sfa.lpRebootMsg = null;
    	        sfa.lpsaActions = new IntPtr(tmpBuff.ToInt32());

    	        bool success = ChangeServiceFailureActions(_ServiceHandle, SERVICE_CONFIG_FAILURE_ACTIONS, ref sfa);
    	        if (!success)
    	        {
    	            if (GetLastError() == ERROR_ACCESS_DENIED)
    	                throw new Exception("Access denied while setting failure actions.");
    	            else
    	                throw new Exception("Unknown error while setting failure actions.");
    	        }
    	    }
    	    finally
    	    {
    	        Marshal.FreeHGlobal(tmpBuff);
    	        tmpBuff = IntPtr.Zero;
    	    }
    	}
	}
}
