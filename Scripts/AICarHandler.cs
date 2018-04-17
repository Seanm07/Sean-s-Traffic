using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[System.Serializable]
public class AICarVehicleData { 
	public NavMeshPath Path { get; set; }
	public List<Vector3> GeneratedPath = new List<Vector3>();
	public int CurPathId;
	public int CornerId;
	public bool HasReachedDestination;
	public bool HasPathGenerated = false;

	public float DistanceToReachWaypoint = 15f;
}

[System.Serializable]
public class AICarVehicleConfig {
	public List<Transform> WheelTransforms = new List<Transform>();

	public List<Vector3> FrontWheelsInitalRotations = new List<Vector3>();
	public List<Vector3> RearWheelsInitialRotations = new List<Vector3>();

	public List<WheelCollider> FrontWheels = new List<WheelCollider>();
	public List<WheelCollider> RearWheels = new List<WheelCollider>();

	public Vector3 CenterOfMass;

	public float ForwardStiffness;
	public float SidewaysStiffness;
	public float RigidbodyDrag = 0.05f;

	public float DamageTaken = 0f;

	[Header("Control Related Config")]
	public float MaxSteer = 20f;
	public float MaxSpeed = 80f;

	public float SpeedPercentLimitSteer = 0.7f;
	public float MaxSteerLimit = 0.9f;

	public float Slip = 1f;
	public float Drag = 1f;

	public float ForceAmount = 5500f;
}

[System.Serializable]
public class AICarInputs {
	public float InputAcceleration;
	public float InputBrake;
	public float InputHorizontal;
	public float InputVertical;
	public float InputNitro;

	public bool IsHandbrakeActive;

	public float Speed;
	public float SpeedPercent;

	public float WantedRPM;
	public float RPMPercent;

	public float RaycastHorizontalAdjustment;
	public float RaycastSpeedAdjustment;

	public bool WantToReverse = false;
	public float WantToReverseTimer = 0f;
}

public class AICarHandler : MonoBehaviour {

	public Rigidbody SelfRigidbody { get; set; }
	public VehicleTrafficData SelfVehicleData { get; set; }

	private bool IsHazardsActive = false;
	private Transform HazardsActiveTransform;
	private Transform HazardsDisabledTrasform;

	private bool HazardsCurrentlyActive = false;
	private Vector3 HazardsActivePos;
	private Vector3 HazardsDisabledPos = new Vector3(0f, -10000f, 0f);
	private float TimeSinceHazardsChanged;

	// Store the last velocities so the rigidbodies can move around as low costing kinematic rigidbodies and be given physics on collision
	private Vector3 LastVelocity;
	private Vector3 LastAngularVelocity;

	// Store the time since last impact so we can limit how often sparks are created and crash sounds are played
	private float TimeSinceLastImpact;

	public AICarVehicleData PathData = new AICarVehicleData();
	public AICarVehicleConfig Config = new AICarVehicleConfig();
	public AICarInputs Inputs = new AICarInputs();

	private List<float> RaycastHitDistance = new List<float>();
	private int CurRaycastId = 0;

	private bool CacheIsWheelColliderActive = true;

	public float VehicleHealth = 1f;
	public bool IsDestroyed = false;

	public AudioSource BurningAudioSource;

	private ParticleSystem SmokeParticles;
	private ParticleSystem.EmissionModule SmokeEmission;
	private ParticleSystem.MainModule SmokeMain;

	private ParticleSystem FireParticles;
	private ParticleSystem.EmissionModule FireEmission;

	private GameObject Explosion;

	public Renderer VehicleBodyRenderer;
	public List<Material> VehicleBodyMaterials { get; set; }

	public void Setup(Rigidbody NewRigidbody, VehicleTrafficData NewVehicleData, Transform NewHazardsOffTransform, Transform NewHazardsOnTransform)
	{
		SelfRigidbody = NewRigidbody;
		SelfVehicleData = NewVehicleData;

		HazardsActiveTransform = NewHazardsOnTransform;
		HazardsDisabledTrasform = NewHazardsOffTransform;

		HazardsActivePos = NewHazardsOffTransform.localPosition;

		TimeSinceLastImpact = 0f;

		Config.FrontWheelsInitalRotations.Clear();

		for(int i=0;i < Config.FrontWheels.Count;i++)
			Config.FrontWheelsInitalRotations.Add(Config.FrontWheels[i].transform.localEulerAngles);

		Config.RearWheelsInitialRotations.Clear();

		for(int i=0;i < Config.RearWheels.Count;i++)
			Config.RearWheelsInitialRotations.Add(Config.RearWheels[i].transform.localEulerAngles);

		SelfRigidbody.centerOfMass += Config.CenterOfMass;

		if(VehicleBodyRenderer != null){
			VehicleBodyMaterials = new List<Material>();

			for(int i=0;i < VehicleBodyRenderer.materials.Length;i++)
				VehicleBodyMaterials.Add(VehicleBodyRenderer.materials[i]);
		}

		UpdateVehicleStatus();
	}

	public void ResetVehicle()
	{
		if(IsDestroyed){
			// Restore the vehicle materials from the burnt material
			if(VehicleBodyRenderer != null && VehicleBodyMaterials != null && VehicleBodyMaterials.Count > 0)
				VehicleBodyRenderer.materials = VehicleBodyMaterials.ToArray();
		}

		VehicleHealth = 1f;
		IsDestroyed = false;
		SetHazardsActive(false);

		// Destroy rather than disable because the chances are low that this car will be damaged again
		// So just cleanup the clear the memory
		if(SmokeParticles){
			Destroy(SmokeParticles.gameObject);
			SmokeEmission = default(ParticleSystem.EmissionModule);
			SmokeMain = default(ParticleSystem.MainModule);
		}

		if(FireParticles){
			Destroy(FireParticles.gameObject);
			FireEmission = default(ParticleSystem.EmissionModule);
		}

		if(Explosion) Destroy(Explosion);
	}

	public void DamageVehicle(float DamageAmount)
	{
		VehicleHealth -= DamageAmount / 100f;

		UpdateVehicleStatus();
	}

	public void UpdateVehicleStatus()
	{
		if(IsDestroyed) return;

		// Updates the smoke effect (changing colour and strength of the smoke)
		if(TrafficLaneManager.Instance.SmokeParticlesTemplate != null && VehicleHealth <= 0.75f){
			if(SmokeParticles == null){
				SmokeParticles = GameObject.Instantiate(TrafficLaneManager.Instance.SmokeParticlesTemplate, transform).GetComponent<ParticleSystem>();
				SmokeParticles.transform.localPosition = new Vector3(0f, 0f, 2.75f);
				SmokeParticles.transform.localEulerAngles = new Vector3(0f, 180f, 0f);
				SmokeParticles.transform.localScale = Vector3.one;

				// Set the emission module for the smoke particles
				SmokeEmission = SmokeParticles.emission;
				SmokeMain = SmokeParticles.main;
			}

			ParticleSystem.MinMaxCurve NewCurve = new ParticleSystem.MinMaxCurve();
			NewCurve.constant = (1f - VehicleHealth) * 2f;

			ParticleSystem.EmissionModule NewEmissionModule = SmokeEmission;
			NewEmissionModule.rateOverTime = NewCurve;

			ParticleSystem.MainModule NewMainModule = SmokeMain;
			ParticleSystem.MinMaxGradient NewGradient = new ParticleSystem.MinMaxGradient();
			NewGradient.color = Color.Lerp(Color.white, Color.gray, 1f - VehicleHealth);

			NewMainModule.startColor = NewGradient;
		}

		if(TrafficLaneManager.Instance.FireParticlesTemplate != null && VehicleHealth <= 0f){
			if(FireParticles == null){
				FireParticles = GameObject.Instantiate(TrafficLaneManager.Instance.FireParticlesTemplate, transform).GetComponent<ParticleSystem>();
				FireParticles.transform.localPosition = new Vector3(0f, 1.5f, 2.75f);
				FireParticles.transform.localEulerAngles = new Vector3(-90f, 180f, 0f);
				FireParticles.transform.localScale = Vector3.one;

				// Set the emission module for the fire particles
				FireEmission = FireParticles.emission;
			}

			if(BurningAudioSource != null){
				if(!BurningAudioSource.isPlaying)
					BurningAudioSource.Play();

				BurningAudioSource.volume = 1f - VehicleHealth;
			}

			ParticleSystem.MinMaxCurve NewCurve = new ParticleSystem.MinMaxCurve();
			NewCurve.constant = (1f - VehicleHealth) * 5f;

			ParticleSystem.EmissionModule NewEmissionModule = FireEmission;
			NewEmissionModule.rateOverTime = NewCurve;
		}

		if(TrafficLaneManager.Instance.ExplosionParticlesTemplate != null && VehicleHealth <= -0.5f){
			if(Explosion == null){
				Explosion = GameObject.Instantiate(TrafficLaneManager.Instance.ExplosionParticlesTemplate, transform);
				Explosion.transform.localPosition = Vector3.zero;
				Explosion.transform.localEulerAngles = Vector3.zero;
				Explosion.transform.localScale = Vector3.one;
			}

			OnVehicleDestroyed();
		}
	}

	private void OnVehicleDestroyed()
	{
		// Suggestion: Call a script to spawn an explosion at transform.position

		IsDestroyed = true;

		// Replace the body materials with the burnt material
		if(VehicleBodyRenderer != null && VehicleBodyMaterials.Count > 0){
			Material[] BurntMaterials = new Material[VehicleBodyRenderer.materials.Length];

			for(int i=0; i < VehicleBodyRenderer.materials.Length;i++)
				BurntMaterials[i] = TrafficLaneManager.Instance.BurntMaterial;

			// Had to set the burnt materials like this because the separate materials in renderer.materials seem to be read only unless you set the whole array
			VehicleBodyRenderer.materials = BurntMaterials;
		}
	}

	// Called from TrafficLaneManager
	public void TrafficAIUpdate()
	{
		if(IsDestroyed){
			Inputs.InputAcceleration = 0f;
			Inputs.InputBrake = 1f;
			Inputs.InputHorizontal = 0f;
			Inputs.InputVertical = 0f;
			Inputs.InputNitro = 0f;
			Inputs.IsHandbrakeActive = true;
			Inputs.WantedRPM = 0f;
			Inputs.Speed = 0f;
			Inputs.SpeedPercent = 0f;
			return;
		}

		TimeSinceLastImpact += Time.deltaTime;

		// Update the hazard lights so they can flash on/off when active
		UpdateHazards();

		if(VehicleHealth <= 0f){
			VehicleHealth -= Time.deltaTime / 20f;
			UpdateVehicleStatus();
		}

		switch(TrafficLaneManager.Instance.TrafficActionWhenHit)
		{
			case TrafficLaneManager.ActionWhenHit.NothingIgnoreCollisions:
				
				break;

			case TrafficLaneManager.ActionWhenHit.StopBecomePhysical:
				
				break;

			case TrafficLaneManager.ActionWhenHit.SmartAIDynamicallyRejoin:
				if(!SelfVehicleData.IsOnRoad){
					UpdateAIInputs();
					UpdateVehicle();
					UpdateRaycasts();
				}
				break;
		}
	}

	public void SetWheelCollidersActive(bool WantActive)
	{
		if(CacheIsWheelColliderActive != WantActive){
			CacheIsWheelColliderActive = WantActive;

			foreach(WheelCollider FWC in Config.FrontWheels)
				FWC.enabled = WantActive;

			foreach(WheelCollider RWC in Config.RearWheels)
				RWC.enabled = WantActive;
		}
	}

	private void UpdateDestination()
	{
		PathData.CurPathId++;
		PathData.Path = null;
	}

	public float FixDegrees(float Input)
	{
		float Output = Input;
		while(Output < 0f) Output += 360f;
		while(Output > 360f) Output -= 360f;
		while(Output > 180f) Output -= 360f;

		if(Output < 0f){
			Output += 180f;
		} else {
			Output -= 180f;
		}

		return Output;
	}

	private void AIInputCalculation(Vector3 SourcePos, Vector3 TargetPos, out float Distance)
	{
		// Using Vector2 for the directional calculations because we don't care about height, math with Vector2 is faster than Vector3 anyway
		Vector2 DirectionalAngle = (new Vector2(SourcePos.x, SourcePos.z) - new Vector2(TargetPos.x, TargetPos.z));

		Debug.DrawLine(SourcePos, TargetPos, Color.green, 1f);

		float Direction = (Mathf.Atan2(DirectionalAngle.y, DirectionalAngle.x) * Mathf.Rad2Deg) + (transform.localEulerAngles.y - 90f);
		Direction = -FixDegrees(Direction);

		// Store the distance from the destination (using vector3 here so roads going over roads won't be seen as reaching the destination)
		Distance = Vector3.Distance(SourcePos, TargetPos);

		Inputs.RaycastSpeedAdjustment = 1f;
		float CurRaycastHorizontalAdjustment = 0f;
		bool IsFrontBlocked = false;

		// Take the raycast data we have into consideration for the input
		for(int i=0;i < RaycastHitDistance.Count;i++)
		{
			if(RaycastHitDistance[i] >= 0f){
				// This raycast is for angle ((AngleInc * i) - 90)
				// 0 is full left, RaycastHitDistance.Count is full right

				// Convert the angle to a value between -1 and 1
				// 180f = 1, 0f = -1, 90f = 0
				float UsableAngle = -((((AngleInc * i) / VisionAngle) * 2f) - 1f); // The - inverses this (so left is 1f and right is -1f)

				if(!Mathf.Approximately(UsableAngle, 0f)){
					// Center rays need a stronger turn than edge ones because they're directly in front of the AI and need to be avoided
					// So inverse the UsableAngle but keep it negative/positive
					// However edge rays are still important so make them die down to 0.5f instead of 0f
					// e.g 0.1 becomes 1.2 or -0.4 becomes -0.7 or 1 becomes 0.2
					// Then we clamp it between 0 and 1 or -1 (depending on whether it's negative or positive)
					float ClampValue = UsableAngle < 0f ? -1f : 1f;

					UsableAngle = ((UsableAngle < 0f ? -1f : 1f) - UsableAngle) + (UsableAngle < 0f ? -0.1f : 0.1f);

					if(ClampValue == -1f){
						UsableAngle = Mathf.Clamp(UsableAngle, ClampValue, 0f);
					} else {
						UsableAngle = Mathf.Clamp(UsableAngle, 0f, ClampValue);
					}

					float FinalAdjustment = UsableAngle * (1f - (RaycastHitDistance[i] / 15f));

					if(ClampValue == -1f){
						FinalAdjustment = Mathf.Clamp(UsableAngle, ClampValue, 0f);
					} else {
						FinalAdjustment = Mathf.Clamp(UsableAngle, 0f, ClampValue);
					}

					CurRaycastHorizontalAdjustment += FinalAdjustment;
				} else {
					// For the forwward ray just use the direction we're already turning and turn in that direction a bit more
					UsableAngle += Inputs.InputHorizontal > 0f ? 0.25f : -0.25f; // If InputHorizontal is exactly 0 then left is favoured

					Inputs.RaycastSpeedAdjustment = Mathf.Clamp((RaycastHitDistance[i] / 15f) - 0.5f, -1f, 1f);

					if(Inputs.RaycastSpeedAdjustment <= 0f){
						Inputs.RaycastSpeedAdjustment += 0.25f;

						if(Inputs.RaycastSpeedAdjustment >= 0f)
							Inputs.RaycastSpeedAdjustment = 0f;
					}

					IsFrontBlocked = (RaycastHitDistance[i] <= 10f);

					CurRaycastHorizontalAdjustment += UsableAngle * ((1f - (RaycastHitDistance[i] / 15f) / 2f));
				}
			}
		}

		// If RaycastSpeedAdjustment is negative for x seconds then we want to reverse
		if(Inputs.RaycastSpeedAdjustment < 0f || IsFrontBlocked || (Inputs.InputAcceleration >= 0.1f && Mathf.Abs(Inputs.Speed) < 5f)){
			Inputs.WantToReverseTimer += Time.deltaTime;

			if(Inputs.WantToReverseTimer >= 2f){
				Inputs.WantToReverse = true;
				Inputs.WantToReverseTimer = 5f;
			}
		} else if(Inputs.WantToReverse){
			Inputs.WantToReverseTimer -= Time.deltaTime;

			if(Inputs.WantToReverseTimer < 0f){
				Inputs.WantToReverse = false;
				Inputs.WantToReverseTimer = -8f; // Sets an 8 second cooldwon for reversing
			}
		} else {
			Inputs.WantToReverseTimer = Mathf.MoveTowards(Inputs.WantToReverseTimer, 0f, Time.deltaTime);
		}

		CurRaycastHorizontalAdjustment = Mathf.Clamp(CurRaycastHorizontalAdjustment, -1f, 1f);
		Inputs.RaycastHorizontalAdjustment = CurRaycastHorizontalAdjustment;

		float WantedHorizontal = Mathf.Clamp((Direction / Config.MaxSteer) + (Inputs.WantToReverse ? 0f : (Inputs.RaycastHorizontalAdjustment * 1f)), -1f, 1f);

		Inputs.InputHorizontal = WantedHorizontal;

		if(!Inputs.WantToReverse){
			Inputs.RaycastSpeedAdjustment = Mathf.Clamp(Inputs.RaycastSpeedAdjustment, 0.1f, 1f);
			Inputs.InputAcceleration = Mathf.Clamp(1f - Mathf.Abs(Inputs.InputHorizontal * (Inputs.Speed / Config.MaxSpeed) * 2f), -1f, Inputs.Speed > Config.MaxSpeed ? 0f : 1f) * Inputs.RaycastSpeedAdjustment; 
		} else {
			Inputs.InputAcceleration = -0.2f;
			Inputs.InputHorizontal = -Inputs.InputHorizontal;
		}
	}

	private void UpdateAIInputs()
	{
		if(!PathData.HasPathGenerated)
			return;

		if(PathData.HasReachedDestination){
			//TrafficLaneManager.Instance.RemoveMonitoredTransform(transform);

			//SelfRigidbody.interpolation = RigidbodyInterpolation.None;
			SelfRigidbody.useGravity = true;
			SelfRigidbody.isKinematic = true;

			SelfVehicleData.IsOnRoad = true;
			SetHazardsActive(false);

			SetWheelCollidersActive(false);

			PathData.HasPathGenerated = false;

			TimeSinceLastImpact = 0.1f;
			return;
		}

		// If the vehicle is moving towards the final point then they'll be moved directly to the point
		bool WantDirectlyToPoint = (PathData.CurPathId == (PathData.GeneratedPath.Count - 1));

		if(PathData.Path != null){
			switch(PathData.Path.status)
			{
				case NavMeshPathStatus.PathComplete:
					float Distance = 0f;

					// Calculate the next point to drive towards within the path
					AIInputCalculation(SelfRigidbody.position, PathData.Path.corners[PathData.CornerId], out Distance);

					if(WantDirectlyToPoint){
						// Disabled as the pathing is now async so it's always delayed
						// The AI car will be dynamic until it's far enough away from the player to be warped back into a normal car by the traffic script
						/*if(Distance < 0.25f){
							Debug.Log("DIRECT Corner point reached!");
							PathData.CornerId++;

							if(PathData.CornerId > (PathData.Path.corners.Length - 1)){
								Debug.Log("End point reached!");
								PathData.HasReachedDestination = true;
							}
						}*/
					} else {
						if(Distance <= PathData.DistanceToReachWaypoint){
							//Debug.Log("NOT DIRECT Corner point reached!");
							PathData.CornerId++;

							if(PathData.CornerId > (PathData.Path.corners.Length - 1)){
								//Debug.Log("Moving to next destination point!");
								UpdateDestination();
							}
						}
					}
					break;

				case NavMeshPathStatus.PathPartial:
					// The destination cannot be reached with the navmesh \o/
					//Debug.LogError("Cannot complete navmesh path! It's not possible to reach destination!");
					break;
			}
		} else {
			// This generates the path to the next PathData.GeneratedPath[PathData.CurPathId] position
			PathData.Path = new NavMeshPath();
			NavMeshHit HitSelf = new NavMeshHit();
			NavMeshHit HitDest = new NavMeshHit();

			// Get closest positon on the navmesh
			if(!NavMesh.SamplePosition(SelfRigidbody.position, out HitSelf, 150f, NavMesh.AllAreas)){
				//Debug.Log("Something went wrong, failed to get closest position on navmesh!");
				PathData.HasReachedDestination = true;
				return;
			}

			if(HitSelf.position.x == Mathf.Infinity){
				PathData.HasReachedDestination = true;
				return;
			}

			if(!NavMesh.SamplePosition(PathData.GeneratedPath[PathData.CurPathId], out HitDest, 150f, NavMesh.AllAreas)){
				//Debug.Log("Something went wrong, failed to sampleposition on navmesh");
				PathData.HasReachedDestination = true;
				return;
			}

			if(HitDest.position.x == Mathf.Infinity){
				PathData.HasReachedDestination = true;
				return;
			}

			if(!NavMesh.CalculatePath(HitSelf.position, HitDest.position, NavMesh.AllAreas, PathData.Path)){
				//Debug.Log("Something went wrong, failed to calculate new path");
				PathData.HasReachedDestination = true;
				return;
			}

			PathData.CornerId = 1; // Set the corner id to 1, because 0 is the position we're currently in
		}
	}

	private void UpdateVehicle()
	{
		if(SelfRigidbody.constraints == RigidbodyConstraints.FreezeRotationZ)
			transform.localEulerAngles = new Vector3(transform.localEulerAngles.x, transform.localEulerAngles.y, 0f);

		Inputs.Speed = Mathf.MoveTowards(Inputs.Speed, transform.InverseTransformDirection(SelfRigidbody.velocity).z * 2.23694f, 50f * Time.deltaTime); // Convert the speed from m/s to mph

		// Make all the wheels roll
		foreach(Transform WheelTransform in Config.WheelTransforms)
			WheelTransform.Rotate(Vector3.right * Inputs.Speed * 70f * Time.deltaTime);

		// Front left wheel turning
		Vector3 WantedLeftWheelRotation = new Vector3(Config.WheelTransforms[0].localEulerAngles.x, Config.FrontWheelsInitalRotations[0].y + (Inputs.InputHorizontal * Config.MaxSteer), 0f);
		Config.WheelTransforms[0].localRotation = Quaternion.Lerp(Config.WheelTransforms[0].localRotation, Quaternion.Euler(WantedLeftWheelRotation), Time.deltaTime * 5f);

		// Front right wheel turning (only if the wheel has atleast 3 wheels, otherwise it's a motorbike)
		if(Config.WheelTransforms.Count >= 3){
			Vector3 WantedRightWheelRotation = new Vector3(Config.WheelTransforms[1].localEulerAngles.x, Config.FrontWheelsInitalRotations[1].y + (Inputs.InputHorizontal * Config.MaxSteer), 0f);
			Config.WheelTransforms[1].localRotation = Quaternion.Lerp(Config.WheelTransforms[1].localRotation, Quaternion.Euler(WantedRightWheelRotation), Time.deltaTime * 5f);
		}

		// Braking above 30 speed acts as a handbrake (we do this to keep controls as simple as possible)
		Inputs.IsHandbrakeActive = (Inputs.Speed > 30f && Inputs.InputAcceleration == -1f);
		Inputs.InputBrake = (Inputs.IsHandbrakeActive ? 1f : 0f);

		if(Inputs.InputAcceleration != 0f && ((Inputs.InputAcceleration > 0f && Inputs.WantedRPM >= 0f) || (Inputs.InputAcceleration < 0f && Inputs.WantedRPM <= 0f))){
			Inputs.WantedRPM = (Config.ForceAmount * Inputs.InputAcceleration) * 0.1f + Inputs.WantedRPM * 0.9f;
		} else {
			Inputs.WantedRPM = 0f;
		}

		if(((Inputs.Speed > 1f && Inputs.InputAcceleration < 0f) || (Inputs.Speed < -1f && Inputs.InputAcceleration > 0f))){
			Inputs.InputBrake = 1f;
			Config.Slip = 0.35f;
		} else {
			// Config.Slip = 1f - Mathf.Abs(InputHorizontal / 2f)
		}

		Inputs.RPMPercent = Mathf.Clamp01(Inputs.WantedRPM / Config.ForceAmount);
		Inputs.SpeedPercent = Mathf.Clamp01(Inputs.Speed / Config.MaxSpeed);

		SelfRigidbody.drag = Config.RigidbodyDrag * Config.Drag;

		Config.Drag = Mathf.Lerp(Config.Drag, 1f, Time.deltaTime);

		// Only use antiroll if this is a car
		if(Config.FrontWheels.Count > 1 && Config.RearWheels.Count > 1)
			CalculateAntiRoll();
	}

	private float VisionAngle = 80f;
	private float AngleInc = 20f;

	private void UpdateRaycasts()
	{
		int TotalRaycasts = Mathf.CeilToInt(VisionAngle / AngleInc);
		Vector3 SourcePos = transform.position; // Rays will be cast from this position

		if(RaycastHitDistance.Count <= 0)
			for(int i=0;i < TotalRaycasts;i++)
				RaycastHitDistance.Add(-1f);

		Vector3 DestPos = (SourcePos + (Quaternion.AngleAxis((AngleInc * CurRaycastId) - (VisionAngle / 2f), Vector3.up) * transform.forward));
		Vector3 DestRot = (DestPos - SourcePos).normalized;

		RaycastHit Hit = new RaycastHit();

		if(Physics.SphereCast(SourcePos, 1f, DestRot, out Hit, 15f, TrafficLaneManager.Instance.ObstacleLayer)){
			RaycastHitDistance[CurRaycastId] = Hit.distance;
		} else {
			RaycastHitDistance[CurRaycastId] = -1; // No registered hit
		}

		CurRaycastId = (CurRaycastId + 1 >= TotalRaycasts ? 0 : CurRaycastId + 1);
	}

	// Called from TrafficLaneManager
	public void TrafficAIFixedUpdate()
	{
		switch(TrafficLaneManager.Instance.TrafficActionWhenHit)
		{
			case TrafficLaneManager.ActionWhenHit.NothingIgnoreCollisions:

				break;

			case TrafficLaneManager.ActionWhenHit.StopBecomePhysical:

				break;

			case TrafficLaneManager.ActionWhenHit.SmartAIDynamicallyRejoin:
				LastVelocity = SelfRigidbody.velocity;
				LastAngularVelocity = SelfRigidbody.angularVelocity;

				UpdateFrontWheels();
				UpdateRearWheels();
				break;
		}
	}

	private void UpdateFrontWheels()
	{
		foreach(WheelCollider FrontWheel in Config.FrontWheels)
		{
			FrontWheel.motorTorque = Inputs.WantedRPM / 2f;

			if(!Inputs.IsHandbrakeActive){
				FrontWheel.brakeTorque = Inputs.InputBrake * Config.ForceAmount;
			} else {
				FrontWheel.brakeTorque = 0f;
			}

			FrontWheel.steerAngle = Mathf.MoveTowardsAngle(FrontWheel.steerAngle, (Inputs.InputHorizontal * Config.MaxSteer) * Mathf.Clamp((Inputs.SpeedPercent - Config.SpeedPercentLimitSteer) * (1f / (1f - Config.SpeedPercentLimitSteer)) - 1f, Config.MaxSteerLimit, 1f), 50f * Time.deltaTime);

			// Adjust the forward skid of the tyres
			WheelFrictionCurve FC = FrontWheel.forwardFriction;
			FC.stiffness = (Config.ForwardStiffness / Config.Slip);
			FrontWheel.forwardFriction = FC;

			// Adjust the sideways skid of the tyres
			FC = FrontWheel.sidewaysFriction;
			FC.stiffness = (Config.SidewaysStiffness / Config.Slip);
			FrontWheel.sidewaysFriction = FC;
		}
	}

	private void UpdateRearWheels()
	{
		foreach(WheelCollider RearWheel in Config.RearWheels)
		{
			RearWheel.motorTorque = Inputs.WantedRPM;

			Inputs.WantedRPM = Inputs.WantedRPM * (1f - Inputs.InputBrake);

			if(!Inputs.IsHandbrakeActive){
				RearWheel.brakeTorque = Inputs.InputBrake * Config.ForceAmount;

				if(Inputs.Speed > 1f){
					Config.Slip = Mathf.Lerp(Config.Slip, 1f, 0.002f);
				} else {
					Config.Slip = Mathf.Lerp(Config.Slip, 1f, 0.02f);
				}
			} else {
				RearWheel.brakeTorque = Inputs.InputBrake * Config.ForceAmount;

				Config.Slip = Mathf.Lerp(Config.Slip, 3f, Inputs.InputAcceleration * 0.9f);
			}

			// Adjust the forward skid of the tyres
			WheelFrictionCurve FC = RearWheel.forwardFriction;
			FC.stiffness = (Config.ForwardStiffness / Config.Slip);
			RearWheel.forwardFriction = FC;

			// Adjust the sideways skid of the tyres
			FC = RearWheel.sidewaysFriction;
			FC.stiffness = (Config.SidewaysStiffness / Config.Slip);
			RearWheel.sidewaysFriction = FC;
		}
	}

	public IEnumerator InitialRejoinPathDelay()
	{
		yield return new WaitForSeconds(2f);

		GenerateRejoinPath();
	}

	public void GenerateRejoinPath()
	{
		StartCoroutine(GeneratePath(SelfVehicleData.LastTargetPosition));
	}

	// Generate a path to drive to the destination with
	public IEnumerator GeneratePath(Vector3 Destination)
	{
		// Clear the previous stored path
		//PathData.GeneratedPath.Clear();

		// Get the starting points of the path
		NavMeshHit StartPointHitNav = new NavMeshHit();
		RaycastHit StartPointHitRay = new RaycastHit();

		// Raycast the ground so height doesn't affect the SamplePosition check
		if(!Physics.Raycast(transform.position + (Vector3.up * 5f), Vector3.down, out StartPointHitRay, 100f)) yield break;
		if(!NavMesh.SamplePosition(StartPointHitRay.point, out StartPointHitNav, 100f, NavMesh.AllAreas)) yield break;

		yield return null; // Wait a frame

		// Get the ending point of the path
		NavMeshHit EndPointHitNav = new NavMeshHit();
		RaycastHit EndPointHitRay = new RaycastHit();

		// Raycast the ground so height doesn't affect the SamplePosition check
		if(!Physics.Raycast(Destination + (Vector3.up * 5f), Vector3.down, out EndPointHitRay, 100f)) yield break;
		if(!NavMesh.SamplePosition(EndPointHitRay.point, out EndPointHitNav, 100f, NavMesh.AllAreas)) yield break;

		yield return null; // Wait a frame

		NavMeshPath TemporaryPath = new NavMeshPath();

		// We now have a start and end point
		if(!NavMesh.CalculatePath(StartPointHitNav.position, EndPointHitNav.position, NavMesh.AllAreas, TemporaryPath)) yield break;

		yield return null; // Wait a frame

		// We need to cut the corners up a bit more so the car drives towards more specific points instead of driving to path in the distance
		List<Vector3> FinalPaths = new List<Vector3>();
		Vector3 LastPos = TemporaryPath.corners[0];

		for(int i=1;i < TemporaryPath.corners.Length;i++)
		{
			float NextPointDistance = Vector3.Distance(LastPos, TemporaryPath.corners[i]);
			int WantedSplits = Mathf.CeilToInt(NextPointDistance / 20f); // Split approx every 20 meters

			float LastY = float.MinValue;

			for(int Split=0;Split < WantedSplits;Split++)
			{
				Vector3 RawSplitPosition = Vector3.Lerp(LastPos, TemporaryPath.corners[i], (float)Split / (float)WantedSplits);

				if(!Mathf.Approximately(LastY, float.MinValue))
					RawSplitPosition.y = LastY;

				// Raycast downwards to get closer to the road
				// If the downwards ray fails then we'll trying directly upward instead
				// If either fails then we just fallback to SameplePosition without raycast
				RaycastHit RayHit = new RaycastHit();

				//Debug.DrawRay(RawSplitPosition, Vector3.down * 50f, Color.black, 1f);

				if(Physics.Raycast(RawSplitPosition, Vector3.down, out RayHit, 50f, LayerMask.NameToLayer("Road"))){
					RawSplitPosition = RayHit.point;
				} else if(Physics.Raycast(RawSplitPosition, Vector3.up, out RayHit, 50f, LayerMask.NameToLayer("Road"))){
					RawSplitPosition = RayHit.point;
				}

				NavMeshHit MeshHit = new NavMeshHit();

				if(NavMesh.SamplePosition(RawSplitPosition, out MeshHit, 50f, NavMesh.AllAreas)){
					FinalPaths.Add(MeshHit.position);
					//Debug.DrawRay(MeshHit.position, Vector3.up * 10f, Color.red, 1f);

					LastY = MeshHit.position.y;
				} else {
					Debug.LogError("Failed to SamplePosition D:");
				}

				yield return null; // Wait a frame
			}

			LastPos = TemporaryPath.corners[i];
		}

		/*for(int i=0;i < FinalPaths.Count-1;i++)
		{
			Debug.DrawLine(FinalPaths[i], FinalPaths[i+1], Color.cyan, 1f);
		}*/

		//Debug.Log("Path now has " + FinalPaths.Count + " final points!");

		List<Vector3> PathTempStorage = new List<Vector3>();

		// We now have a temporary path to the destination but we now need to get the center points of all the roads and add them to the PathTempStorage list
		for(int i=0;i < FinalPaths.Count;i++)
		{
			NavMeshHit ClosestEdgeInit = new NavMeshHit();

			// Find the edge closest to the position (this shouldn't fail if the path position is valid, which it should be..)
			if(!NavMesh.FindClosestEdge(FinalPaths[i], out ClosestEdgeInit, NavMesh.AllAreas))
				continue;

			// Because we aren't directly using the path results anymore we need to sample position here cause we probably aren't aligned properly right now
			NavMeshHit ClosestEdge = new NavMeshHit();

			if(!NavMesh.SamplePosition(ClosestEdgeInit.position, out ClosestEdge, 10f, NavMesh.AllAreas))
				continue;

			// Setup the other variables once our other checks are done
			NavMeshHit OtherEdgeFinder = new NavMeshHit();
			NavMeshHit OtherEdge = new NavMeshHit();

			Vector3 MidPoint = Vector3.zero;

			//Debug.DrawRay(ClosestEdge.position, Vector3.up * 3f, Color.green, 1f);

			Vector3 OffMeshPositions = Vector3.zero;
			float OffMeshAngles = 0f; // Also get a median angle so we can figure out which side of the road the ClosestEdge is on
			int TotalOffMeshPositions = 0;

			Vector3 ForwardComparedToPrevious = (PathTempStorage.Count > 0 ? (PathTempStorage[PathTempStorage.Count - 1] - ClosestEdge.position).normalized : -transform.forward);

			// Sample positions around the ClosestEdge position to check which are off the navmesh
			// from that find the center of those points and move in the opposite direction to find the opposite side of the road
			for(int Angle=0;Angle < 360;Angle += 30)
			{
				Vector3 PosToCheck = ClosestEdge.position + (Quaternion.Euler(0f, Angle, 0f) * ForwardComparedToPrevious * 2f);

				RaycastHit AlignmentHit = new RaycastHit();

				if(Physics.Raycast(PosToCheck + Vector3.up * 5f, Vector3.down, out AlignmentHit, 50f)){
					NavMeshHit HitTest = new NavMeshHit();
					if(NavMesh.SamplePosition(AlignmentHit.point, out HitTest, 1f, NavMesh.AllAreas)){
						//Debug.DrawRay(AlignmentHit.point, Vector3.up * 3f, Color.green, 1f);
					} else {
						//Debug.DrawRay(AlignmentHit.point, Vector3.up * 3f, Color.red, 1f);

						OffMeshPositions += AlignmentHit.point;
						OffMeshAngles += Angle;
						TotalOffMeshPositions++;
					}
				} else {
					Debug.LogError("Failed to hit ground alignment raycast!");
				}

				yield return null;
			}

			Vector3 MedianPoint = OffMeshPositions / TotalOffMeshPositions;
			MedianPoint.y = ClosestEdge.position.y; // We don't care about Y, well we do but only because he sends points flying in the air or underground :<

			float MedianAngle = OffMeshAngles / TotalOffMeshPositions;
			//Debug.DrawRay(MedianPoint, Vector3.up * 5f, Color.yellow, 1f);

			Vector3 DirToOtherEdge = (ClosestEdge.position - MedianPoint).normalized;
			//Debug.DrawRay(ClosestEdge.position, DirToOtherEdge * 10f, Color.blue, 1f);

			bool DiffEdgeFound = false;

			for(int EdgeCheckId=1;!DiffEdgeFound && EdgeCheckId < 5;EdgeCheckId++)
			{
				// Convert the edge check positions to a valid navmesh position
				if(!NavMesh.SamplePosition(ClosestEdge.position + (DirToOtherEdge * (5F * EdgeCheckId)), out OtherEdgeFinder, 5f, NavMesh.AllAreas))
					continue;

				// Find the edge closest to OtherEdgeFinder (This shouldn't fail if the Vector3 is a valid NavMesh position)
				if(!NavMesh.FindClosestEdge(OtherEdgeFinder.position, out OtherEdge, NavMesh.AllAreas))
					continue;

				// Make sure we can get a valid position on the navmesh near OtherEdge
				if(!NavMesh.SamplePosition(OtherEdge.position, out OtherEdge, 10f, NavMesh.AllAreas))
					continue;

				//Debug.DrawLine(ClosestEdge.position, OtherEdge.position, Color.cyan, 1f);

				if(Vector3.Distance(ClosestEdge.position, OtherEdge.position) < 5f)
					continue;

				DiffEdgeFound = true;

				//Debug.DrawRay(OtherEdge.position, Vector3.up * 10f, Color.magenta, 1f);

				MidPoint = Vector3.LerpUnclamped(ClosestEdge.position, OtherEdge.position, (MedianAngle < 180f ? 0.25f : 0.75f));

				NavMeshHit FinalMeshHit = new NavMeshHit();

				if(!NavMesh.SamplePosition(MidPoint, out FinalMeshHit, 15f, NavMesh.AllAreas))
					continue;

				MidPoint = FinalMeshHit.position;

				// MidPoint is the center of the valid lanes this vehicle can drive in
				//Debug.DrawRay(MidPoint, Vector3.up * 10f, Color.yellow, 1f);

				PathTempStorage.Add(MidPoint);

				yield return null;
			}

			yield return null;
		}

		// Add the exact wanted point as the final path point (this may not be on the navmesh cause we might be wanting to pull in off the road onto a driveway or something)
		RaycastHit FinalGroundHit = new RaycastHit();

		if(Physics.Raycast(Destination + Vector3.up * 3f, Vector3.down, out FinalGroundHit, 50f)){
			PathTempStorage.Add(FinalGroundHit.point);
		} else {
			Debug.LogError("Failed to raycast final point. Final point not added to path list!");
		}

		// Clear the previous stored path
		PathData.GeneratedPath.Clear();

		for(int i=0;i < PathTempStorage.Count;i++)
			PathData.GeneratedPath.Add(PathTempStorage[i]);

		PathData.HasPathGenerated = true;
		PathData.CurPathId = 0;
		PathData.Path = null;

		if(!SelfVehicleData.IsOnRoad && !IsDestroyed)
			GenerateRejoinPath();
	}

	[ContextMenu("Bring Off Road")]
	public void DebugBringOffRoad()
	{
		OnCollisionEnter(null);
	}

	public void OnWeaponAttack(float Damage)
	{
		if(IsDestroyed) return;

		DamageVehicle(Damage * 5f);

		DetachVehicleFromRails();
	}

	void OnCollisionEnter(Collision Obj)
	{
		if(IsDestroyed) return;

		// Make sure it has atleast been 0.1 seconds since the last impact
		if(TimeSinceLastImpact >= 0.1f){
			TimeSinceLastImpact = 0f;

			TrafficLaneManager TLM = TrafficLaneManager.Instance;
			bool IsAI = false;
			bool IsPlayer = false;

			if(Obj != null){
				IsAI = TLM.AILayer == (TLM.AILayer | (1 << Obj.gameObject.layer));
				IsPlayer = TLM.PlayerLayer == (TLM.PlayerLayer | (1 << Obj.gameObject.layer));

				float CrashForce = Obj.relativeVelocity.magnitude;

				// Create impact sparks
				if(CrashForce >= 10f) TLM.ActivateSparkAt(Obj.contacts[0].point);

				// Play an impact sound
				if(CrashForce >= 30f){
					TLM.HitSource.PlayOneShot(TLM.BigHitSound, Mathf.Clamp(CrashForce / 40f, 0.3f, 2f));
				} else {
					TLM.HitSource.PlayOneShot(TLM.SmallHitSound, Mathf.Clamp(CrashForce / 15f, 0.3f, 1f));
				}

				TLM.HitSource.pitch = Random.Range(0.9f, 1.1f); // Randomly set the pitch of each sound so all crashes don't sound the same
				TLM.HitSource.transform.position = Obj.contacts[0].point; // Make the sound play from the source of the impact

				if(IsPlayer)
					DamageVehicle(CrashForce);
			}

			// Check if we're still on the road
			if(SelfVehicleData.IsOnRoad)
				DetachVehicleFromRails();

			// We need to restore the previous player velocity as the vehicle they hit was kinematic
			// We simulate it having gravity by simply giving it gravity and restoring the previous velocity values
			if(IsPlayer){
				//VehicleManager.Instance.PlayerCollisionRegistered();
				RestorePlayerVelocity();
			}
		}
	}

	private void DetachVehicleFromRails()
	{
		if(IsDestroyed) return;

		switch(TrafficLaneManager.Instance.TrafficActionWhenHit)
		{
			case TrafficLaneManager.ActionWhenHit.NothingIgnoreCollisions:
				
				break;

			case TrafficLaneManager.ActionWhenHit.StopBecomePhysical:
				SetWheelCollidersActive(true);

				// This traffic vehicle was on the road driving
				SelfVehicleData.IsOnRoad = false;
				SetHazardsActive(true);

				SelfRigidbody.isKinematic = false;
				SelfRigidbody.useGravity = true;
				SelfRigidbody.interpolation = RigidbodyInterpolation.None;
				break;

			case TrafficLaneManager.ActionWhenHit.SmartAIDynamicallyRejoin:
				SetWheelCollidersActive(true);

				// This traffic vehicle was on the road driving
				SelfVehicleData.IsOnRoad = false;

				SelfRigidbody.isKinematic = false;
				SelfRigidbody.useGravity = true;

				StartCoroutine(InitialRejoinPathDelay());
				break;

			default:
				Debug.LogError("This TrafficLaneManager action when hit does is not setup on how to handle a collision!");
				break;
		}
	}

	private void RestorePlayerVelocity()
	{
		Debug.Log("Developer notice! Call RestorePlayerVelocity in your vehicle script here!");
		//VehicleManager.Instance.RestoreLastPlayerVelocities();

		/* // Example of RestorePlayerVelocity setup
			private Vector3 LastVelocity;
			private Vector3 LastAngularVelocity;

			public void RestorePlayerVelocity()
			{
				// Replace thios with a reference to your current active vehicle rigidbody
				Rigidbody SelfRigidbody = GetActiveVehicle().SelfRigidbody();

				SelfRigidbody.velocity = LastVelocity;
				SelfRigdbody.angularVelocity = LastAngularVelocity;
			}

			void FixedUpdate()
			{
				// Replace this with a reference to your current active vehicle rigidbody
				Rigidbody SelfRigidbody = GetActiveVehicle().SelfRigibody;

				if(SelfRigidbody != null){
					LastVelocity = SelfRigidbody.velocity;
					LastAngularVelocity = SelfRigidbody.angularVelocity;
				}
			}
		*/
	}

	public void RestoreAIVelocity()
	{
		SelfRigidbody.velocity = LastVelocity;
		SelfRigidbody.angularVelocity = LastAngularVelocity;
	}

	public void SetHazardsActive(bool WantActive)
	{
		IsHazardsActive = WantActive;

		HazardsActiveTransform.localPosition = WantActive ? HazardsActivePos : HazardsDisabledPos;
		HazardsDisabledTrasform.localPosition = WantActive ? HazardsDisabledPos : HazardsActivePos;
	}

	private void UpdateHazards()
	{
		if(IsHazardsActive){
			if(TimeSinceHazardsChanged >= 1f){
				TimeSinceHazardsChanged = 0f;
				HazardsCurrentlyActive = !HazardsCurrentlyActive;

				HazardsActiveTransform.localPosition = HazardsCurrentlyActive ? HazardsActivePos : HazardsDisabledPos;
				HazardsDisabledTrasform.localPosition = HazardsCurrentlyActive ? HazardsDisabledPos : HazardsActivePos;
			} else {
				TimeSinceHazardsChanged -= Time.deltaTime;
			}
		} else if(HazardsCurrentlyActive){
			HazardsCurrentlyActive = false;

			HazardsActiveTransform.localPosition = HazardsDisabledPos;
			HazardsDisabledTrasform.localPosition = HazardsActivePos;
		}
	}

	private void CalculateAntiRoll()
	{
		float LeftRollForce = 0f;
		float RightRollForce = 0f;

		WheelHit Hit = new WheelHit();

		WheelCollider[] Wheels = {
			Config.FrontWheels[0],
			Config.FrontWheels[1],
			Config.RearWheels[0],
			Config.RearWheels[1]
		};

		for(int i=0;i < Wheels.Length;i+=2)
		{
			bool LeftGrounded = Wheels[i].GetGroundHit(out Hit);

			if(LeftGrounded)
				LeftRollForce = (-Wheels[i].transform.InverseTransformPoint(Hit.point).y - Wheels[i].radius) / Wheels[i].suspensionDistance;

			bool RightGrounded = Wheels[i+1].GetGroundHit(out Hit);

			if(RightGrounded)
				RightRollForce = (-Wheels[i+1].transform.InverseTransformPoint(Hit.point).y - Wheels[i+1].radius) / Wheels[i+1].suspensionDistance;

			float AntiRollForce = (LeftRollForce - RightRollForce) * 100f; // Adjust 100 for the antiroll force

			if(LeftGrounded)
				SelfRigidbody.AddForceAtPosition(Wheels[i].transform.up * -AntiRollForce, Wheels[i].transform.position);

			if(RightGrounded)
				SelfRigidbody.AddForceAtPosition(Wheels[i+1].transform.up * AntiRollForce, Wheels[i+1].transform.position);
		}
	}

}