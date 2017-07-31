using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

	public List<Lane> Lanes = new List<Lane>();
}

[System.Serializable]
public class CarData {
	public GameObject VehicleObj;
	public Rigidbody VehicleRigidbody;
	public List<Transform> Wheels { get; set; }
	public AudioSource HornAudio { get; set; }
	public float Speed { get; set; }

	public float TimeSinceLastValidRaycast = 0f;
	public Vector3 RaycastUpVector;
	public float RaycastYPoint;
	public float LastYBezierPos;

	public AICarHandler CarHandler;
	public bool IsOnRoad = true;

	public CarData (GameObject InObj, List<Transform> InWheels, AudioSource InHornAudio, Rigidbody InRigidbody, AICarHandler InCarHandler)
	{
		VehicleObj = InObj;
		Wheels = InWheels;
		VehicleRigidbody = InRigidbody;

		CarHandler = InCarHandler;
		HornAudio = InHornAudio;
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

	public bool CarsStopWhenHit = true; // Suggested true for car games, false for bike games

	public int MapCount; // Store the count of maps into a variable so the list doesn't need to be constantly counted
	public List<MapTrafficData> TrafficData = new List<MapTrafficData> ();

	public List<CarData> CarTemplates = new List<CarData>();
	public List<CarData> CarObjects = new List<CarData> ();

	public float DistanceBetweenVehicles = 15f; // This value is divided by the size of the road to give a consistant distance between vehicles (it's not in meters)

	public int TotalAIVehicles { get; set; }

	private Vector3 CachedPlayerPosition;
	public LayerMask RoadLayer;
	public LayerMask PlayerLayer;
	public LayerMask AILayer;
	public LayerMask DynamicAILayer;

	public GameObject HitParticle;
	public AudioSource HitSource;
	public AudioClip HitSound;
	public AudioClip SmallHitSound;

	public List<SparkData> SparkPool = new List<SparkData>();
	private int SparkID = 0;

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
			case 0: UnityEditor.EditorUtility.DisplayDialog ("No maps?", "There's no maps available to ungroup!", "Ok"); return; break;
			case 1: SelectedMapID = 0; break;
			case 2: SelectedMapID = (UnityEditor.EditorUtility.DisplayDialog ("Selected a map", "Which map do you want to ungroup the lanes of?\n(Closing this window will ungroup the last map)", MapNames[0], MapNames[1]) ? 0 : 1); break;
			case 3: SelectedMapID = UnityEditor.EditorUtility.DisplayDialogComplex ("Select a map", "Which map do you want to ungroup the lanes of?\n(Closing this window will ungroup the last map)", MapNames[0], MapNames[1], MapNames[2]); break;
			default: UnityEditor.EditorUtility.DisplayDialog ("Too many maps!", "This function isn't built to support over 3 maps, changes need to be made to UngroupAllLanes inside TrafficLaneManager.cs", "Ok"); return; break;
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
					TrafficData [MapID].RoadData [TotalRoadID].RoadObj = CurRoad.gameObject;
					TrafficData [MapID].RoadData [TotalRoadID].IntersectionID = CurRoad.name.ToLowerInvariant ().Contains ("intersection") ? RoadID : -1;

					int TotalLanes = CurRoad.childCount;

					for (int LaneID = 0; LaneID < TotalLanes; LaneID++) {
						Transform CurLane = CurRoad.GetChild (LaneID);
						TrafficData [MapID].RoadData [TotalRoadID].Lanes.Add (new Lane ());

						TrafficData [MapID].RoadData [TotalRoadID].Lanes [LaneID].RoadID = TotalRoadID;
						TrafficData [MapID].RoadData [TotalRoadID].Lanes [LaneID].StartLane = CurLane;
						TrafficData [MapID].RoadData [TotalRoadID].Lanes [LaneID].Bezier = CurLane.GetComponent<LaneBezierHandler> ();
						TrafficData [MapID].RoadData [TotalRoadID].Lanes [LaneID].EndLane = CurLane.GetComponentsInChildren<LaneBezierHandler> () [1].transform;

						TrafficData [MapID].RoadData [TotalRoadID].Lanes [LaneID].WaitForClearIntersection = TrafficData [MapID].RoadData [TotalRoadID].Lanes [LaneID].Bezier.WaitForClearIntersection;
						TrafficData [MapID].RoadData [TotalRoadID].Lanes [LaneID].DontWaitForThisLane = TrafficData [MapID].RoadData [TotalRoadID].Lanes [LaneID].Bezier.DontWaitForThisLane;

						//TrafficData [MapID].RoadData [TotalRoadID].Lanes [LaneID].Bezier.SnapPointsToRoadSurface ();
					}

					TrafficData [MapID].RoadData [TotalRoadID].LaneCount = TrafficData [MapID].RoadData [TotalRoadID].Lanes.Count;
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

					if (Vector3.Distance (EndPointPos, CurLane.StartLane.position) < 1f) {
						JointRoads.Add (new JointLaneData (CurLane.RoadID, LaneID));
					}
				}
			}
		}

		return JointRoads;
	}
	#endif

	private int CachedActiveMap = -1;
	private Transform CachedActiveVehicle = null;

	public void SetActiveMap(int MapID)
	{
		CachedActiveMap = MapID;
	}


	public void SetActiveVehicle(Transform VehicleTransform)
	{
		CachedActiveVehicle = VehicleTransform;
	}

	// We're using a FixedUpdate because otherwise the traffic movement would stutter
	void FixedUpdate()
	{
		if(!CachedActiveVehicle || CachedActiveMap < 0) return;

		CachedPlayerPosition = CachedActiveVehicle.position;

		// Update the progress of AI vehicles in the road lanes
		for (int RoadID = 0; RoadID < TrafficData[CachedActiveMap].RoadCount; RoadID++) {
			// Update the movement of all AI inside lanes of this road
			UpdateAIMovement (TrafficData[CachedActiveMap].RoadData[RoadID]);
		}
	}

	public void DespawnAllTraffic()
	{
		for (int i = 0; i < CarObjects.Count; i++)
			Destroy (CarObjects [i].VehicleObj);

		CarObjects.Clear ();

		for (int RoadID = 0; RoadID < TrafficData[CachedActiveMap].RoadData.Count; RoadID++) {
			for (int LaneID = 0; LaneID < TrafficData[CachedActiveMap].RoadData [RoadID].Lanes.Count; LaneID++) {
				Lane CurLane = TrafficData[CachedActiveMap].RoadData [RoadID].Lanes [LaneID];

				CurLane.ActiveAIVehicles.Clear ();
				CurLane.TotalActiveAIVehicles = 0;
				CurLane.TotalInactiveAIVehicles = 0;
			}
		}

		TotalAIVehicles = 0;
	}

	public void SpawnRandomTrafficVehicle(int ForceRoad = -1, int ForceLane = -1)
	{
		int RandomRoad = ForceRoad < 0 ? Random.Range (0, TrafficData[CachedActiveMap].RoadCount) : ForceRoad;
		Road SelectedRoad = TrafficData[CachedActiveMap].RoadData [RandomRoad];

		// Don't spawn new traffic on intersections, it just causes issues
		if (SelectedRoad.IntersectionID >= 0) return;

		int RandomLane = ForceLane < 0 ? Random.Range (0, SelectedRoad.LaneCount) : ForceLane;
		Lane SelectedLane = SelectedRoad.Lanes [RandomLane];

		float SpawnPosition = Random.Range (0f, 1f);
		float SpawnOffset = DistanceBetweenVehicles / SelectedLane.Bezier.GetLength ();

		if (IsVehicleInProgress (SelectedLane, SpawnPosition - SpawnOffset, 0, 2f) == BlockageType.None){
			// Register the new vehicle with the unique vehicle ID to reference back to the gameobject
			SelectedLane.ActiveAIVehicles.Add (new AIVehicleLaneData (CarObjects.Count, SpawnPosition));
			SelectedLane.TotalActiveAIVehicles++;

			// Increase the global count of AI vehicles
			TotalAIVehicles++;

			Vector3 StartPosition = SelectedLane.Bezier.GetPosition (0f);
			Quaternion StartRotation = SelectedLane.Bezier.GetDirection (0f);

			// Instantiate the physical car gameobject and add it to a list
			GameObject NewVehicle = Instantiate(CarTemplates[Random.Range(0, CarTemplates.Count)].VehicleObj, StartPosition, StartRotation) as GameObject;

			VehicleRefData NewVehicleRefData = NewVehicle.GetComponent<VehicleRefData> ();

			CarObjects.Add(new CarData(NewVehicle, NewVehicleRefData.Wheels, NewVehicleRefData.HornAudio, NewVehicleRefData.Rigidbody, NewVehicleRefData.CarHandlerScript));
			CarData NewCarObject = CarObjects[CarObjects.Count - 1];

			AlignToRoad(NewCarObject, SelectedLane, SelectedLane.ActiveAIVehicles[SelectedLane.TotalActiveAIVehicles - 1], StartPosition, 1f, true);

			NewCarObject.VehicleRigidbody.detectCollisions = false;
			NewVehicleRefData.CarHandlerScript.Setup(NewCarObject.VehicleRigidbody, NewCarObject, NewVehicleRefData.HazardLightsOff, NewVehicleRefData.HazardLightsOn);

			UpdateAICalculationMode(SelectedLane.ActiveAIVehicles[SelectedLane.TotalActiveAIVehicles - 1], SelectedLane, StartPosition);
		}
	}

	public void UpdateAIMovement(Road CurRoad)
	{
		float DeltaTime = Time.deltaTime;

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
								CurCarData.CarHandler.SetHazardLights(false);

								CurCarData.VehicleRigidbody.isKinematic = false;
								CurCarData.VehicleRigidbody.useGravity = true;
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
					if (CurVehicleLaneData.CalculationMode < 3 || CurVehicleLaneData.TimeUntilNextDistanceCheck <= 0f || CurVehicleLaneData.CalculationMode == 0)
						CurVehiclePosition = CurLane.Bezier.GetPosition (CurVehicleLaneData.VehicleProgress, CurVehicleLaneData.CalculationMode < 3);

					// If the wait timer for the next distance check reaches 0, recalculate the AI calculation mode (and use the return value as the time to wait to next check this)
					if (CurVehicleLaneData.TimeUntilNextDistanceCheck <= 0f){
						CurVehicleLaneData.TimeUntilNextDistanceCheck = UpdateAICalculationMode (CurVehicleLaneData, CurLane, CurVehiclePosition) + Random.Range (0f, 0.1f);
					}

					// Make sure there's no vehicles blocking our paths or we need to stop and wait in a traffic jam
					if (CurVehicleLaneData.TimeUntilNextProgressCheck <= 0f) {
						BlockageType CurBlockageType = IsVehicleInProgress (CurLane, CurVehicleLaneData.VehicleProgress, CurVehicleLaneData.CalculationMode);

						if (CurVehicleLaneData.VehicleProgress < 1f && CurBlockageType == BlockageType.None) {
							// Normal speed increase with no blockages
							CurVehicleLaneData.VehicleSpeed = Mathf.MoveTowards (CurVehicleLaneData.VehicleSpeed, 1f, DeltaTime);
						} else {
							// If the player is blocking this vehicle (or any traffic blocking this vehicle is being blocked by the player) then use the horn
							if (CurVehicleLaneData.CalculationMode <= 1 && CurVehicleLaneData.TimeUntilHornCanBeUsedAgain <= 0f && CurBlockageType == BlockageType.Player) {
								CurVehicleLaneData.TimeUntilHornCanBeUsedAgain = Random.Range (1f, 5f);
								CarObjects [CurVehicleLaneData.VehicleID].HornAudio.pitch = Random.Range (0.9f, 1.1f); // Randomizing the pitch makes it feel more dynamic
								CarObjects [CurVehicleLaneData.VehicleID].HornAudio.Play ();
							}

							if (CurVehicleLaneData.VehicleProgress >= 1f) {
								// Waiting at the end of a lane to move into the next one
								if (CurVehicleLaneData.VehicleSpeed <= 0f) {
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
								// There's traffic in front of the vehicle
								CurVehicleLaneData.VehicleSpeed = Mathf.MoveTowards (CurVehicleLaneData.VehicleSpeed, 0f, 3f * DeltaTime);
							}
						}

						CurVehicleLaneData.LastBlockageReason = CurBlockageType;
					}

					CurVehicleLaneData.VehicleProgress += ((15f * CurVehicleLaneData.VehicleSpeed) / RoadLength) * DeltaTime;

					// Calculation mode 2 doesn't render any vehicles, only moves the traffic data around to simulate the cars driving normally when out of vision
					if ((CurVehicleLaneData.CalculationMode < 2 && CurVehicleLaneData.VehicleSpeed > 0f)) {
						AlignToRoad (CurCarData, CurLane, CurVehicleLaneData, CurVehiclePosition, DeltaTime, false);
					}

					if (CurVehicleLaneData.VehicleProgress >= 1f) {
						// Clamp the value within range so it doesn't mess with our IsVehicleInProgress detection for vehicles in front of others
						CurVehicleLaneData.VehicleProgress = 1f;

						// Move the vehicle to one of the joint lanes
						if (CurLane.JointLaneCount > 0) {
							// We need to ensure all of the joint lanes have their starting part empty because joint lanes overlay from the starting point
							bool IsLaneJoinable = true;

							// Checking all the joint lanes for vehicles is (relatively) expensive so don't bother doing any checks in calculation mode 2
							// If any vehicles in calculation mode 2 drive inside each other one of them will detect the other and stop whilst moving later fixing the issue
							//if (CurVehicleLaneData.CalculationMode != 2) {
							foreach (JointLaneData CurJointLaneData in CurLane.JointLanes) {
								Lane CurJointLane = GetLane (CurJointLaneData);

								if (!CanJoinLane (CurJointLane, CurVehicleLaneData, DistanceBetweenVehicles / CurJointLane.Bezier.GetLength (), true)) {
									IsLaneJoinable = false;
									break;
								}
							}
							
							if (IsLaneJoinable) {
								// Move the vehicle into one of the joint lanes
								Lane NewLane = GetLane (CurLane.JointLanes [Random.Range (0, CurLane.JointLaneCount)]);

								// Add the vehicle into the new lane
								NewLane.ActiveAIVehicles.Add (new AIVehicleLaneData (CurVehicleLaneData.VehicleID, 0f, CurVehicleLaneData.VehicleSpeed));
								NewLane.TotalActiveAIVehicles++;

								// Remove the vehicle from the previous lane
								CurLane.ActiveAIVehicles.RemoveAt (i);
								CurLane.TotalActiveAIVehicles--;

								// Set the initial rotation of the vehicle
								//CurCarObject.transform.rotation = CurLane.Bezier.GetDirection (0f);
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

	private void AlignToRoad(CarData CurCarData, Lane CurLane, AIVehicleLaneData CurVehicleLaneData, Vector3 WantedVehiclePosition, float DeltaTime, bool ForceRaycast = false)
	{
		bool NeedToRaycast = Mathf.Abs(WantedVehiclePosition.y - CurCarData.LastYBezierPos) >= 0.002f ? true : false;

		CurCarData.LastYBezierPos = WantedVehiclePosition.y;

		switch (CurVehicleLaneData.CalculationMode) {
			case 0:
			case 1:
			case 2:
				Quaternion WantedRotation = CurLane.Bezier.GetDirection(CurVehicleLaneData.VehicleProgress);

				// Align the X and Z with the ground normal
				RaycastHit Hit;

				if(ForceRaycast || (NeedToRaycast && CurCarData.TimeSinceLastValidRaycast >= (CurVehicleLaneData.CalculationMode <= 1 ? 1f : 3f) * DeltaTime)){
					if (Physics.Raycast (WantedVehiclePosition + (Vector3.up * 10f), Vector3.down, out Hit, 100f, RoadLayer)) {
						CurCarData.VehicleObj.transform.up = Hit.normal;

						WantedVehiclePosition.y = Hit.point.y;

						CurCarData.TimeSinceLastValidRaycast = 0f;
						CurCarData.RaycastUpVector = Hit.normal;
						CurCarData.RaycastYPoint = Hit.point.y;
					}
				} else {
					CurCarData.TimeSinceLastValidRaycast += DeltaTime;
					CurCarData.VehicleObj.transform.up = CurCarData.RaycastUpVector;
					WantedVehiclePosition.y = CurCarData.RaycastYPoint;
				}

				CurCarData.VehicleObj.transform.Rotate(Vector3.up, Mathf.DeltaAngle(CurCarData.VehicleObj.transform.rotation.eulerAngles.y, WantedRotation.eulerAngles.y), Space.Self);
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

		// Move the vehicle rigidbody to the wanted position
		CurCarData.VehicleRigidbody.MovePosition(WantedVehiclePosition);
	}

	// Returns the time until the calculation mode should be re-checked
	public float UpdateAICalculationMode(AIVehicleLaneData LaneData, Lane CurLane, Vector3 Position)
	{
		float Distance = (CachedPlayerPosition - Position).sqrMagnitude;
		CarData CurCarData = CarObjects[LaneData.VehicleID];

		if (Distance >= 40000f) { // 200f
			if (LaneData.CalculationMode != 3) {
				LaneData.CalculationMode = 3; // Best performance, only move vehicle no rendering or fancy bezier stuff

				// Disable the car rigidbody from detecting collisions
				CurCarData.VehicleRigidbody.isKinematic = true;
				CurCarData.VehicleRigidbody.useGravity = false;
				CurCarData.VehicleRigidbody.detectCollisions = false;
				CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;

				// We move the car underground AFTER disabling collisions, otherwise moving cars underground would cause them all to collider together!
				CurCarData.VehicleObj.transform.position = new Vector3(0f, -10000f, 0f); // Much cheaper than toggling the object or renderer (we ran tests)
			}
				
			return 0.5f; // Half a second should be enough time to switch calculation mode before we get too close
		} else if (Distance >= 22500f) { // 150f
			if(LaneData.CalculationMode != 2){
				LaneData.CalculationMode = 2;

				// Disable the car rigidbody from detecting collisions
				CurCarData.VehicleRigidbody.isKinematic = true;
				CurCarData.VehicleRigidbody.useGravity = false;
				CurCarData.VehicleRigidbody.detectCollisions = false;
				CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;

				AlignToRoad(CurCarData, CurLane, LaneData, Position, 1f, true);
			} else {
				if(CurCarData.IsOnRoad){
					// Always force position in this mode
					AlignToRoad(CurCarData, CurLane, LaneData, Position, 1f, false);
				}
			}

			return (CurCarData.IsOnRoad ? 0.2f : 3f);
		} else if(Distance >= 2500f){ // 50f
			if(LaneData.CalculationMode != 1){
				LaneData.CalculationMode = 1; // No reaction to the player, road alignment is cheaper and bezier calculations are ran less often

				// Disable the car rigidbody from detecting collisions
				CurCarData.VehicleRigidbody.isKinematic = true;
				CurCarData.VehicleRigidbody.useGravity = false;
				CurCarData.VehicleRigidbody.detectCollisions = true;
				CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.None;

				AlignToRoad(CurCarData, CurLane, LaneData, Position, 1f, true);
			}

			return (CurCarData.IsOnRoad ? 0.05f : 3f); // This needs to be updated more often than others as the player is pretty close to this vehicle
		} else {
			if(LaneData.CalculationMode != 0){
				LaneData.CalculationMode = 0; // AI cars do full bezier calculations, react to the player and align to the road correctly

				if(CurCarData.IsOnRoad){
					// Make sure to move the AI onto the road before enabling collisions or they'll explode as they hit anyone else moving
					AlignToRoad(CurCarData, CurLane, LaneData, Position, 1f, true);
				}

				// Allow the car rigidbody to detect collisions
				CurCarData.VehicleRigidbody.isKinematic = true;
				CurCarData.VehicleRigidbody.useGravity = false;
				CurCarData.VehicleRigidbody.detectCollisions = true;
				CurCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
			}

			return 5f; // We don't need to worry about despawning cars at a distance as much as making them visible
		}
	}

	// Converts a lane start transform into Lane data
	public Lane GetLane(JointLaneData LaneData)
	{
		return TrafficData [CachedActiveMap].RoadData [LaneData.RoadID].Lanes [LaneData.LaneID];
	}

	public bool CanJoinLane(Lane NewLane, AIVehicleLaneData CurVehicleLaneData, float CarDistance, bool IsRoadChange = false)
	{
		// Only allow 1 vehicle on an intersection at once (because lanes cross over each other at intersections)
		if (IsRoadChange) {
			// IntersectionID is -1 if the road is not an intersection
			if (TrafficData [CachedActiveMap].RoadData [NewLane.RoadID].IntersectionID >= 0) {
				// Only wait to join the lane if it's marked as WaitForClearIntersection
				if (NewLane.WaitForClearIntersection) {
					// Check for any vehicles on any other lanes with this IntersectionID
					for (int i = 0; i < TrafficData [CachedActiveMap].RoadData [NewLane.RoadID].LaneCount; i++) {
						Lane ComparisonLane = TrafficData [CachedActiveMap].RoadData [NewLane.RoadID].Lanes [i];

						// Ignore the check if we're looking at the same lane
						if (NewLane != ComparisonLane) {
							// If the current comparison lane is marked as DontWaitForThisLane then we can just ignore it
							if (!ComparisonLane.DontWaitForThisLane) {
								if (ComparisonLane.TotalActiveAIVehicles > 0) {
									return false; // We are a black lane waiting for a blue lane to clear to join the road
								}
							}
						}
					}
				} else if (!NewLane.DontWaitForThisLane) {
					// We also need to wait if this is a normal intersection if there's cars in the WaitForClearIntersection lanes incase they get stuck waiting in the intersection
					for (int i = 0; i < TrafficData [CachedActiveMap].RoadData [NewLane.RoadID].LaneCount; i++) {
						Lane ComparisonLane = TrafficData [CachedActiveMap].RoadData [NewLane.RoadID].Lanes [i];

						// If the lane we're checking if a wait for clear intersection lane then we need to make sure it's empty before we can enter the lane
						if (ComparisonLane.WaitForClearIntersection) {
							if (ComparisonLane.TotalActiveAIVehicles > 0) {
								//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name += " [WFBLK]";
								return false; // We are a blue lane and the black lane overlapping our lane has cars in it, wait for them to clear
							}
						}
					}
				}
			} else {
				//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name += " [NINT]";

				// Make sure there's no cars at the start of the road
				return IsVehicleInProgress(NewLane, 0f, 0) == BlockageType.None;
			}
		} else {
			// Note: Below code won't work because it's checking jointlanes at the end of the new road instead of joint lanes connecting TO the new lane
			// Don't allow joining roads which multiple roads join unless all other joint roads are empty
			/*if (NewLane.JointLaneCount > 1) {
			for (int i = 0; i < NewLane.JointLanes.Count; i++) {
				if (GetLane (NewLane.JointLanes [i]).ActiveAIVehicles.Count > 0) {
					return false;
				}
			}
		}*/

			//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name += " [NRC]";

			// Make sure there's atleast CarDistance in front and (CarDistance * 2) behind the vehicle
			return IsVehicleInProgress (NewLane, (IsRoadChange ? 0f : CurVehicleLaneData.VehicleProgress - (CarDistance * 2f)), CurVehicleLaneData.CalculationMode) == BlockageType.None;
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

	// Test for any vehicles within a certain range of progress in the selected lane
	public BlockageType IsVehicleInProgress(Lane SelectedLane, float MinProgress, int CalculationMode = 0, float DistanceMultiplier = 1f)
	{
		float VehicleDistance = (DistanceBetweenVehicles / SelectedLane.Bezier.GetLength()) * DistanceMultiplier;

		MinProgress += 0.001f; // Stops the MinProgress check detecting itself
		float MaxProgress = MinProgress + VehicleDistance;

		//Debug.Log ("Testing min " + MinProgress + ", Max: " + MaxProgress);

		// Test for any other vehicles blocking progress
		for (int i = 0; i < SelectedLane.TotalActiveAIVehicles; i++) {
			AIVehicleLaneData CurVehicleLaneData = SelectedLane.ActiveAIVehicles [i];

			// Check if there's a vehicle on the section of the road lane we just queried
			if(CarObjects[CurVehicleLaneData.VehicleID].IsOnRoad){
				if (CurVehicleLaneData.VehicleProgress >= MinProgress && CurVehicleLaneData.VehicleProgress <= MaxProgress) {

					// If the car in front of this block is being blocked by the player then mark this blockage type as player too (so everyone beeps their horns)
					if (CurVehicleLaneData.LastBlockageReason == BlockageType.Player) {
						//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name += "[PLYPROG1]";

						return BlockageType.Player;
					} else {
						//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name += "[AIPROG1]";

						return BlockageType.AI;
					}
				}
			}
		}

		// NOTE: This may be broken
		// Also check if the player is blocking in any joint lanes if MaxProgress is 1f or greater
		if (MinProgress >= 1f - VehicleDistance) {
			for (int JointLaneID = 0; JointLaneID < SelectedLane.JointLaneCount; JointLaneID++) {
				Lane CurJointLane = GetLane (SelectedLane.JointLanes [JointLaneID]);
				float CurLaneVehicleDistance = (DistanceBetweenVehicles / CurJointLane.Bezier.GetLength ());

				// From here we're doing the same as the above for loop
				for (int i = 0; i < CurJointLane.TotalActiveAIVehicles; i++) {
					AIVehicleLaneData CurVehicleLaneData = CurJointLane.ActiveAIVehicles [i];

					// Check if there's a vehicle on the section of the road lane we just queied
					if (CurVehicleLaneData.VehicleProgress <= (CurLaneVehicleDistance / 2f)) {
						// If the car in front of this block is being blocked by the player then mark this blockage type as player too (so everyone beeps their horns)
						if (CurVehicleLaneData.LastBlockageReason == BlockageType.Player) {
							//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name += "[PLYPROG2]";

							return BlockageType.Player;
						} else {
							//CarObjects [CurVehicleLaneData.VehicleID].VehicleObj.name += "[AIPROG2]";

							return BlockageType.AI;
						}
					}
				}
			}
		}

		// Do a check to see if the player is blocking the AI but do it step by step so all the heavy functions aren't being ran when not needed

		// Test for the player blocking progress
		float LaneWidth = 2f; // TODO: Move this variable outside the function

		if (CalculationMode == 0) {
			// Check if the player is in front of the AI
			Vector2 FlatPlayerPosition = GetVector2NoY (CachedPlayerPosition);

			Vector2 StartCenter = GetVector2NoY (SelectedLane.Bezier.GetPosition (MinProgress));
			Quaternion StartDirection = SelectedLane.Bezier.GetDirection (MinProgress);
			Vector2 StartForwardDirection = GetVector2NoY (StartDirection * Vector3.forward);

			bool IsWithinStartEdge = (Vector2.Dot (StartForwardDirection, (FlatPlayerPosition - StartCenter).normalized) >= 0);

			Vector2 EndCenter = Vector2.zero;
			Quaternion EndDirection = Quaternion.identity;

			if (IsWithinStartEdge) {
				// Check if the player is in front of the AI vehicle
				EndCenter = GetVector2NoY (SelectedLane.Bezier.GetPosition (MaxProgress));
				EndDirection = SelectedLane.Bezier.GetDirection (MaxProgress);
				Vector2 EndForwardDirection = GetVector2NoY (EndDirection * Vector3.forward);

				bool IsWithinEndEdge = (Vector2.Dot (-EndForwardDirection, (FlatPlayerPosition - EndCenter).normalized) >= 0);

				if (IsWithinEndEdge) {
					if (IsWithinLeftRight (StartDirection, EndDirection, LaneWidth, FlatPlayerPosition, StartCenter, EndCenter)) {
						return BlockageType.Player;
					}
				}
			} 

			// THIS IS CURRENTLY BROKEN!!
			/*else if (VehicleManager.Instance.IsActiveVehicleSirenOn () && (MaxProgress - MinProgress) - MinProgress >= 0f) {
				// Check if the player is behind the AI with their siren on



				float BackwardsMaxProgress = (MaxProgress - MinProgress) - MinProgress;

				EndCenter = GetVector2NoY (SelectedLane.Bezier.GetPosition (BackwardsMaxProgress));
				EndDirection = SelectedLane.Bezier.GetDirection(BackwardsMaxProgress);
				Vector2 EndBackwardsDirection = GetVector2NoY(EndDirection * Vector3.back);

				bool IsWithinEndEdge = (Vector2.Dot(-EndBackwardsDirection, (FlatPlayerPosition - EndCenter).normalized) >= 0);

				if(IsWithinEndEdge){
					if (IsWithinLeftRight (StartDirection, EndDirection, LaneWidth, FlatPlayerPosition, StartCenter, EndCenter)) {
						return BlockageType.PlayerSiren;

					}
				}
			}*/
		}

		return BlockageType.None;
	}

	private bool IsWithinLeftRight(Quaternion StartDirection, Quaternion EndDirection, float LaneWidth, Vector2 FlatPlayerPosition, Vector2 StartCenter, Vector2 EndCenter)
	{
		Vector2 StartRightDirection = GetVector2NoY (StartDirection * Vector3.right);
		Vector2 EndRightDirection = GetVector2NoY (EndDirection * Vector3.right);

		Vector2 StartLeft = StartCenter - (StartRightDirection * LaneWidth);
		Vector2 StartRight = StartCenter + (StartRightDirection * LaneWidth);

		Vector2 EndLeft = EndCenter - (EndRightDirection * LaneWidth);
		Vector2 EndRight = EndCenter + (EndRightDirection * LaneWidth);

		bool IsWithinLeftEdgeStart = (Vector2.Dot (StartRightDirection, (FlatPlayerPosition - StartLeft).normalized) >= 0);
		bool IsWithinRightEdgeStart = (Vector2.Dot (-StartRightDirection, (FlatPlayerPosition - StartRight).normalized) >= 0);

		bool IsWithinLeftEdgeEnd = (Vector2.Dot (EndRightDirection, (FlatPlayerPosition - EndLeft).normalized) >= 0);
		bool IsWithinRightEdgeEnd = (Vector2.Dot (-EndRightDirection, (FlatPlayerPosition - EndRight).normalized) >= 0);

		return (((IsWithinLeftEdgeStart && IsWithinRightEdgeStart) || (IsWithinLeftEdgeEnd && IsWithinRightEdgeEnd)));
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
