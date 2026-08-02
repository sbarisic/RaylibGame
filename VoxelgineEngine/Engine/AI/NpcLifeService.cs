using System.Numerics;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Engine;

public enum StableNpcIdKind : byte { Generated, Persistent }

public readonly record struct StableNpcId
{
	private StableNpcId(StableNpcIdKind kind,GeneratedMarkerId marker,PersistentEntityId persistent){Kind=kind;GeneratedMarkerId=marker;PersistentEntityId=persistent;}
	public StableNpcIdKind Kind { get; }
	public GeneratedMarkerId GeneratedMarkerId { get; }
	public PersistentEntityId PersistentEntityId { get; }
	public bool IsValid => Kind==StableNpcIdKind.Generated ? !string.IsNullOrWhiteSpace(GeneratedMarkerId.Site.Value)&&!string.IsNullOrWhiteSpace(GeneratedMarkerId.BlueprintMarkerId) : PersistentEntityId.Value!=0;
	public static StableNpcId Generated(GeneratedMarkerId marker)=>new(StableNpcIdKind.Generated,marker,default);
	public static StableNpcId Persistent(PersistentEntityId persistent)=>new(StableNpcIdKind.Persistent,default,persistent);
	public static void Write(BinaryWriter writer,StableNpcId id){if(!id.IsValid)throw new InvalidDataException("Stable NPC identity is invalid.");writer.Write((byte)id.Kind);if(id.Kind==StableNpcIdKind.Generated){writer.Write(id.GeneratedMarkerId.Site.Value);writer.Write(id.GeneratedMarkerId.BlueprintMarkerId);}else writer.Write(id.PersistentEntityId.Value);}
	public static StableNpcId Read(BinaryReader reader)=>((StableNpcIdKind)reader.ReadByte()) switch{StableNpcIdKind.Generated=>Generated(new GeneratedMarkerId(new GeneratedSiteId(reader.ReadString()),reader.ReadString())),StableNpcIdKind.Persistent=>Persistent(new PersistentEntityId(reader.ReadUInt64())),var kind=>throw new InvalidDataException($"Invalid stable NPC identity kind {kind}.")};
	public override string ToString()=>Kind==StableNpcIdKind.Generated?$"g:{GeneratedMarkerId.Site.Value}:{GeneratedMarkerId.BlueprintMarkerId}":$"p:{PersistentEntityId.Value}";
}

public readonly record struct NpcLifeRecord(StableNpcId NpcId,ushort Fatigue,PersistentFurnitureKey? AssignedBed);

/// <summary>Server-active fatigue, bed assignment, reservation, and sleep controller keyed only by stable IDs.</summary>
public sealed class NpcLifeService
{
	public const ushort SeekSleepFatigue = 6000;
	public const ushort WakeFatigue = 1000;
	private readonly FurnitureStore furniture;
	private readonly Func<IEnumerable<VEntBed>> beds;
	private readonly Dictionary<StableNpcId,LifeState> states=new();
	private readonly Dictionary<PersistentFurnitureKey,StableNpcId> reservations=new();
	private double lastGameHours;
	public NpcLifeService(FurnitureStore furniture,Func<IEnumerable<VEntBed>> beds,double absoluteGameHours){this.furniture=furniture??throw new ArgumentNullException(nameof(furniture));this.beds=beds??throw new ArgumentNullException(nameof(beds));lastGameHours=absoluteGameHours;}
	public void Restore(IEnumerable<NpcLifeRecord> records,double absoluteGameHours){states.Clear();reservations.Clear();lastGameHours=absoluteGameHours;foreach(NpcLifeRecord record in records){if(!record.NpcId.IsValid||states.ContainsKey(record.NpcId))throw new InvalidDataException("NPC life archive contains invalid or duplicate identity.");states.Add(record.NpcId,new LifeState(record));}}
	public void Attach(VEntNPC npc)
	{
		if(npc==null||!npc.StableId.IsValid)throw new ArgumentException("NPC requires stable identity.",nameof(npc));
		if(!states.TryGetValue(npc.StableId,out LifeState state))states[npc.StableId]=state=new LifeState(new NpcLifeRecord(npc.StableId,0,null));state.Npc=npc;
	}
	public IReadOnlyList<NpcLifeRecord> Capture()=>states.Values.Select(static state=>state.Record with{Fatigue=(ushort)Math.Clamp((int)Math.Round(state.PreciseFatigue),0,10000)}).OrderBy(static record=>record.NpcId.ToString(),StringComparer.Ordinal).ToArray();
	public bool TryGet(StableNpcId id,out NpcLifeRecord record){if(states.TryGetValue(id,out LifeState state)){record=state.Record with{Fatigue=(ushort)Math.Clamp((int)Math.Round(state.PreciseFatigue),0,10000)};return true;}record=default;return false;}
	public void AssignBed(StableNpcId id,PersistentFurnitureKey? bed){if(!states.TryGetValue(id,out LifeState state))throw new KeyNotFoundException();if(bed.HasValue&&(!furniture.TryGet(bed.Value,out PersistentFurnitureRecord record)||record.Type!=FurnitureType.Bed))throw new InvalidOperationException("Assigned bed does not exist.");state.Record=state.Record with{AssignedBed=bed};}
	public IReadOnlyList<(StableNpcId NpcId,PersistentFurnitureKey MissingBed)> RepairAssignments()
	{
		List<(StableNpcId,PersistentFurnitureKey)> cleared=new();foreach(LifeState state in states.Values)if(state.Record.AssignedBed is PersistentFurnitureKey key&&(!furniture.TryGet(key,out PersistentFurnitureRecord record)||record.Type!=FurnitureType.Bed)){cleared.Add((state.Record.NpcId,key));state.Record=state.Record with{AssignedBed=null};}return cleared;
	}
	public void Update(double absoluteGameHours,float timeOfDay,float realTime)
	{
		double elapsed=Math.Max(0,absoluteGameHours-lastGameHours);lastGameHours=absoluteGameHours;bool night=timeOfDay>=18f||timeOfDay<6f;
		foreach(LifeState state in states.Values)
		{
			VEntNPC npc=state.Npc;if(npc==null||npc.IsDead)continue;state.PreciseFatigue=Math.Clamp(state.PreciseFatigue+(npc.IsSleeping?-5000d:625d)*elapsed,0d,10000d);state.Record=state.Record with{Fatigue=(ushort)Math.Round(state.PreciseFatigue)};
			if(npc.IsSleeping)
			{
				if(!night||state.PreciseFatigue<=WakeFatigue||state.TargetBed is not PersistentFurnitureKey sleepingBed||!TryGetBed(sleepingBed,out _))Wake(state);
				continue;
			}
			if(!night||state.PreciseFatigue<SeekSleepFatigue||realTime<state.RetryAfter)continue;
			if(state.TargetBed is null)
			{
				VEntBed bed=ChooseBed(state,npc.Position);if(bed==null){state.RetryAfter=realTime+30f;continue;}state.TargetBed=bed.PersistentKey;reservations[bed.PersistentKey]=state.Record.NpcId;state.SeekingSince=realTime;npc.IsLifeControlled=true;
				Vector3 approach=GetApproach(bed);if(!npc.NavigateTo(approach)){Release(state);state.RetryAfter=realTime+30f;continue;}
			}
			if(realTime-state.SeekingSince>=20f){npc.StopNavigation();Release(state);state.RetryAfter=realTime+30f;continue;}
			if(state.TargetBed is PersistentFurnitureKey target&&TryGetBed(target,out VEntBed targetBed)&&(npc.HasReachedTarget||Vector3.DistanceSquared(npc.Position,GetApproach(targetBed))<0.64f))
				npc.EnterSleep(new Vector3(targetBed.Anchor.X+0.5f,targetBed.Anchor.Y+0.35f,targetBed.Anchor.Z+0.5f));
		}
	}
	public void OnBedRemoved(PersistentFurnitureKey bed)
	{
		reservations.Remove(bed);foreach(LifeState state in states.Values){if(state.Record.AssignedBed==bed)state.Record=state.Record with{AssignedBed=null};if(state.TargetBed==bed)Wake(state);}
	}
	private VEntBed ChooseBed(LifeState state,Vector3 position)
	{
		if(state.Record.AssignedBed is PersistentFurnitureKey assigned&&TryGetBed(assigned,out VEntBed assignedBed)&&IsAvailable(assigned,state.Record.NpcId))return assignedBed;
		return beds().Where(bed=>IsAvailable(bed.PersistentKey,state.Record.NpcId)).OrderBy(bed=>Vector3.DistanceSquared(position,bed.Position)).FirstOrDefault();
	}
	private bool IsAvailable(PersistentFurnitureKey bed,StableNpcId npc)=>!reservations.TryGetValue(bed,out StableNpcId reserved)||reserved==npc;
	private bool TryGetBed(PersistentFurnitureKey key,out VEntBed bed){bed=beds().FirstOrDefault(candidate=>candidate.PersistentKey==key);return bed!=null&&furniture.TryGet(key,out PersistentFurnitureRecord record)&&record.Type==FurnitureType.Bed;}
	private static Vector3 GetApproach(VEntBed bed){BlockCoordinate offset=VEntBed.FacingOffset(bed.Facing);return new Vector3(bed.Anchor.X+0.5f-offset.X,bed.Anchor.Y,bed.Anchor.Z+0.5f-offset.Z);}
	private void Wake(LifeState state){state.Npc?.WakeFromSleep();Release(state);}
	private void Release(LifeState state){if(state.TargetBed is PersistentFurnitureKey bed)reservations.Remove(bed);state.TargetBed=null;if(state.Npc?.IsSleeping!=true&&state.Npc!=null)state.Npc.IsLifeControlled=false;}
	private sealed class LifeState
	{
		public LifeState(NpcLifeRecord record){Record=record;PreciseFatigue=record.Fatigue;}
		public NpcLifeRecord Record;public double PreciseFatigue;public VEntNPC Npc;public PersistentFurnitureKey? TargetBed;public float SeekingSince;public float RetryAfter;
	}
}
