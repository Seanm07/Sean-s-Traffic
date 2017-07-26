﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AICarHandler : MonoBehaviour {

	private Rigidbody SelfRigidbody;
	private CarData SelfCarData;

	private bool IsHazardLightsEnabled = false;

	private Transform HazardLights_Enabled;
	private Transform HazardLights_Disabled;

	private bool IsHLEnabled = false;
	private Vector3 HLMovedPos = new Vector3(0f, -10000f, 0f);
	private Vector3 HLOrigPos;
	private float TimeSinceHLChange;

	private Vector3 LastVelocity;
	private Vector3 LastAngularVelocity;

	private float ImpactCooldown = 0f;

	public void Setup(Rigidbody NewRigidbody, CarData NewCarData, Transform InHazardLightsOff, Transform InHazardLightsOn)
	{
		SelfRigidbody = NewRigidbody;
		SelfCarData = NewCarData;

		HazardLights_Enabled = InHazardLightsOn;
		HazardLights_Disabled = InHazardLightsOff;

		HLOrigPos = HazardLights_Disabled.localPosition;

		ImpactCooldown = 0f;
	}

	void OnCollisionEnter(Collision Obj)
	{
		// The impact cooldown is set after a full collision so multiple full collisions aren't quickly spammed causing lag
		if (ImpactCooldown > 0f) return;

		bool isAi = TrafficLaneManager.Instance.AILayer == (TrafficLaneManager.Instance.AILayer | (1 << Obj.gameObject.layer));
		bool isPlayer = TrafficLaneManager.Instance.PlayerLayer == (TrafficLaneManager.Instance.PlayerLayer | (1 << Obj.gameObject.layer));

		if (isPlayer && SelfCarData.IsOnRoad && TrafficLaneManager.Instance.CarsStopWhenHit) {
			SelfCarData.IsOnRoad = false;
			SetHazardLights (true);

			SelfRigidbody.isKinematic = false;
			SelfRigidbody.useGravity = true;

			VehicleManager.Instance.RestoreLastPlayerVelocities ();
			// vvv [The above function looks like this] vvv //

			// Start TrafficLaneManager crash restoration //
			/*private Vector3 LastVelocity;
			private Vector3 LastAngularVelocity;

			public void RestoreLastPlayerVelocities()
			{
				// Replace this with a reference to your current active vehicle rigidbody
				Rigidbody SelfRigidbody = GetActiveVehicle().SelfRigidbody;

				SelfRigidbody.velocity = LastVelocity;
				SelfRigidbody.angularVelocity = LastAngularVelocity;
			}

			void FixedUpdate()
			{
				// Replace this with a reference to your current active vehicle rigidbody
				Rigidbody SelfRigidbody = GetActiveVehicle().SelfRigidbody;

				if(SelfRigidbody != null){
					LastVelocity = SelfRigidbody.velocity;
					LastAngularVelocity = SelfRigidbody.angularVelocity;
				}
			}*/
			// End TrafficLaneManager crash restoration //

			SelfCarData.VehicleRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
		} else {
			if (isPlayer || isAi) {
				TrafficLaneManager TLM = TrafficLaneManager.Instance;

				float CrashForce = Obj.relativeVelocity.magnitude;

				if (CrashForce >= 10f) {
					TLM.ActivateSparkAt (Obj.contacts [0].point);

					// Add some explosion force to the vehicle we just hit to make it more dramatic
					//SelfRigidbody.AddExplosionForce (10000f * (CrashForce / 10f), Obj.contacts [0].point, 20f);
				}

				if (CrashForce >= 30f) {
					TLM.HitSource.PlayOneShot (TLM.HitSound, Mathf.Clamp (CrashForce / 40f, 0.3f, 2f));
					TLM.HitSource.pitch = Random.Range (0.9f, 1.1f); // So each crash doesn't sound exactly the same
					TLM.HitSource.transform.position = Obj.contacts [0].point;
				} else {
					TLM.HitSource.PlayOneShot (TLM.SmallHitSound, Mathf.Clamp (CrashForce / 15f, 0.3f, 1f));
					TLM.HitSource.pitch = Random.Range (0.9f, 1.1f); // So each crash doesn't sound exactly the same
					TLM.HitSource.transform.position = Obj.contacts [0].point;
				}

				ImpactCooldown = 0.1f;
			}
		}
	}

	public void SetHazardLights(bool DoEnable)
	{
		IsHazardLightsEnabled = DoEnable;

		if (!DoEnable)
			ImpactCooldown = 0f;
	}

	void Update()
	{
		if (IsHazardLightsEnabled) {
			if (TimeSinceHLChange >= 0.5f) {
				IsHLEnabled = !IsHLEnabled;
				HazardLights_Enabled.localPosition = IsHLEnabled ? HLOrigPos : HLMovedPos;
				HazardLights_Disabled.localPosition = IsHLEnabled ? HLMovedPos : HLOrigPos;
				TimeSinceHLChange = 0f;
			} else {
				TimeSinceHLChange += Time.deltaTime;
			}

			if (ImpactCooldown > 0f)
				ImpactCooldown = (ImpactCooldown - Time.deltaTime < 0f ? 0f : ImpactCooldown - Time.deltaTime);
		} else if (IsHLEnabled) {
			IsHLEnabled = false;
			HazardLights_Enabled.localPosition = HLMovedPos;
			HazardLights_Disabled.localPosition = HLOrigPos;
		}
	}
}