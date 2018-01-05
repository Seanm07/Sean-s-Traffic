// Version 3
// AI lane rejoining
// Traffic pull over

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
	using UnityEditor;
#endif

public class AIVehicleLaneData {
	public int VehicleID; // ID of the AI vehicle currently in this lane
	public float VehicleProgress; // How far the AI vehicle is between Start and End (0 - 1)
	public float VehicleSpeed; // Vehicle speed as a percentage (0 - 1)
	public int CalculationMode; // 0 = Very close, 1 = Pretty close, 2 = distance high performance, 3 = very far car not rendered just move the car ultra performance
	public float TimeUntilNextProgressCheck;
	public float TimeUntilNextDistanceCheck;
	public float TimeUntilHornCanBeUsedAgain;

	public TrafficLaneManager.BlockageType LastBlockageReason = TrafficLaneManager.BlockageType.None;

	public AIVehicleLaneData(int InVehicleID, float InVehicleProgress = 0f, float InSpeed = 1f)
	{
		VehicleID = InVehicleID;
		VehicleProgress = InVehicleProgress; // This value is set when a vehicle is changing lanes
		VehicleSpeed = InSpeed;
		CalculationMode = 4; // Spawn in as calc mode 4 (invalid state) so it'll act as if it's just entered whatever calc mode is appropriate for its distance
	}
}

[System.Serializable]
public class JointLaneData {
	public int RoadID;
	public int LaneID;

	public JointLaneData(int InRoadID, int InLaneID)
	{
		RoadID = InRoadID;
		LaneID = InLaneID;
	}
}

[System.Serializable]
public class Lane {
	public Transform StartLane;
	public Transform EndLane;
	public LaneBezierHandler Bezier;
	public int JointLaneCount; // Store the joint lane list count in a variable when calculating roads so we don't need to constantly count it
	public int RoadID; // ID of the Road containing this Lane

	public bool WaitForClearIntersection; // If true vehicles will wait until all lanes not marked as DontWaitForThisLane are empty
	public bool DontWaitForThisLane; // When true lanes marked as WaitForClearIntersection won't wait for this lane to be empty

	public List<JointLaneData> JointLanes = new List<JointLaneData>(); // List of possible lanes the AI car can start at after reaching the end of the lane

	public int TotalActiveAIVehicles; // Cached count of the ActiveAIVehicles list, updated when we add or remove vehicles to the list
	public int TotalInactiveAIVehicles; // Vehicles marked as IsOnRoad false will increase this counter

	public List<AIVehicleLaneData> ActiveAIVehicles = new List<AIVehicleLaneData> ();
}

[System.Serializable]
public class Road {
	public GameObject RoadObj;
	public int LaneCount; // Store the lane list count in a variable when calculating roads so we don't need to constantly count the lanes
	public int IntersectionID = -1; // -1 = Not an intersection, otherwise this is an intersection and all other roads with this intersection ID must be clear before AI can enter the road
	public bool RoadClosed = false; // When a road is closed no NEW cars can join it

	public List<Lane> Lanes = new List<Lane>();
}

[System.Serializable]
public class CarData {
	public GameObject VehicleObj;
	public Rigidbody VehicleRigidbody { get; set; }
	public List<Transform> Wheels { get; set; }
	public AudioSource HornAudio { get; set; }
	public float Speed { get; set; }

	// Not yet implemented
	//[Range(0f, 1f)]
	//public float FleeWhenHitChance = 0f; // Similar to GTA when some cars will flee from the player when being hit as they're scared

	public float TimeSinceLastValidRaycast { get; set; }
	public Vector3 RaycastUpVector { get; set; }
	public float LastYPoint { get; set; }
	public float RaycastYPoint { get; set; }
	public float LastYBezierPos { get; set; }
	public Vector3 LastTargetPosition { get; set; }
	public Vector3 LaneChangePositionOffset { get; set; }

	public AICarHandler CarHandler { get; set; }
	public bool IsOnRoad { get; set; }
	public bool IsChangingLane { get; set; }
	public bool IsAllowedOffRoadMovement { get; set; }

	public CarData (GameObject InObj, List<Transform> InWheels = default(List<Transform>), AudioSource InHornAudio = null, Rigidbody InRigidbody = null, AICarHandler InCarHandler = null)
	{
		VehicleObj = InObj;
		Wheels = InWheels;
		VehicleRigidbody = InRigidbody;

		CarHandler = InCarHandler;
		HornAudio = InHornAudio;

		IsOnRoad = true;
	}
}

[System.Serializable]
public class MapTrafficData {
	public int RoadCount; // Store the road list count in a variable when calculating roads so we don't need to constantly count the list
	public List<Road> RoadData = new List<Road>();
}

public class SparkData {
	public GameObject Obj;
	public ParticleSystem Particle;

	public SparkData(GameObject InObj)
	{
		Obj = InObj;
		Particle = InObj.GetComponent<ParticleSystem>();
		Particle.Stop(true);
	}
}

public class TrafficLaneManager : MonoBehaviour {
	public static TrafficLaneManager Instance;

	public enum ActionWhenHit { NothingIgnoreCollisions, StopBecomePhysical } // SmartAIDynamicallyRejoin coming soon

	[Header("Functionality Settings")]
	[Tooltip("How should the traffic act when the player crashes into them? (Note: Selecting nothing also makes them into solid objects which cannot be pushed)")]
	public ActionWhenHit TrafficActionWhenHit;

	[Tooltip("Stopping distance between traffic. Setting this too low may cause vehicles to drive inside each other")]
	public float DistanceBetweenVehicles = 10f; // This value is divided by the size of the road to give a consistant distance between vehicles (it's not in meters)

	[Header("Performance Optimizations")]
	[Tooltip("If a single car on a road is too far from the player then all no more vehicle calculations will be ran on that road this frame. Do not use with long roads!")]
	public bool ShortRoadOptimization = false; // When true if 1 car on a road is calculation mode 3 then no more calculations will be ran on that road. 
	//Note! Games with long roads should not use this as it will seem like cars just suddenly freeze or drive inside each other.

	[Tooltip("When enabled vehicles will only raycast when they are spawned (vehicles spawn as you come in range of them)")]
	public bool OnlyRaycastWhenActivated = false; // Will only make vehicles raycast once each time they're activated instead of every few frames, useful optimization if your ground height doesn't change (not suitable if cars will be spawned when the terrain is disabled, e.g multistory building where each floor is disabled for optimization)

	[Tooltip("When enabled vehicles will never raycast and their Y position is determined based on the road points and beziers only")]
	public bool NeverRaycast = false; // Will never raycast, even when spawning. This means the Y will follow the plotted road points and beziers (very useful optimization if your ground height doesn't change and your road beziers are aligned with the ground correctly already)

	[Header("Debugging")]
	[Tooltip("Logs some extra editor only debug information")]
	public bool EditorDebugMode = false;

	public int MapCount { get; set; } // Store the count of maps into a variable so the list doesn't need to be constantly counted

	[HideInInspector]
	public List<MapTrafficData> TrafficData = new List<MapTrafficData> ();

	[Header("Vehicle Prefabs")]
	public List<CarData> CarTemplates = new List<CarData>();
	private List<CarData> CarObjects = new List<CarData> ();

	public int TotalAIVehicles { get; set; }

	[Header("Project Settings")]
	[Tooltip("Context Menu > Auto Create Layers to auto set")]public LayerMask RoadLayer;
	[Tooltip("Context Menu > Auto Create Layers to auto set")]public LayerMask PlayerLayer;
	[Tooltip("Context Menu > Auto Create Layers to auto set")]public LayerMask AILayer;

	[Header("Sounds & Particles")]
	[Tooltip("Prefab of the sparks when colliding with traffic,. (Sean-s-Traffic/HitParticle/HitParticle.prefab)")]
	public GameObject HitParticle;

	[Tooltip("An audio source in your project to use to play vehicle collision sounds. It will be moved to the position of collision on impact")]
	public AudioSource HitSource;

	[Space]
	[Tooltip("Sound to play when doing a hard crashing into a vehicle. (Sean-s-Traffic/Vehicle Templates/Sounds/HardCrash.mp3)")]
	public AudioClip BigHitSound;

	[Tooltip("Sound to play when bumping into another vehicle. (Sean-s-Traffic/Vehicle Templates/Sounds/LowCrash.mp3)")]
	public AudioClip SmallHitSound;

	// Not yet implemented
	//[Space]
	//public AudioClip FemaleFleeScream;
	//public AudioClip MaleFleeScream;

	public List<SparkData> SparkPool = new List<SparkData>();
	private int SparkID = 0;

	private GraphicsTier CachedGraphicsTier;

	private Vector3 DespawnCarPosition = new Vector3(0f, -10000f, 0f);

	public void CreateSparkPool()
	{
		for(int i=0;i < 5; i++){
			SparkPool.Add(new SparkData((GameObject)Instantiate(HitParticle, Vector3.zero, Quaternion.identity) as GameObject));
		}
	}

	public void ActivateSparkAt(Vector3 InPos)
	{
		SparkPool[SparkID].Obj.transform.position = InPos;
		SparkPool[SparkID].Particle.Play(true);

		SparkID = (SparkID + 1 >= 5 ? 0 : SparkID + 1);
	}

	void Awake()
	{
		Instance = (Instance == null ? this : Instance);

		CachedGraphicsTier = Graphics.activeTier; 
		 
		switch(CachedGraphicsTier)
		{
			// User is playing on a potato (e.g Tab 3)
			case GraphicsTier.Tier1:
				Time.fixedDeltaTime = 0.025f;
				Time.maximumDeltaTime = 0.05f; // This might be causing issues with the joint jittering (0.05 = 20 FPS)
				break;

			// User is playing on a phone/tablet
			default:
			case GraphicsTier.Tier2:
				Time.fixedDeltaTime = 0.012f;
				Time.maximumDeltaTime = 0.12f; // This might be causing issues with joint jittering (0.01 = 100 FPS) (0.0333 = 30 FPS) (0.0666 = 15 FPS) (0.1 = 10 FPS)

				//CameraManager.Instance.SetCameraFarViewDistance(2000f);
				//CameraManager.Instance.SetSkyboxScale(new Vector3(65f, 65f, 65f));

				//QualitySettings.SetQualityLevel(1);
				break;
		}

		Time.maximumParticleDeltaTime = Time.fixedDeltaTime;

		CreateSparkPool();
	}

	#if UNITY_EDITOR
	[ContextMenu("Ungroup all lanes for single map")]
	public void UngroupAllLanes()
	{
		int SelectedMapID = 0;
		List<string> MapNames = new List<string> ();

		for (int i = 0; i < transform.childCount; i++) {
			MapNames.Add (transform.GetChild (i).name);
		}

		switch(MapNames.Count){
			case 0: EditorUtility.DisplayDialog ("No maps?", "There's no maps available to ungroup!", "Ok"); return; break;
			case 1: SelectedMapID = 0; break;
			case 2: SelectedMapID = (EditorUtility.DisplayDialog ("Selected a map", "Which map do you want to ungroup the lanes of?\n(Closing this window will ungroup the last map)", MapNames[0], MapNames[1]) ? 0 : 1); break;
			case 3: SelectedMapID = EditorUtility.DisplayDialogComplex ("Select a map", "Which map do you want to ungroup the lanes of?\n(Closing this window will ungroup the last map)", MapNames[0], MapNames[1], MapNames[2]); break;
			default: EditorUtility.DisplayDialog ("Too many maps!", "This function isn't built to support over 3 maps, changes need to be made to UngroupAllLanes inside TrafficLaneManager.cs", "Ok"); return; break;
		}

		// Moves all lanes for the selected map to the root and deletes roads + categories
		Transform SelectedMap = transform.GetChild (SelectedMapID);

		for (int CatID = 0; CatID < SelectedMap.childCount; CatID++) {
			Transform CurCat = SelectedMap.GetChild (CatID);

			for (int RoadID = 0; RoadID < CurCat.childCount; RoadID++) {
				Transform CurRoad = CurCat.GetChild (RoadID);

				// Iterate backwards so removing them doesn't affect the iteration
				for (int LaneID = CurRoad.childCount - 1; LaneID >= 0; LaneID--) {
					Transform CurLane = CurRoad.GetChild (LaneID);

					// Unparent the lane
					CurLane.SetParent (null);

					// Moves the lane to the bottom of the hierarchy
					CurLane.SetAsLastSibling ();

					CurLane.name = "Start Lane 0";
				}
			}
		}

		Debug.Log ("Done! All lanes are unparented!");
	}

	// Erk sorry about the messy function, but this is just an editor script rushed for quick project completion, ask me if you need this script cleaning up (sean@i6.com)
	[ContextMenu("Auto Group Lanes into Roads")]
	public void GroupLanesIntoRoads()
	{
		// Find lanes of the same type where the starts and ends are within x distance of each other and group them

		List<LaneBezierHandler> UnassignedLanes = new List<LaneBezierHandler> ();
		List<LaneBezierHandler> UnassignedIntersectionLanes = new List<LaneBezierHandler> ();
		//List<Road> RoadList = new List<Road> ();

		GameObject MapCategory = new GameObject ("Map Traffic");
		GameObject RoadCategory = new GameObject ("Roads");
		GameObject IntersectionCategory = new GameObject ("Intersections");

		MapCategory.transform.SetParent (transform);
		RoadCategory.transform.SetParent (MapCategory.transform);
		IntersectionCategory.transform.SetParent (MapCategory.transform);

		// Find all lanes which are root children of this transform and add them to the lane assignment list + set their lanes
		for (int i = 0; i < transform.childCount; i++) {
			LaneBezierHandler CurLaneHandler = transform.GetChild (i).GetComponent<LaneBezierHandler> ();

			if (CurLaneHandler != null) {
				if (CurLaneHandler.IsStartPoint) {
					if (!CurLaneHandler.IsIntersection) {
						UnassignedLanes.Add (CurLaneHandler);
					} else {
						UnassignedIntersectionLanes.Add (CurLaneHandler);
					}

					CurLaneHandler.name = "Start Lane 0";

					// Align to ground
					CurLaneHandler.SnapPointsToRoadSurface();
				}
			}
		}

		for (int RoadID = 0; UnassignedLanes.Count > 0; RoadID++) {
			//RoadList.Add (new Road ());
			GameObject RoadObject = new GameObject("Road " + RoadID);
			RoadObject.transform.SetParent (RoadCategory.transform);

			// Iterate backwards so removing values doesn't mess with our iteration
			for(int CompareLaneID = UnassignedLanes.Count-1;CompareLaneID >= 0;CompareLaneID--)
			{
				if (Vector3.Distance (UnassignedLanes [0].GetPosition (0f), UnassignedLanes [CompareLaneID].GetPosition (0f)) <= 20f) {
					if (Vector3.Distance (UnassignedLanes [0].GetPosition (1f), UnassignedLanes [CompareLaneID].GetPosition (1f)) <= 20f) {
						//RoadList [RoadID].Lanes.Add (new Lane( UnassignedLanes [CompareLaneID]);
						UnassignedLanes [CompareLaneID].name = "Lane: " + RoadObject.transform.childCount + " R: " + RoadID;
						UnassignedLanes[CompareLaneID].transform.SetParent(RoadObject.transform);
						UnassignedLanes.RemoveAt (CompareLaneID);
					}
				}
			}
		}

		for (int IntersectionID = 0; UnassignedIntersectionLanes.Count > 0; IntersectionID++) {
			GameObject IntersectionObject = new GameObject ("Intersection " + IntersectionID);
			IntersectionObject.transform.SetParent (IntersectionCategory.transform);

			List<LaneBezierHandler> MatchingIntersection = new List<LaneBezierHandler> ();

			for (int CompareIntersectionID = UnassignedIntersectionLanes.Count - 1; CompareIntersectionID >= 0; CompareIntersectionID--) {
				bool IsCloseEnough = false;

				for (int i = 0; i < MatchingIntersection.Count; i++) {
					float OtherDistStart = Vector3.Distance (MatchingIntersection [i].GetPosition (0f), UnassignedIntersectionLanes [CompareIntersectionID].GetPosition (0f));
					float OtherDistEnd = Vector3.Distance (MatchingIntersection [i].GetPosition (1f), UnassignedIntersectionLanes [CompareIntersectionID].GetPosition (1f));
					float OtherDistStartEnd = Vector3.Distance (MatchingIntersection [i].GetPosition (0f), UnassignedIntersectionLanes [CompareIntersectionID].GetPosition (1f));
					float OtherDistEndStart = Vector3.Distance (MatchingIntersection [i].GetPosition (1f), UnassignedIntersectionLanes [CompareIntersectionID].GetPosition (0f));

					if (OtherDistStart <= 30f || OtherDistEnd <= 30f || OtherDistStartEnd <= 30f || OtherDistStartEnd <= 30f) {
						IsCloseEnough = true;
						break;
					}
				}

				if (!IsCloseEnough) { 
					float DistStart = Vector3.Distance (UnassignedIntersectionLanes [0].GetPosition (0f), UnassignedIntersectionLanes [CompareIntersectionID].GetPosition (0f));
					float DistEnd = Vector3.Distance (UnassignedIntersectionLanes [0].GetPosition (1f), UnassignedIntersectionLanes [CompareIntersectionID].GetPosition (1f));
					float DistStartEnd = Vector3.Distance (UnassignedIntersectionLanes [0].GetPosition (0f), UnassignedIntersectionLanes [CompareIntersectionID].GetPosition (1f));
					float DistEndStart = Vector3.Distance (UnassignedIntersectionLanes [0].GetPosition (1f), UnassignedIntersectionLanes [CompareIntersectionID].GetPosition (0f));

					Vector3 StartPosA = UnassignedIntersectionLanes [0].GetPosition (0f);
					Vector3 StartPosB = UnassignedIntersectionLanes [CompareIntersectionID].GetPosition (0f);

					if (DistStart <= 30f || DistEnd <= 30f || DistStartEnd <= 30f || DistEndStart <= 30f)
						IsCloseEnough = true;
				} 

				if(IsCloseEnough) {
					UnassignedIntersectionLanes [CompareIntersectionID].name = "Lane: " + IntersectionObject.transform.childCount + " I: " + IntersectionID; // DEBUG
					MatchingIntersection.Add (UnassignedIntersectionLanes [CompareIntersectionID]);
					UnassignedIntersectionLanes [CompareIntersectionID].transform.SetParent (IntersectionObject.transform);
					UnassignedIntersectionLanes.RemoveAt (CompareIntersectionID);

					// Need to restart iteration as this newly found road now needs to be compared against all the roads we already checked
					// If the ID is 0 then we just added the comparison position and need to now move on to the next position
					if(CompareIntersectionID > 0){
						CompareIntersectionID = UnassignedIntersectionLanes.Count; // This will be minus 1 when the interation restarts
					}
				}
			}
		}

		Debug.Log ("Done grouping lanes!");
	}

	[ContextMenu("Auto Populate Sounds and Particles")]
	public void AutoPopulateSoundsParticles()
	{
		// Path of this script
		string CurScriptPath = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(this));
		string HitParticlePrefabPath = CurScriptPath.Replace("Scripts/TrafficLaneManager.cs","HitParticle/HitParticle.prefab");
		string VehicleSoundsPath = CurScriptPath.Replace("Scripts/TrafficLaneManager.cs","Vehicle Templates/Sounds");

		HitParticle = (GameObject)AssetDatabase.LoadAssetAtPath(HitParticlePrefabPath, typeof(GameObject));

		BigHitSound = (AudioClip)AssetDatabase.LoadAssetAtPath(VehicleSoundsPath + "/HardCrash.mp3", typeof(AudioClip));
		SmallHitSound = (AudioClip)AssetDatabase.LoadAssetAtPath(VehicleSoundsPath + "/LowCrash.wav", typeof(AudioClip));
	}

	[ContextMenu("Auto Populate Car Templates")]
	public void AutoPopulateCarTemplates()
	{
		// Path of this script
		string CurScriptPath = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(this));
		string VehicleTemplatesPrefabsPath = CurScriptPath.Replace("Scripts/TrafficLaneManager.cs","Vehicle Templates/Prefabs");

		string[] VehiclePrefabs = AssetDatabase.FindAssets("t:prefab", new string[1]{ VehicleTemplatesPrefabsPath });

		Debug.Log("Found " + VehiclePrefabs.Length + " vehicle prefabs in " + VehicleTemplatesPrefabsPath);

		CarTemplates.Clear();

		for(int i=0;i < VehiclePrefabs.Length;i++)
			CarTemplates.Add(new CarData((GameObject)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(VehiclePrefabs[i]), typeof(GameObject))));

		Debug.Log("Done assigning vehicle prefabs to car templates!");
	}

	[ContextMenu("Auto Create Layers")]
	public void AutoCreateLayers()
	{
		// There's currently no direct way to create layers via script in the editor without using reflection to modify the TagManager asset
		SerializedObject TagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
		SerializedProperty LayerProperties = TagManager.FindProperty("layers");
		int LayerCount = LayerProperties.arraySize;
		string[] LayersToCreate = new string[3]{"Road", "Player", "AIVehicle"};

		foreach(string WantedLayer in LayersToCreate)
		{
			for(int i=0;i < LayerCount;i++)
			{
				SerializedProperty LayerProperty = LayerProperties.GetArrayElementAtIndex(i);
				string LayerName = LayerProperty.stringValue;

				// If the wanted layer already exists then we can break out of the inner for loop
				if(LayerName == WantedLayer){
					Debug.Log("Not creating layer " + WantedLayer + " as it already exists");
					break;
				}

				// The first 8 layers are builtin unchangable layers, or skip if this layer isn't blank
				if(i < 8 || LayerName != string.Empty) continue;

				LayerProperty.stringValue = WantedLayer;

				Debug.Log("New layer " + WantedLayer + " created successfully!");
				break;
			}
		}

		TagManager.ApplyModifiedProperties();

		RoadLayer = LayerMask.GetMask("Road");
		PlayerLayer = LayerMask.GetMask("Player");
		AILayer = LayerMask.GetMask("AIVehicle");

		Debug.Log("Done creating layers & setting layer masks!");
	}

	[ContextMenu("Make lane starts face end")]
	public void StartFaceEnds()
	{
		int TotalMaps = transform.childCount;

		MapCount = TotalMaps;

		for(int MapId = 0; MapId < TotalMaps; MapId++)
		{
			Transform CurMapTransform = transform.GetChild(MapId);

			TrafficData.Add(new MapTrafficData());

			int TotalRoads = 0;
			int TotalRoadId = 0;

			for(int CatId=0; CatId < CurMapTransform.childCount;CatId++){
				Transform CurCatTransform = CurMapTransform.GetChild(CatId);

				TotalRoads += CurCatTransform.childCount;

				for(int RoadId=0;TotalRoadId < TotalRoads;RoadId++, TotalRoadId++){
					Transform CurRoad = CurCatTransform.GetChild(RoadId);

					int TotalLanes = CurRoad.childCount;

					for(int LaneId=0; LaneId < TotalLanes;LaneId++){
						Transform LaneStart = CurRoad.GetChild(LaneId);
						Transform LaneEnd = LaneStart.GetComponentsInChildren<LaneBezierHandler>()[1].transform;

						Vector3[] AllChildPositions = new Vector3[LaneStart.childCount];
						Quaternion[] AllChildRotations = new Quaternion[LaneStart.childCount];

						for(int i=0;i < LaneStart.childCount;i++)
						{
							Transform CurLaneChild = LaneStart.GetChild(i);

							AllChildPositions[i] = CurLaneChild.position;
							AllChildRotations[i] = CurLaneChild.rotation;
						}

						LaneStart.LookAt(LaneEnd.position);

						for(int i=0;i < LaneStart.childCount;i++)
						{
							Transform CurLaneChild = LaneStart.GetChild(i);

							CurLaneChild.position = AllChildPositions[i];
							CurLaneChild.rotation = AllChildRotations[i];
						}
					}
				}
			}
		}

		Debug.Log("Lane starts rotated!");
	}

	[ContextMenu("Auto Find Lanes")]
	public void AutoFindLanes()
	{
		TrafficData.Clear ();

		int TotalMaps = transform.childCount;

		MapCount = TotalMaps;

		for (int MapID = 0; MapID < TotalMaps; MapID++) {
			Transform CurMapTransform = transform.GetChild (MapID);

			TrafficData.Add (new MapTrafficData ());

			int TotalRoads = 0;
			int TotalRoadID = 0;

			for (int CatID = 0; CatID < CurMapTransform.childCount; CatID++) {
				Transform CurCatTransform = CurMapTransform.GetChild (CatID);

				TotalRoads += CurCatTransform.childCount;

				for (int RoadID = 0; TotalRoadID < TotalRoads; RoadID++,TotalRoadID++) {
					Transform CurRoad = CurCatTransform.GetChild (RoadID);

					TrafficData [MapID].RoadData.Add (new Road ());
					Road CurRoadData = TrafficData[MapID].RoadData[TotalRoadID];

					CurRoadData.RoadObj = CurRoad.gameObject;
					CurRoadData.IntersectionID = CurRoad.name.ToLowerInvariant ().Contains ("intersection") ? RoadID : -1;

					int TotalLanes = CurRoad.childCount;
					
					for (int LaneID = 0; LaneID < TotalLanes; LaneID++) {
						Transform CurLane = CurRoad.GetChild (LaneID);
						CurRoadData.Lanes.Add (new Lane ());

						CurRoadData.Lanes [LaneID].RoadID = TotalRoadID;
						CurRoadData.Lanes [LaneID].StartLane = CurLane;

						LaneBezierHandler CurLaneBezierHandlerStart = CurLane.GetComponent<LaneBezierHandler>();
						LaneBezierHandler CurLaneBezierHandlerEnd = CurLane.GetComponentsInChildren<LaneBezierHandler>()[1];

						CurRoadData.Lanes [LaneID].Bezier = CurLaneBezierHandlerStart;
						CurRoadData.Lanes [LaneID].EndLane = CurLaneBezierHandlerEnd.transform;

						// Update the cached length of the lane
						CurLaneBezierHandlerStart.GetLength(50);

						// Update the cached lane directions
						for(int i=0;i <= 5;i++)
							CurLaneBezierHandlerStart.GetDirection((float)i * 0.2f);

						CurLaneBezierHandlerStart.SetDirty();

						CurRoadData.Lanes [LaneID].WaitForClearIntersection = CurRoadData.Lanes [LaneID].Bezier.WaitForClearIntersection;
						CurRoadData.Lanes [LaneID].DontWaitForThisLane = CurRoadData.Lanes [LaneID].Bezier.DontWaitForThisLane;

						//CurRoadData.Lanes [LaneID].Bezier.SnapPointsToRoadSurface ();
					}

					// Sort the lanes within this road from left to right
					List<Lane> SortedLanes = new List<Lane>();

					for(int i=0;i < CurRoadData.Lanes.Count; i++){
						float LeftmostVal = 0f;
						int LeftmostId = -1;

						for(int CompareLaneId=0;CompareLaneId < CurRoadData.Lanes.Count; CompareLaneId++){
							bool HasLaneAlreadySorted = false;

							foreach(Lane SortedLane in SortedLanes)
								if(CurRoadData.Lanes[CompareLaneId] == SortedLane)
									HasLaneAlreadySorted = true;

							if(HasLaneAlreadySorted) continue;

							if(LeftmostId < 0)
								LeftmostId = CompareLaneId;

							float CompareVal = CurRoadData.Lanes[i].StartLane.InverseTransformPoint(CurRoadData.Lanes[CompareLaneId].StartLane.position).x;

							if(CompareVal <= LeftmostVal){
								LeftmostVal = CompareVal;
								LeftmostId = CompareLaneId;
							}
						}

						SortedLanes.Add(CurRoadData.Lanes[LeftmostId]);
						CurRoadData.Lanes[LeftmostId].StartLane.name = "Lane: " + i + " R: " + RoadID;
					}

					CurRoadData.Lanes = SortedLanes;

					CurRoadData.LaneCount = CurRoadData.Lanes.Count;
				}
			}

			TrafficData [MapID].RoadCount = TrafficData [MapID].RoadData.Count;
		}

		// We're done setting up the RoadData but now we need to set the Joint Lanes (we do this by detecting start positions on top of end positions)
		for (int MapID = 0; MapID < TotalMaps; MapID++) {
			for (int RoadID = 0; RoadID < TrafficData [MapID].RoadData.Count; RoadID++) {
				Road CurRoad = TrafficData[MapID].RoadData [RoadID];

				for (int LaneID = 0; LaneID < CurRoad.Lanes.Count; LaneID++) {
					Lane CurLane = CurRoad.Lanes [LaneID];

					if (CurLane.EndLane == null) {
						Debug.LogError ("Error! Road missing end point!", CurLane.StartLane.gameObject);
					}
					Vector3 EndPointPos = CurLane.EndLane.position;

					CurLane.JointLanes = ScanJointRoads (EndPointPos);
					CurLane.JointLaneCount = CurLane.JointLanes.Count;
				}
			}
		}

		// Set lane intersection IDs (determine if the road is an intersection, if it is assign it an ID to group it with other roads on that intersection)
		// The intersection IDs are used to stop AI driving through an intersection until the other roads are clear
		for (int MapID = 0; MapID < TotalMaps; MapID++) {
			for (int RoadID = 0; RoadID < TrafficData[MapID].RoadData.Count; RoadID++) {
				Road CurRoad = TrafficData[MapID].RoadData [RoadID];

				if (CurRoad.RoadObj.name.ToLowerInvariant ().Contains ("intersection")) {
					for (int LaneID = 0; LaneID < CurRoad.Lanes.Count; LaneID++) {
						Lane CurLane = CurRoad.Lanes [LaneID];


					}
				}
			}
		}

		Debug.Log("TrafficLaneManager RoadData regenerated!");
	}

	private List<JointLaneData> ScanJointRoads(Vector3 EndPointPos)
	{
		List<JointLaneData> JointRoads = new List<JointLaneData> ();

		for (int MapID = 0; MapID < MapCount; MapID++) {
			for (int RoadID = 0; RoadID < TrafficData[MapID].RoadData.Count; RoadID++) {
				Road CurRoad = TrafficData[MapID].RoadData [RoadID];

				for (int LaneID = 0; LaneID < CurRoad.Lanes.Count; LaneID++) {
					Lane CurLane = CurRoad.Lanes [LaneID];

					if (CurLane == null) {
						Debug.LogError ("CurLane is null! RoadID: " + RoadID + ", LaneID: " + LaneID);
					} else if (CurLane.StartLane == null) {
						Debug.LogError ("StartLane is null! RoadID: " + RoadID + ", LaneID: " + LaneID);
					}

					if (Vector3.Distance (EndPointPos, CurLane.StartLane.position) < 3f) {
						JointRoads.Add (new JointLaneData (CurLane.RoadID, LaneID));
					}
				}
			}
		}

		return JointRoads;
	}
	#endif

	private int CachedActiveMap = -1;
	private MapTrafficData ActiveMapTrafficData;

	// List of transforms which is used by the traffic to check for blockages
	private List<Transform> TrafficMonitoredTransforms = new List<Transform>();

	private int CachedTrafficMonitoredTransformCount;

	public void SetActiveMap(int MapID)
	{
		CachedActiveMap = MapID;

		ActiveMapTrafficData = TrafficData[CachedActiveMap];

		ResetAllClosedRoads();
	}

	public void ClearMonitoredTransforms()
	{
		TrafficMonitoredTransforms.Clear();
		CachedTrafficMonitoredTransformCount = 0;
	}

	public void AddMonitoredTransform(Transform InTransform)
	{
		TrafficMonitoredTransforms.Add(InTransform);
		CachedTrafficMonitoredTransformCount++;
	}

	public void RemoveMonitoredTransform(Transform InTransform)
	{
		for(int i=0;i < TrafficMonitoredTransforms.Count;i++)
		{
			if(TrafficMonitoredTransforms[i] == InTransform){
				TrafficMonitoredTransforms.RemoveAt(i);
				CachedTrafficMonitoredTransformCount--;
			}
		}


	}

	void FixedUpdate()
	{
		if(TotalAIVehicles <= 0 || CachedTrafficMonitoredTransformCount <= 0 || CachedActiveMap < 0) return;

		// Update the progress of AI vehicles in the road lanes
		for (int RoadID = 0; RoadID < ActiveMapTrafficData.RoadCount; RoadID++) {
			Road CurRoad = ActiveMapTrafficData.RoadData[RoadID];

			bool IsRoadNearPlayer = true; // This is set to false and we stop evaluating lanes and vehicles in the lane once a single car is not in calculation mode 2 or less on this road

			// Update the movement of all AI inside lanes of this road
			for(int LaneID = 0; LaneID < CurRoad.Lanes.Count && IsRoadNearPlayer; LaneID++)
			{
				Lane CurLane = CurRoad.Lanes[LaneID];

				for(int VehicleID = 0; VehicleID < CurLane.TotalActiveAIVehicles && IsRoadNearPlayer; VehicleID++)
				{
					AIVehicleLaneData CurVehicle = CurLane.ActiveAIVehicles[VehicleID];

					if(CurVehicle.CalculationMode <= 2){
						CarData CurCarData = CarObjects[CurVehicle.VehicleID];

						// If a car is not marked as IsOnRoad then it has been hit and is sitting with hazards enabled
						if(CurCarData.IsOnRoad || CurCarData.IsAllowedOffRoadMovement){
							// If the wanted position is very far from the target move it instantly without interpolation
							if(CurCarData.VehicleRigidbody.position != DespawnCarPosition){
								// Move the vehicle rigidbody to the wanted position
								if(CurCarData.IsAllowedOffRoadMovement || CurCarData.IsChangingLane){
									CurCarData.VehicleRigidbody.MovePosition(Vector3.Lerp(CurCarData.VehicleRigidbody.position, CurCarData.LastTargetPosition + CurCarData.LaneChangePositionOffset, 15f * Time.deltaTime));
								} else {
									CurCarData.VehicleRigidbody.MovePosition(Vector3.Lerp(CurCarData.VehicleRigidbody.position, CurCarData.LastTargetPosition, 15f * Time.deltaTime));
								}
							} else {
								CurCarData.VehicleRigidbody.position = CurCarData.LastTargetPosition;
							}
						}
					} else {
						IsRoadNearPlayer = !ShortRoadOptimization;
					}
				}
			}
		}
	}

	private int RaycastCounterFrame = 0;
	private int FrameCounter = 0;

	void Update()
	{
		if(TotalAIVehicles <= 0 || CachedTrafficMonitoredTransformCount <= 0 || CachedActiveMap < 0) return;

		#if UNITY_EDITOR
			if(EditorDebugMode){
				RaycastCounterFrame += RaycastsThisFrame;
				FrameCounter++;

				if(FrameCounter >= 30){
					Debug.Log("Ran " + RaycastCounterFrame + " raycast" + (RaycastCounterFrame != 1 ? "s" : "") + " in last " + FrameCounter + " frames!");

					FrameCounter = 0;
					RaycastCounterFrame = 0;
				}
			}
		#endif

		RaycastsThisFrame = 0;

		float DeltaTime = Time.deltaTime;

		// Update the progress of AI vehicles in the road lanes
		for (int RoadID = 0; RoadID < ActiveMapTrafficData.RoadCount; RoadID++) {
			// Update the movement of all AI inside lanes of this road
			UpdateAIMovement (ActiveMapTrafficData.RoadData[RoadID], DeltaTime);
		}
	}

	public void DespawnAllTraffic()
	{
		if(TotalAIVehicles <= 0 || CachedActiveMap < 0) return;

		StartCoroutine(DoDespawnAllTraffic());
	}

	private IEnumerator DoDespawnAllTraffic()
	{
		TotalAIVehicles = 0;

		for (int i = 0; i < CarObjects.Count; i++){
			Destroy (CarObjects [i].VehicleObj);

			// Wait a frame for every 20 vehicles processed
			if(i % 20 == 0) yield return null;
		}

		CarObjects.Clear ();

		for (int RoadID = 0; RoadID < ActiveMapTrafficData.RoadData.Count; RoadID++) {
			for (int LaneID = 0; LaneID < ActiveMapTrafficData.RoadData [RoadID].Lanes.Count; LaneID++) {
				Lane CurLane = ActiveMapTrafficData.RoadData [RoadID].Lanes [LaneID];

				CurLane.ActiveAIVehicles.Clear ();
				CurLane.TotalActiveAIVehicles = 0;
				CurLane.TotalInactiveAIVehicles = 0;
			}
		}
	}

	public void SpawnRandomTrafficVehicle(int ForceRoad = -1, int ForceLane = -1)
	{
		int RandomRoad = ForceRoad < 0 ? Random.Range (0, ActiveMapTrafficData.RoadCount) : ForceRoad;
		Road SelectedRoad = ActiveMapTrafficData.RoadData [RandomRoad];

		// Don't spawn new traffic on intersections, it just causes issues
		if (SelectedRoad.IntersectionID >= 0) return;

		int RandomLane = ForceLane < 0 ? Random.Range (0, SelectedRoad.LaneCount) : ForceLane;
		Lane SelectedLane = SelectedRoad.Lanes [RandomLane];

		float SpawnPosition = Random.Range (0f, 1f);

		if (IsVehicleInProgress (SelectedLane, SpawnPosition - (DistanceBetweenVehicles / SelectedLane.Bezier.GetLength ()), 0, 2f) == BlockageType.None){
			Vector3 StartPosition = SelectedLane.Bezier.GetPosition (0f);

			// Don't allow traffic to spawn closer than 100m of monitored transforms to ensure they don't spawn inside them
			foreach(Transform CurTransform in TrafficMonitoredTransforms)
			{
				if(Vector3.Distance(StartPosition, CurTransform.position) < 100f)
					return;
			}

			// Register the new vehicle with the unique vehicle ID to reference back to the gameobject
			SelectedLane.ActiveAIVehicles.Add (new AIVehicleLaneData (CarObjects.Count, SpawnPosition));
			SelectedLane.TotalActiveAIVehicles++;

			// Increase the global count of AI vehicles
			TotalAIVehicles++;

			Quaternion StartRotation = SelectedLane.Bezier.GetDirection (0f);

			// Instantiate the physical car gameobject and add it to a list
			int RandCarId = Random.Range(0, CarTemplates.Count);

			// If you decide to blacklist all AI cars that will cause an infinite loop here..
			//while(MissionManager.Instance.IsTrafficVehicleBlacklisted(RandCarId))
			//	RandCarId = RandCarId + 1 >= CarTemplates.Count ? 0 : RandCarId + 1;

			GameObject NewVehicle = Instantiate(CarTemplates[RandCarId].VehicleObj, StartPosition, StartRotation) as GameObject;

			VehicleRefData NewVehicleRefData = NewVehicle.GetComponent<VehicleRefData> ();

			CarObjects.Add(new CarData(NewVehicle, NewVehicleRefData.Wheels, NewVehicleRefData.HornAudio, NewVehicleRefData.Rigidbody, NewVehicleRefData.CarHandlerScript));
			CarData NewCarObject = CarObjects[CarObjects.Count - 1];

			NewCarObject.VehicleRigidbody.detectCollisions = false;
			NewVehicleRefData.CarHandlerScript.Setup(NewCarObject.VehicleRigidbody, NewCarObject, NewVehicleRefData.HazardLightsOff, NewVehicleRefData.HazardLightsOn);

			UpdateAICalculationMode(SelectedLane.ActiveAIVehicles[SelectedLane.TotalActiveAIVehicles - 1], SelectedLane, StartPosition);
		}

		// Scale the max raycasts per frame because if there's a LOT of AI vehicles we need to work harder to keep them aligned with the ground
		MaxRaycastsPerFrame = Mathf.CeilToInt(((float)TotalAIVehicles / 100f) * ((CachedGraphicsTier == GraphicsTier.Tier1) ? 1.75f : 2f));
	}

	public void UpdateAIMovement(Road CurRoad, float DeltaTime)
	{
		// This is assigned all the way out here because getting Vector3.zero is actually pretty expensive when called a lot
		// and CurVehiclePosition will never be used unless it has its variable set at the start of the iteration anyway
		Vector3 CurVehiclePosition = Vector3.zero;

		for (int LaneID = 0; LaneID < CurRoad.LaneCount; LaneID++) {
			Lane CurLane = CurRoad.Lanes [LaneID];

			int VehicleCount = CurLane.TotalActiveAIVehicles;

			if (VehicleCount > 0) {
				float RoadLength = CurLane.Bezier.GetLength ();
				float CarDistance = DistanceBetweenVehicles / RoadLength; // Min distance between vehicles

				// Iterate backwards so if any vehicles are removed from the lane it won't affect our iteration
				for (int i = VehicleCount - 1; i >= 0; i--) {
					AIVehicleLaneData CurVehicleLaneData = CurLane.ActiveAIVehicles [i];
					CarData CurCarData = CarObjects[CurVehicleLaneData.VehicleID];

					if(!CurCarData.IsOnRoad){
						if(CurVehicleLaneData.TimeUntilNextDistanceCheck <= 0f){
							CurVehiclePosition = CurLane.Bezier.GetPosition(CurVehicleLaneData.VehicleProgress, CurVehicleLaneData.CalculationMode < 3);
							float CalcModeTime = UpdateAICalculationMode(CurVehicleLaneData, CurLane, CurVehiclePosition);

							// If the player is far away from the AI car
							if(CurVehicleLaneData.CalculationMode >= 3){
								// Set the AI vehicle back on the road
								CurCarData.IsOnRoad = true;
								CurCarData.IsAllowedOffRoadMovement = false;
								CurCarData.CarHandler.SetHazardLights(false);

								CurCarData.VehicleRigidbody.isKinematic = true;
								CurCarData.VehicleRigidbody.useGravity = false;
							} else {
								CurVehicleLaneData.TimeUntilNextDistanceCheck = CalcModeTime;
							}
						} else {
							CurVehicleLaneData.TimeUntilNextDistanceCheck -= DeltaTime;
						}
					
						continue; // Exit early because this vehicle isn't on the road
					}

					CurCarData.Speed = CurVehicleLaneData.VehicleSpeed;

					// We only need the vehicle position at certain times so wrap it with this if statement for optimization
					if (CurVehicleLaneData.CalculationMode < 3 || CurVehicleLaneData.TimeUntilNextDistanceCheck <= 0f){
						CurVehiclePosition = CurLane.Bezier.GetPosition (CurVehicleLaneData.VehicleProgress, CurVehicleLaneData.CalculationMode < 3);
					}

					// If the wait timer for the next distance check reaches 0, recalculate the AI calculation mode (and use the return value as the time to wait to next check this)
					if (CurVehicleLaneData.TimeUntilNextDistanceCheck <= 0f){
						CurVehicleLaneData.TimeUntilNextDistanceCheck = UpdateAICalculationMode (CurVehicleLaneData, CurLane, CurVehiclePosition);
					}

					// Make sure there's no vehicles blocking our paths or we need to stop and wait in a traffic jam
					if (CurVehicleLaneData.TimeUntilNextProgressCheck <= 0f) {
						BlockageType CurBlockageType = IsVehicleInProgress (CurLane, CurVehicleLaneData.VehicleProgress, CurVehicleLaneData.CalculationMode);

						if (CurVehicleLaneData.VehicleProgress < 1f && CurBlockageType == BlockageType.None) {
							// Normal speed increase with no blockages
							if(CurVehicleLaneData.VehicleSpeed < 1f)
								CurVehicleLaneData.VehicleSpeed += DeltaTime;//Mathf.MoveTowards (CurVehicleLaneData.VehicleSpeed, 1f, DeltaTime);
						} else {
							// If the player is blocking this vehicle (or any traffic blocking this vehicle is being blocked by the player) then use the horn
							if (CurVehicleLaneData.CalculationMode <= 1 && CurVehicleLaneData.TimeUntilHornCanBeUsedAgain <= 0f && CurBlockageType == BlockageType.Player) {
								CurVehicleLaneData.TimeUntilHornCanBeUsedAgain = Random.Range (1f, 5f);
								CurCarData.HornAudio.pitch = Random.Range (0.9f, 1.1f); // Randomizing the pitch makes it feel more dynamic
								CurCarData.HornAudio.Play ();
							}

							if (CurVehicleLaneData.VehicleProgress >= 1f) {
								// Waiting at the end of a lane to move into the next one
								if (CurVehicleLaneData.VehicleSpeed <= 0f) {
									switch (CurVehicleLaneData.CalculationMode) {
										case 0:
											CurVehicleLaneData.TimeUntilNextProgressCheck = 0.1f;
											break;
										case 1:
											CurVehicleLaneData.TimeUntilNextProgressCheck = 0.25f;
											break;
										case 2:
											CurVehicleLaneData.TimeUntilNextProgressCheck = 0.5f;
											break;
										case 3:
											CurVehicleLaneData.TimeUntilNextProgressCheck = 1.5f;
											break;
									}
								}
							} else {
								if(CurCarData.IsChangingLane){
									CurCarData.LaneChangePositionOffset = Vector3.MoveTowards(CurCarData.LaneChangePositionOffset, Vector3.zero, 2.5f * Time.deltaTime);

									if(Vector3.Distance(CurCarData.LaneChangePositionOffset, Vector3.zero) <= 0.1f)
										CurCarData.IsChangingLane = false;
								} else {
									if(CurBlockageType == BlockageType.PlayerSiren){
										// Traffic behaviour doesn't change on intersections
										if(CurRoad.IntersectionID < 0){
											// If this vehicle is on the furthest left lane pull over
											if(LaneID <= 0){
												// Slow down
												if(CurCarData.Speed >= 0.3f)
													CurCarData.Speed -= Time.deltaTime;

												//CurCarData.IsOnRoad = false;
												//CurCarData.IsAllowedOffRoadMovement = true;
											} else {
												// Move to the left lane
												Lane LeftLane = CurRoad.Lanes[LaneID - 1];

												if(CanJoinLane(LeftLane, CurVehicleLaneData, DistanceBetweenVehicles, false)){
													// Add the vehicle into the new lane
													LeftLane.ActiveAIVehicles.Add (new AIVehicleLaneData (CurVehicleLaneData.VehicleID, CurVehicleLaneData.VehicleProgress, CurVehicleLaneData.VehicleSpeed));
													LeftLane.TotalActiveAIVehicles++;
													CurCarData.IsChangingLane = true;

													Vector3 FromLanePosition = CurLane.Bezier.GetPosition(CurVehicleLaneData.VehicleProgress, CurVehicleLaneData.CalculationMode < 3);
													Vector3 ToLanePosition = LeftLane.Bezier.GetPosition(CurVehicleLaneData.VehicleProgress, CurVehicleLaneData.CalculationMode < 3);
													CurCarData.LaneChangePositionOffset = FromLanePosition - ToLanePosition;

													// Remove the vehicle from the previous lane
													CurLane.ActiveAIVehicles.RemoveAt (i);
													CurLane.TotalActiveAIVehicles--;
												}
											}
										}
									} else {
										// There's traffic in front of the vehicle
										if(CurVehicleLaneData.VehicleSpeed > 0f){
											//CurVehicleLaneData.VehicleSpeed -= DeltaTime;//Mathf.MoveTowards (CurVehicleLaneData.VehicleSpeed, 0f, 3f * DeltaTime);
											CurVehicleLaneData.VehicleSpeed = 0f;
										} else if(CurVehicleLaneData.VehicleSpeed != 0f){
											CurVehicleLaneData.VehicleSpeed = 0f;
										}
									}
								}
							}
						}

						CurVehicleLaneData.LastBlockageReason = CurBlockageType;
					}

					// Calculation mode 3 doesn't render any vehicles, only moves the traffic data around to simulate the cars driving normally when out of vision
					if ((CurVehicleLaneData.CalculationMode < 3 && CurVehicleLaneData.VehicleSpeed > 0f)) {
						AlignToRoad (CurCarData, CurLane, CurVehicleLaneData, CurVehiclePosition, DeltaTime, false);
					}

					CurVehicleLaneData.VehicleProgress += (CurVehicleLaneData.VehicleSpeed / RoadLength) * DeltaTime * 15f;

					if (CurVehicleLaneData.VehicleProgress >= 1f) {
						// Clamp the value within range so it doesn't mess with our IsVehicleInProgress detection for vehicles in front of others
						CurVehicleLaneData.VehicleProgress = 1f;

						// Move the vehicle to one of the joint lanes
						if (CurLane.JointLaneCount > 0) {
							// We need to ensure all of the joint lanes have their starting part empty because joint lanes overlay from the starting point
							bool IsLaneJoinable = true;

							foreach (JointLaneData CurJointLaneData in CurLane.JointLanes) {
								Lane CurJointLane = GetLane (CurJointLaneData);

								if (!CanJoinLane (CurJointLane, CurVehicleLaneData, DistanceBetweenVehicles / CurJointLane.Bezier.GetLength (), true)) {
									IsLaneJoinable = false;
									break;
								}
							}

							//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name += "Joinable? " + IsLaneJoinable;

							if (IsLaneJoinable) {
								// Move the vehicle into one of the joint lanes
								Lane NewLane = GetLane (CurLane.JointLanes [Random.Range (0, CurLane.JointLaneCount)]);

								// Add the vehicle into the new lane
								NewLane.ActiveAIVehicles.Add (new AIVehicleLaneData (CurVehicleLaneData.VehicleID, 0f, CurVehicleLaneData.VehicleSpeed));
								NewLane.TotalActiveAIVehicles++;

								// Remove the vehicle from the previous lane
								CurLane.ActiveAIVehicles.RemoveAt (i);
								CurLane.TotalActiveAIVehicles--;
							} else {
								CurVehicleLaneData.VehicleSpeed = 0f;

								switch (CurVehicleLaneData.CalculationMode) {
									case 2:
										CurVehicleLaneData.TimeUntilNextProgressCheck = 0.5f;
										break;
									case 3:
										CurVehicleLaneData.TimeUntilNextProgressCheck = 1f;
										break;
								}
							}
						} else {
							CurVehicleLaneData.VehicleSpeed = 0f;

							// There's no joint lane set! I guess the vehicle will be stuck here forever
							CurVehicleLaneData.TimeUntilNextProgressCheck = 10f;
						}
					}

					CurVehicleLaneData.TimeUntilNextProgressCheck -= DeltaTime;
					CurVehicleLaneData.TimeUntilNextDistanceCheck -= DeltaTime;
					CurVehicleLaneData.TimeUntilHornCanBeUsedAgain -= DeltaTime;
				}
			}
		}
	}

	private int RaycastsThisFrame = 0;
	private int MaxRaycastsPerFrame;

	private void AlignToRoad(CarData CurCarData, Lane CurLane, AIVehicleLaneData CurVehicleLaneData, Vector3 WantedVehiclePosition, float DeltaTime, bool ForceRaycast = false)
	{
		int CalcMode = CurVehicleLaneData.CalculationMode;

		switch (CalcMode) {
			case 0:
			case 1:
			case 2:
				Quaternion WantedRotation = CurLane.Bezier.GetDirection(CurVehicleLaneData.VehicleProgress);
				float WantedRotationAngle = Mathf.LerpAngle(CurCarData.VehicleObj.transform.eulerAngles.y, WantedRotation.eulerAngles.y, 10f * DeltaTime);

				if(!NeverRaycast){
					float YPosChange = Mathf.Abs(WantedVehiclePosition.y - CurCarData.LastYBezierPos);
					CurCarData.LastYBezierPos = WantedVehiclePosition.y;

					// Align the X and Z with the ground normal
					RaycastHit Hit;

					bool DidRunAndHitRaycast = false;

					//if(ForceRaycast || (YPosChange >= ((0.00015f * (CalcMode + 1)) && (CurCarData.TimeSinceLastValidRaycast >= (CalcMode <= 1 ? (CalcMode + 1f) : (CalcMode + 2f)) * DeltaTime))){
					if(ForceRaycast || (!OnlyRaycastWhenActivated && (CurCarData.TimeSinceLastValidRaycast >= (CalcMode + 1f) * DeltaTime))){
						if(ForceRaycast || (!OnlyRaycastWhenActivated && (RaycastsThisFrame < MaxRaycastsPerFrame || (CalcMode == 0 && RaycastsThisFrame < (MaxRaycastsPerFrame * 2)) || CurCarData.TimeSinceLastValidRaycast > ((RaycastsThisFrame <= 0 ? 5f : 6f) * DeltaTime)))){
							RaycastsThisFrame++;

							if (Physics.Raycast (WantedVehiclePosition + (Vector3.up * 10f), Vector3.down, out Hit, 30f, RoadLayer)) {
								CurCarData.VehicleObj.transform.up = Hit.normal;

								if(ForceRaycast){// || CalcMode == 0){
									WantedVehiclePosition.y = Hit.point.y; // This will only be called when we're forcing a raycast (happens when spawning, switching lanes or as the calculation mode changes)
								} else {
									WantedVehiclePosition.y = Mathf.Lerp(CurCarData.LastYPoint, Hit.point.y, 30f * DeltaTime);
								}

								CurCarData.TimeSinceLastValidRaycast = 0f;
								CurCarData.RaycastUpVector = Hit.normal;
								CurCarData.LastYPoint = WantedVehiclePosition.y;
								CurCarData.RaycastYPoint = Hit.point.y;//WantedVehiclePosition.y;//Hit.point.y;

								DidRunAndHitRaycast = true;

								//CurCarData.CarHandler.gameObject.name = Time.frameCount + ", " + WantedVehiclePosition.y + ", " + CurCarData.LastYPoint + ", " + Hit.point.y + ", " + ForceRaycast + ", " + CalcMode;
							}
						}
					}

					if(!DidRunAndHitRaycast) {
						CurCarData.TimeSinceLastValidRaycast += DeltaTime;
						CurCarData.VehicleObj.transform.up = CurCarData.RaycastUpVector;
						WantedVehiclePosition.y = Mathf.Lerp(CurCarData.LastYPoint, CurCarData.RaycastYPoint, 30f * DeltaTime);
						CurCarData.LastYPoint = WantedVehiclePosition.y;
					}
				}

				CurCarData.VehicleObj.transform.Rotate(Vector3.up, Mathf.DeltaAngle(CurCarData.VehicleObj.transform.eulerAngles.y, WantedRotationAngle), Space.Self);
				break;
		}

		if(CurVehicleLaneData.CalculationMode == 0){
			// Cached variables because repeating them 4 times per car builds up quite a bit..
			Vector3 V3Right = Vector3.right; // Referencing Vector3.right a lot actually becomes pretty expensive :o
			float WheelRotationDelta = 360f * DeltaTime * CurVehicleLaneData.VehicleSpeed;

			// Rotate the wheels
			foreach(Transform CurWheel in CarObjects[CurVehicleLaneData.VehicleID].Wheels)
				CurWheel.Rotate(V3Right, WheelRotationDelta, Space.Self);
		}

		CurCarData.LastTargetPosition = WantedVehiclePosition;
	}

	// Returns the time until the calculation mode should be re-checked
	public float UpdateAICalculationMode(AIVehicleLaneData LaneData, Lane CurLane, Vector3 Position)
	{
		float Distance = float.MaxValue;

		// The calculation mode distance is calculated depending on the closest monitored transform position
		foreach(Transform CurTransform in TrafficMonitoredTransforms)
		{
			float CurDistance = (CurTransform.position - Position).sqrMagnitude;

			if(CurDistance < Distance)
				Distance = CurDistance;
		}

		CarData CurCarData = CarObjects[LaneData.VehicleID];

		float DeviceTierAdjustment = 1f;

		// If the game is being ran on a potato we'll cut all the AI mode distances by a third
		if(CachedGraphicsTier == GraphicsTier.Tier1) DeviceTierAdjustment = 0.333f;

		if (Distance >= 40000f * DeviceTierAdjustment) {
			if (LaneData.CalculationMode != 3) {
				LaneData.CalculationMode = 3; // Best performance, only move vehicle no rendering or fancy bezier stuff

				// Disable the car rigidbody from detecting collisions
				CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;
				CurCarData.VehicleRigidbody.isKinematic = true;
				CurCarData.VehicleRigidbody.useGravity = false;
				CurCarData.VehicleRigidbody.detectCollisions = false;

				// We move the car underground AFTER disabling collisions, otherwise moving cars underground would cause them all to collider together!
				CurCarData.VehicleObj.transform.position = DespawnCarPosition; // Much cheaper than toggling the object or renderer (we ran tests)

				// sigh lets try disable too
				//CurCarData.VehicleObj.SetActive(false);
			}
				
			return 0.5f; // Half a second should be enough time to switch calculation mode before we get too close
		} else if (Distance >= 20000f * DeviceTierAdjustment) {
			if(LaneData.CalculationMode != 2){
				LaneData.CalculationMode = 2;

				if(CurCarData.IsOnRoad){
					// Disable the car rigidbody from detecting collisions
					CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;
					CurCarData.VehicleRigidbody.isKinematic = true;
					CurCarData.VehicleRigidbody.useGravity = false;
					CurCarData.VehicleRigidbody.detectCollisions = false;

					AlignToRoad(CurCarData, CurLane, LaneData, Position, 1f, true);
				} else {
					CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;
					CurCarData.VehicleRigidbody.isKinematic = false;
					CurCarData.VehicleRigidbody.useGravity = true;
					CurCarData.VehicleRigidbody.detectCollisions = true;
				}
			} else {
				if(CurCarData.IsOnRoad){
					// Always force position in this mode
					AlignToRoad(CurCarData, CurLane, LaneData, Position, 1f, false);
				}
			}

			return (CurCarData.IsOnRoad ? 0.2f : 3f);
		} else if(Distance >= 1000f * DeviceTierAdjustment){
			if(LaneData.CalculationMode != 1){
				LaneData.CalculationMode = 1; // No reaction to the player, road alignment is cheaper and bezier calculations are ran less often

				if(CurCarData.IsOnRoad){
					// Disable the car rigidbody from detecting collisions
					CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;
					CurCarData.VehicleRigidbody.isKinematic = true;
					CurCarData.VehicleRigidbody.useGravity = false;
					CurCarData.VehicleRigidbody.detectCollisions = true;

					AlignToRoad(CurCarData, CurLane, LaneData, Position, 1f, true);
				} else {
					CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;
					CurCarData.VehicleRigidbody.isKinematic = false;
					CurCarData.VehicleRigidbody.useGravity = true;
					CurCarData.VehicleRigidbody.detectCollisions = true;
				}
			}

			return (CurCarData.IsOnRoad ? 0.05f : 3f); // This needs to be updated more often than others as the player is pretty close to this vehicle
		} else {
			if(LaneData.CalculationMode != 0){
				LaneData.CalculationMode = 0; // AI cars do full bezier calculations, react to the player and align to the road correctly

				if(CurCarData.IsOnRoad){
					// Make sure to move the AI onto the road before enabling collisions or they'll explode as they hit anyone else moving
					AlignToRoad(CurCarData, CurLane, LaneData, Position, 1f, true);

					// Allow the car rigidbody to detect collisions
					CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;
					CurCarData.VehicleRigidbody.isKinematic = true;
					CurCarData.VehicleRigidbody.useGravity = false;
					CurCarData.VehicleRigidbody.detectCollisions = true;
				} else {
					CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;
					CurCarData.VehicleRigidbody.isKinematic = false;
					CurCarData.VehicleRigidbody.useGravity = true;
					CurCarData.VehicleRigidbody.detectCollisions = true;
				}
			}

			return 5f; // We don't need to worry about despawning cars at a distance as much as making them visible
		}
	}

	// Converts a lane start transform into Lane data
	public Lane GetLane(JointLaneData LaneData)
	{
		return ActiveMapTrafficData.RoadData [LaneData.RoadID].Lanes [LaneData.LaneID];
	}

	// Stops traffic joining certain roads, useful for making cars wait because of things like traffic accidents for special level scenarios
	public void CloseRoad(int RoadID)
	{
		ActiveMapTrafficData.RoadData[RoadID].RoadClosed = true;
	}

	public void CloseIntersection(int IntersectionID)
	{
		for(int i=0;i < ActiveMapTrafficData.RoadData.Count;i++)
		{
			if(IntersectionID == ActiveMapTrafficData.RoadData[i].IntersectionID)
				ActiveMapTrafficData.RoadData[i].RoadClosed = true;
		}
	}

	public void ResetAllClosedRoads()
	{
		for(int i=0;i < ActiveMapTrafficData.RoadData.Count;i++)
		{
			ActiveMapTrafficData.RoadData[i].RoadClosed = false;
		}
	}

	public bool CanJoinLane(Lane NewLane, AIVehicleLaneData CurVehicleLaneData, float CarDistance, bool IsRoadChange = false)
	{
		// Only allow 1 vehicle on an intersection at once (because lanes cross over each other at intersections)
		if (IsRoadChange) {
			Road CurRoad = ActiveMapTrafficData.RoadData [NewLane.RoadID];

			// The road we are trying to join is closed
			if(CurRoad.RoadClosed) return false;

			// IntersectionID is -1 if the road is not an intersection
			if (CurRoad.IntersectionID >= 0) {

				// Stop the AI moving across an intersection if there's any monitored vehicles nearby
				/*for(int i=0;i < TrafficMonitoredTransforms.Count;i++)
				{
					if(Vector3.Distance(NewLane.StartLane.transform.position, TrafficMonitoredTransforms[i].position) <= 100f)
						return false;
				}*/ // Nidoran

				// Only wait to join the lane if it's marked as WaitForClearIntersection
				if (NewLane.WaitForClearIntersection) {
					// Check for any vehicles on any other lanes with this IntersectionID
					for (int i = 0; i < CurRoad.LaneCount; i++) {
						Lane ComparisonLane = CurRoad.Lanes [i];

						// Ignore the check if we're looking at the same lane
						if (NewLane != ComparisonLane) {
							// If the current comparison lane is marked as DontWaitForThisLane then we can just ignore it
							if (!ComparisonLane.DontWaitForThisLane) {
								if (ComparisonLane.TotalActiveAIVehicles > 0) {
									//CarObjects[CurVehicleLaneData.VehicleID].VehicleObj.name = "[WFCI]";
									return false; // We are a black lane waiting for a blue lane to clear to join the road
								}
							}
						}
					}
				} else if (!NewLane.DontWaitForThisLane) {
					// We also need to wait if this is a normal intersection if there's cars in the WaitForClearIntersection lanes incase they get stuck waiting in the intersection
					for (int i = 0; i < CurRoad.LaneCount; i++) {
						Lane ComparisonLane = CurRoad.Lanes [i];

						// If the lane we're checking if a wait for clear intersection lane then we need to make sure it's empty before we can enter the lane
						if (ComparisonLane.WaitForClearIntersection) {
							if (ComparisonLane.TotalActiveAIVehicles > 0) {
								//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name = "[WFDWFTL]";
								return false; // We are a blue lane and the black lane overlapping our lane has cars in it, wait for them to clear
							}
						}
					}
				}
			} else {
				BlockageType BlockStatus = IsVehicleInProgress(NewLane, 0f, 0);

				// Make sure there's no cars at the start of the road
				return (BlockStatus == BlockageType.None) || (BlockStatus == BlockageType.PlayerSiren);
			}
		} else {
			// Vehicle is just changing lane
			//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name = "[NRC] " + NewLane.RoadID;

			// Make sure there's atleast CarDistance in front and (CarDistance * 2) behind the vehicle
			return IsVehicleInProgress (NewLane, CurVehicleLaneData.VehicleProgress - (CarDistance * 2f), CurVehicleLaneData.CalculationMode) == BlockageType.None;
		}

		// Not possible to reach
		return true;

	}

	public Vector2 GetVector2NoY(Vector3 Input)
	{
		return new Vector2 (Input.x, Input.z);
	}

	public Vector3 GetVector3CustomY(Vector2 Input, float CustomY)
	{
		return new Vector3 (Input.x, CustomY, Input.y);
	}

	public enum BlockageType { Player, AI, PlayerSiren, None }

	private float LaneWidth = 2f;

	// Test for any vehicles within a certain range of progress in the selected lane
	public BlockageType IsVehicleInProgress(Lane SelectedLane, float MinProgress, int CalculationMode = 0, float DistanceMultiplier = 1f)
	{
		// Detection for other traffic vehicles
		float TrafficDistance = (DistanceBetweenVehicles / SelectedLane.Bezier.GetLength()) * DistanceMultiplier;
		float MaxProgress = MinProgress + TrafficDistance;

		BlockageType TrafficBlockageType = GetTrafficBlockState(SelectedLane, MinProgress, MaxProgress);

		if(TrafficBlockageType != BlockageType.None)
			return TrafficBlockageType;

		// Only check for monitored transforms if we're in calculation mode 1 or lower
		if(CalculationMode <= 1){
			foreach(Transform CurTransform in TrafficMonitoredTransforms)
			{
				// If max progress is 1f or greater we also need to check the joining lanes
				if(MaxProgress >= 1f){
					for(int JointLaneId=0;JointLaneId < SelectedLane.JointLaneCount;JointLaneId++)
					{
						Lane CurJointLane = GetLane(SelectedLane.JointLanes[JointLaneId]);
						float CurLaneVehicleDistance = (DistanceBetweenVehicles / CurJointLane.Bezier.GetLength());

						// Using -1f as the min distance because this vehicle isn't on the target lane and we want to check it from the start
						BlockageType MonitoredBlockageType = GetTrafficBlockState(CurJointLane, -1f, CurLaneVehicleDistance);

						if(MonitoredBlockageType != BlockageType.None)
							return MonitoredBlockageType;
					}
				}

				if(CalculationMode <= 0){
					// Check if the monitored transform is in front of the AI
					Vector2 FlatMonitoredPosition = GetVector2NoY(CurTransform.position);

					Vector2 StartCenter = GetVector2NoY(SelectedLane.Bezier.GetPosition(MinProgress));
					Quaternion StartDirection = SelectedLane.Bezier.GetDirection(MinProgress);

					if(Vector2.Dot(GetVector2NoY(StartDirection * Vector3.forward), (FlatMonitoredPosition - StartCenter).normalized) >= 0){ // if is within start edge (-z dir)
						Vector2 EndCenter = GetVector2NoY(SelectedLane.Bezier.GetPosition(MaxProgress));
						Quaternion EndDirection = SelectedLane.Bezier.GetDirection(MaxProgress);

						if(Vector2.Dot(-GetVector2NoY(EndDirection * Vector3.forward), (FlatMonitoredPosition - EndCenter).normalized) >= 0){ // if is within end edge (+z dir)
							Vector2 StartRightDirection = GetVector2NoY (StartDirection * Vector3.right);
							Vector2 EndRightDirection = GetVector2NoY (EndDirection * Vector3.right);

							if(Vector2.Dot (StartRightDirection, (FlatMonitoredPosition - (StartCenter - (StartRightDirection * LaneWidth))).normalized) >= 0) // if is within left edge start
								if(Vector2.Dot (-StartRightDirection, (FlatMonitoredPosition - (StartCenter + (StartRightDirection * LaneWidth))).normalized) >= 0) // if is within left edge start
									return BlockageType.Player;

							if(Vector2.Dot (EndRightDirection, (FlatMonitoredPosition - (EndCenter - (EndRightDirection * LaneWidth))).normalized) >= 0) // if is within right edge end
								if(Vector2.Dot (-EndRightDirection, (FlatMonitoredPosition - (EndCenter + (EndRightDirection * LaneWidth))).normalized) >= 0) // if is within left edge end
									return BlockageType.Player;
						}
					}

					// If the player is behind AI with sirens on then the AI will move to the furthest left lane and slow down
					//if(VehicleManager.Instance.IsActiveVehicleSirenOn()){
						// We onlt need to check if the player is behind the vehicle as if the player is far away the traffic won't be in calculation mode 0 anyway
						//if(Vector3.Dot(GetVector2NoY(StartDirection * Vector3.back), (FlatMonitoredPosition - StartCenter).normalized) >= 0){
							//Vector2 SirenStartRightDirection = GetVector2NoY(StartDirection * Vector3.right);

							//if(Vector2.Dot(SirenStartRightDirection, (FlatMonitoredPosition - (StartCenter - (SirenStartRightDirection * LaneWidth))).normalized) >= 0)
							//	if(Vector2.Dot(-SirenStartRightDirection, (FlatMonitoredPosition - (StartCenter + (SirenStartRightDirection * LaneWidth))).normalized) >= 0)
							//		return BlockageType.PlayerSiren;
						//}
					//}
				}
			}
		}

		return BlockageType.None;
	}

	private BlockageType GetTrafficBlockState(Lane SelectedLane, float MinProgress, float MaxProgress)
	{
		// Test for other traffic vehicles within a certain range
		for(int i=0;i < SelectedLane.TotalActiveAIVehicles;i++)
		{
			AIVehicleLaneData CurVehicleLaneData = SelectedLane.ActiveAIVehicles[i];

			float Progress = CurVehicleLaneData.VehicleProgress;

			// Check if there's a vehicle on the section of the road lane we just queried
			if(CarObjects[CurVehicleLaneData.VehicleID].IsOnRoad){
				if(Progress > MinProgress && Progress < MaxProgress){
					if(CurVehicleLaneData.LastBlockageReason == BlockageType.Player){
						return BlockageType.Player;
					} else {
						return BlockageType.AI;
					}
				}
			}
		}

		return BlockageType.None;
	}

	// Draw a gizmos to the editor for debugging
	/*void OnDrawGizmosSelected()
	{
		for (int RoadID = 0; RoadID < RoadCount; RoadID++) {
			for (int LaneID = 0; LaneID < RoadData [RoadID].LaneCount; LaneID++) {
				Lane CurLane = RoadData [RoadID].Lanes [LaneID];

				// Iterate backwards so if any vehicles are removed from the lane it won't affect our iteration
				for (int i = CurLane.TotalActiveAIVehicles - 1; i >= 0; i--) {
					AIVehicleLaneData CurVehicleLaneData = CurLane.ActiveAIVehicles [i];
					Vector3 CurVehiclePosition = CurLane.Bezier.GetPosition (CurVehicleLaneData.VehicleProgress);

					Gizmos.color = Color.red;
					Gizmos.DrawSphere (CurVehiclePosition, 1f);
				}
			}
		}
	}*/
}
