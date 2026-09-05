#if HARDENED
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;
using Voxelgine.Graphics;

internal static class MultiplayerSmoke
{
	internal static void Run(string serverAssembly, string artifactRoot)
	{
		string root=Path.GetFullPath(artifactRoot); Directory.CreateDirectory(root);
		string hostedRoot=Path.Combine(root,"hosted"), dedicatedRoot=Path.Combine(root,"dedicated");
		SeedArchive(hostedRoot); SeedArchive(dedicatedRoot);
		using (NetClient client=CreateClient(Path.Combine(root,"host-client")))
		using (ServerApplication hosted=new(Paths(hostedRoot),GameLogLevel.Info,client.Identity))
		{
			hosted.StartHosted(0,666,false); hosted.StartupTask.Wait(TimeSpan.FromSeconds(20));
			if(!hosted.StartupTask.IsCompletedSuccessfully) throw new InvalidOperationException("Hosted startup failed.",hosted.BackgroundFailure);
			Exercise(client,hosted.Server.Server.Port,true);
			hosted.Stop(); if(hosted.BackgroundFailure!=null) throw hosted.BackgroundFailure;
		}
		int port; using(UdpClient reservation=new(0)) port=((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
		ProcessStartInfo start=new("dotnet") { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
		foreach(string value in new[]{Path.GetFullPath(serverAssembly),"--port",port.ToString(),"--data-root",dedicatedRoot}) start.ArgumentList.Add(value);
		for (int run=0;run<2;run++)
		{
			using Process process=Process.Start(start)!; Task<string> output=process.StandardOutput.ReadToEndAsync(), error=process.StandardError.ReadToEndAsync();
			try
			{
				string clientRoot=Path.Combine(root,"dedicated-client");
				if(run==1)
				{
					using NetClient denied=CreateClient(clientRoot);string rejection=null;denied.OnConnectionRejected+=reason=>rejection=reason;
					Stopwatch timer=Stopwatch.StartNew();denied.Connect("127.0.0.1",port,"SmokePlayer",0);
					while(rejection==null&&timer.Elapsed.TotalSeconds<10){denied.Tick(timer.Elapsed.TotalSeconds);Thread.Sleep(1);}
					if(rejection?.Contains("Banned")!=true)throw new InvalidOperationException("Persisted ban did not reject connection after restart: "+rejection);
					process.StandardInput.WriteLine("unban "+denied.Identity.Fingerprint);Thread.Sleep(50);
				}
				using NetClient client=CreateClient(clientRoot);Exercise(client,port,run==1);
				if(run==0){process.StandardInput.WriteLine("op "+client.Identity.Fingerprint);process.StandardInput.WriteLine("ban "+client.Identity.Fingerprint);}
			}
			finally
			{
				if(!process.HasExited) process.StandardInput.WriteLine("quit");
				if(!process.WaitForExit(15000)) {process.Kill(true);process.WaitForExit();}
				File.WriteAllText(Path.Combine(root,$"dedicated-{run}.stdout.log"),output.GetAwaiter().GetResult());
				File.WriteAllText(Path.Combine(root,$"dedicated-{run}.stderr.log"),error.GetAwaiter().GetResult());
			}
			if(process.ExitCode!=0) throw new InvalidOperationException("Dedicated server exited with "+process.ExitCode);
		}
		if(Directory.GetFiles(Path.Combine(dedicatedRoot,"players","identities"),"*.bin").Length!=1)throw new InvalidOperationException("Reconnect changed persistent profile identity.");
		Console.WriteLine("Hosted and dedicated: authenticated connect, decode/apply bootstrap, enter gameplay, permission checks, persisted ban/admin across restart, unban, reconnect, disconnect and save passed.");
	}
	private static RuntimePaths Paths(string root)=>RuntimePathResolver.ResolveRuntimePaths(ApplicationKind.Test,root,Environment.CurrentDirectory);
	private static NetClient CreateClient(string root)
	{
		Directory.CreateDirectory(root); NetClient client=new();client.ConfigureIdentity(NetworkIdentity.LoadOrCreate(Path.Combine(root,"client-identity.pk8")),new(Path.Combine(root,"trusted-servers.json")));return client;
	}
	private static void SeedArchive(string root)
	{
		RuntimePaths paths=Paths(root);paths.CreateDirectories();ChunkMap map=new();
		for(int x=0;x<64;x++)for(int z=0;z<64;z++)map.SetBlock(x,0,z,BlockType.Grass);
		using FileStream stream=File.Create(Path.Combine(paths.WorldDirectory,"map.bin"));
		WorldArchive.Write(stream,map,new(666,new(24,1,24),new(28,1,28),new(32,1,32)));
	}
	private static void Exercise(NetClient client,int port,bool administrator)
	{
		Stopwatch clock=Stopwatch.StartNew();int streamId=0,columns=0,tick=0;bool bootstrap=false,playing=false,commandSent=false;bool? commandAccepted=null;string failure=null;double nextReady=0,nextInput=0;
		client.OnConnectionRejected+=reason=>failure=reason;
		client.OnPacketReceived+=packet=>
		{
			switch(packet)
			{
				case WorldStreamBeginPacket begin:streamId=begin.StreamId;break;
				case WorldColumnPacket column:
					if(WorldColumnCodec.ComputeChecksum(column.Payload)!=column.Checksum)throw new InvalidDataException("Column checksum mismatch.");
					_=WorldColumnCodec.Decode(column.X,column.Z,column.Revision,column.Payload);columns++;
					client.Send(new WorldColumnAppliedPacket{StreamId=column.StreamId,X=column.X,Z=column.Z,Revision=column.Revision},true,clock.Elapsed.TotalSeconds);break;
				case WorldBootstrapCompletePacket:bootstrap=true;break;
				case ClientWorldStartPacket:playing=true;client.FinishLoading();break;
				case ConsoleCommandResultPacket result:commandAccepted=result.Success;break;
				case WorldObjectColumnPacket objects:
					if(objects.PartCount!=1||objects.TotalRecordCount!=0)throw new InvalidDataException("Unexpected smoke object fixture.");
					client.Send(new WorldObjectColumnAppliedPacket{StreamId=objects.StreamId,X=objects.X,Z=objects.Z,Epoch=objects.Epoch,Revision=objects.Revision},true,clock.Elapsed.TotalSeconds);break;
			}
		};
		client.Connect("127.0.0.1",port,"SmokePlayer",clock.Elapsed.TotalSeconds);
		while(clock.Elapsed.TotalSeconds<20 && commandAccepted==null && failure==null)
		{
			double now=clock.Elapsed.TotalSeconds;client.Tick(now);
			if(bootstrap&&!playing&&now>=nextReady){client.Send(new ClientWorldReadyPacket{StreamId=streamId},true,now);nextReady=now+.25;}
			if(playing&&now>=nextInput){client.Send(new InputStatePacket{Commands=new[]{new InputCommand{TickNumber=++tick}}},false,now);nextInput=now+.015;}
			if(playing&&!commandSent){client.Send(new ConsoleCommandRequestPacket{RequestId=1,CommandName="night"},true,now);commandSent=true;}
			Thread.Sleep(1);
		}
		if(failure!=null||!playing||columns==0||commandAccepted!=administrator)throw new InvalidOperationException($"Multiplayer smoke failed: playing={playing}, columns={columns}, adminResult={commandAccepted}, reason={failure}");
		client.Disconnect("Smoke complete",clock.Elapsed.TotalSeconds);Thread.Sleep(50);
		Console.WriteLine($"Multiplayer smoke admin={administrator} columns={columns} playableMs={clock.Elapsed.TotalMilliseconds:F1}");
	}
}
#endif
