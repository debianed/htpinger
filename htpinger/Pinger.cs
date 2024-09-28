using SnmpSharpNet;
using System.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HTPinger
{
	/// <summary>
	/// Description of MultiPinger.
	/// </summary>
	public class MultiPinger
	{
		private readonly CancellationTokenSource Cancel = new CancellationTokenSource();
		private EventLog Log = null;
		public ConcurrentDictionary<IPAddress, Pinger> Png = new ConcurrentDictionary<IPAddress, Pinger>();
		
		public MultiPinger(EventLog Log) {
			this.Log = Log;
		}
		
		public void AddAddress(string Addr, int TimeOut = 3000, int BetweenPing = 10000, string Community = "public") {
        	var Pngr = new Pinger(IPAddress.Parse(Addr), Cancel.Token, TimeOut, BetweenPing, this.Log);
        	if (Png.TryAdd(Pngr.IPAddr, Pngr)) {
        		this.GetSNMPInfo(Addr.Trim(), Community);
        	}
    	}
		
		public void RemoveAddress(string Addr) {
			Pinger P = null;
			if (Png.TryRemove(IPAddress.Parse(Addr), out P)) P.Stop();
    	}
		
		public void Start() {
			if (Png.Count != 0) {
				foreach (var P in Png.Values) {
					P.Start();
				}
			}
		}
		
		public void Stop() {
			if (Png.Count != 0) {
				foreach (var P in Png.Values) {
					P.Stop();
				}				
			}			
		}
		
		public PingReply GetReply(IPAddress Addr) {
			return Png.ContainsKey(Addr) ? Png[Addr].GetReply() : null;
    	}
		
		public Tuple<long, long> GetSuccessOperation(IPAddress Addr) {
			return Png.ContainsKey(Addr) ? Png[Addr].GetSuccessOperation() : null;
    	}
		
		public PingReply[] GetReply() {
        	PingReply[] Ret = Png.Values.Select(x=>x.GetReply()).ToArray();
        	return Ret;
    	}
		
		public PingInfo GetPingInfo(IPAddress Addr) {
        	if (Png.ContainsKey(Addr)) {
            	var Ret = new PingInfo();
	            var P = Png[Addr];
    	        Ret.Reply = P.GetReply();
    	        Ret.SuccessPing = P.SuccessReply;
    	        Ret.FailPing = P.FailReply;
    	        Ret.LastSuccessPing = P.LastSuccessfullPing;
    	        return Ret;
    	    }
    	    return null;
    	}
		
		public bool IsPinged(IPAddress Addr) {
			return Png.ContainsKey(Addr);
    	}
		
    	public IPAddress[] GetAddressesPing() {
        	return Png.Keys.ToArray();
    	}
		
		private void GetSNMPInfo(string Host, string Comm)
		{
			if (Host == string.Empty) { return; }
			
			var SNMP = new SimpleSnmp(Host, Comm);
			if (SNMP.Valid) {
				var Ans = new string[2];
				
				// 1.3.6.1.4.1.40297.1.2.4.5.0 - sn
				// 1.3.6.1.4.1.40297.1.2.4.1.0 - model
				var Rep = SNMP.Get(SnmpVersion.Ver1, new string[] {"1.3.6.1.4.1.40297.1.2.4.1.0", "1.3.6.1.4.1.40297.1.2.4.5.0"});
				if (Rep != null) {
					byte i = 0;
					foreach (var Res in Rep) {
						var Tmp = Res.Value.ToString().Split(' ').Select(s => byte.Parse(s, NumberStyles.HexNumber)).ToArray().Where(N => N != 0).ToArray();
						Ans[i] = Encoding.Default.GetString(Tmp);
						i++;
					}
					this.Log.WriteEntry("В мониторинг добавлен IP " + Host + " [ретранслятор " + Ans[0] + " s/n: " + Ans[1] + "]", EventLogEntryType.Information);
				}
			} else {
				this.Log.WriteEntry("Хост с IP " + Host + "не является верным SNMP-хостом", EventLogEntryType.Error);
			}
		}
	}

	/// <summary>
	/// Description of Pinger.
	/// </summary>	
	public class Pinger
	{
		private readonly CancellationToken CancelToken;
		private EventLog Log = null;
		private Task PngTask = null;
		private bool IsStopped = false;
		
		public long FailReply;
    	public long SuccessReply;
    	public DateTime LastSuccessfullPing = DateTime.MinValue;
    	public PingOptions PingOpt;
    	public int BetweenPing;
    	public Stack<PingReply> Replys = new Stack<PingReply>(10);
    	public static byte[] Buf = Encoding.ASCII.GetBytes("Hytera");
    	
    	
		public IPAddress IPAddr { get; set; }
		public int TimeOut { get; set; }
		
		public Pinger(IPAddress Addr, CancellationToken CnclT, int TimeOut = 3000, int BetweenPing = 10000, EventLog Log = null) {
			this.IPAddr = Addr;
        	this.PingOpt = new PingOptions();
        	this.PingOpt.DontFragment = false;
        	this.CancelToken = CnclT;
        	this.TimeOut = TimeOut;
        	this.BetweenPing = BetweenPing;
        	this.Log = Log;
		}
		
		public async void Start() {
			if (PngTask == null || PngTask.Status != TaskStatus.Running) {
            	PngTask = await Task.Factory.StartNew((Func<Task>)PingCircle, TaskCreationOptions.RunContinuationsAsynchronously | TaskCreationOptions.LongRunning);
        	}
		}
		
		public void Stop() {
			if (PngTask.Status == TaskStatus.Running) {
            	IsStopped = true;
            	try {
                	PngTask.Wait(this.TimeOut, CancelToken);
            	}
           		catch (Exception Ex) {
            		this.Log.WriteEntry("Ошибка службы Hytera RAS: " + Ex.Message.ToString(), EventLogEntryType.Error);
            	}
			}
		}
		
		public PingReply GetReply() {
			return Replys.Count == 0 ? null : Replys.Pop();
		}
		
		public Tuple<long, long> GetSuccessOperation() {
			return new Tuple<long, long>(this.SuccessReply, this.FailReply);
		}
		
		private async Task PingCircle() {
			while (CancelToken.IsCancellationRequested == false && !this.IsStopped) {
            	try {
                	try {
						var Png = new Ping();
						var PT = Png.SendPingAsync(this.IPAddr, this.TimeOut, Buf, this.PingOpt);
						var Rep = await Task.WhenAll(PT);
						Png.Dispose();
						if (Rep != null) SetReply(Rep[0]);
						if (Rep[0].Status != IPStatus.Success) {
							this.Log.WriteEntry("[" + DateTime.Now.ToString() + "] Ответ от " + this.IPAddr.ToString() + ": " + Rep[0].Status.ToString(), EventLogEntryType.Error);
						}

                	} catch (PingException P) {
							this.Log.WriteEntry("Ошибка: " + P.ToString(), EventLogEntryType.Error);
                	} catch (Exception Ex) {
							this.Log.WriteEntry("Ошибка службы Hytera RAS: " + Ex.Message.ToString(), EventLogEntryType.Error);
                	}
                	await Task.Delay(this.BetweenPing, CancelToken);
            	} catch (Exception Ex) {
					this.Log.WriteEntry("Ошибка службы Hytera RAS: " + Ex.Message.ToString(), EventLogEntryType.Error);
            	}
        	}
		}
		
		private void SetReply(PingReply Rep) {
        	if (Rep == null) return;
        	this.Replys.Push(Rep);
        	if (Rep.Status == IPStatus.Success) {
            	Interlocked.Increment(ref this.SuccessReply);
            	LastSuccessfullPing = DateTime.Now;
        	} else {
            	Interlocked.Increment(ref this.FailReply);
        	}
    	}
	}
	
	
	/// <summary>
	/// Description of PingInfo.
	/// </summary>
	public class PingInfo {
    	public PingReply Reply;
    	public long SuccessPing = 0;
    	public long FailPing = 0;
    	public DateTime LastSuccessPing;
    	public override string ToString() {
    		return "Успешно: " + SuccessPing.ToString() + ", последний=" + LastSuccessPing.ToString() + "; Ошибок: " + FailPing.ToString() + ", ответ: " + Reply.ToString();
    	}
	}
}
