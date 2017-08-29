using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VehicleRefData : MonoBehaviour {

	public List<Transform> Wheels = new List<Transform>();
	public AudioSource HornAudio;
	public GameObject ColliderObj;
	public AICarHandler CarHandlerScript;
	public Rigidbody Rigidbody;
	public Transform HazardLightsOff;
	public Transform HazardLightsOn;
}
