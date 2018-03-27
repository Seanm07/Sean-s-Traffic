using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LaneBezierHandler : MonoBehaviour {

	public Transform EndPoint;
	public bool IsStartPoint;

	public Transform[] BezierPoints = new Transform[0];

	public List<Transform> JointLanes = new List<Transform> ();

	public bool IsIntersection = false;
	public bool WaitForClearIntersection = false;
	public bool DontWaitForThisLane = false;

	[HideInInspector]
	public float CachedLength = 0f;

	public void SwapStartAndEndPositions()
	{
		// First we need to unparent all the objects so they aren't move relative to the start position anymore
		EndPoint.SetParent(null);

		foreach (Transform CurBezier in BezierPoints)
			CurBezier.SetParent (null);

		Vector3 EndPosition = EndPoint.position;
		Vector3 StartPosition = transform.position;

		EndPoint.position = StartPosition;
		transform.position = EndPosition;

		// Reparent everything
		EndPoint.SetParent(transform);

		foreach (Transform CurBezier in BezierPoints)
			CurBezier.SetParent (transform);
	}

	public void SnapPointsToRoadSurface()
	{
		RaycastHit Hit;

		if (Physics.Raycast (transform.position + (Vector3.up * 10f), Vector3.down, out Hit, 1000f, 1 << LayerMask.NameToLayer("Road"))) {
			transform.position = Hit.point + (Vector3.up * 0f);
			Debug.Log (gameObject.name + " snapped to road surface!");
		} else {
			Debug.Log ("Could not find any valid surfaces below " + gameObject.name + " to snap to!");
		}

		if (EndPoint != null) {
			if (Physics.Raycast (EndPoint.position + (Vector3.up * 10f), Vector3.down, out Hit, 1000f, 1 << LayerMask.NameToLayer("Road"))) {
				EndPoint.transform.position = Hit.point + (Vector3.up * 0f);
				Debug.Log (EndPoint.name + " snapped to road surface!");
			} else {
				Debug.Log ("Could not find any valid surfaces below " + EndPoint.name + " to snap to!");
			}
		}

		if (BezierPoints.Length >= 2) {
			if (Physics.Raycast (BezierPoints [1].position + (Vector3.up * 10f), Vector3.down, out Hit, 1000f, 1 << LayerMask.NameToLayer("Road"))) {
				BezierPoints [1].position = Hit.point + (Vector3.up * 0f);
				Debug.Log (BezierPoints [1].name + " snapped to road surface!");
			} else {
				Debug.Log ("Could not find any valid surfaces below " + BezierPoints [1].name + " to snap to!");
			}
		}

		if (BezierPoints.Length >= 1) {
			if (Physics.Raycast (BezierPoints [0].position + (Vector3.up * 10f), Vector3.down, out Hit, 1000f, 1 << LayerMask.NameToLayer("Road"))) {
				BezierPoints [0].position = Hit.point + (Vector3.up * 0f);
 				Debug.Log (BezierPoints [0].name + " snapped to road surface!"); 
			} else {
				Debug.Log ("Could not find any valid surfaces below " + BezierPoints [0].name + " to snap to!");
			}
		}
	}

	public void AddBezierPoint()
	{
		// Create the new array with an increased length of 1
		Transform[] FinalPoints = new Transform[BezierPoints.Length + 1];

		// Iterate through myInts re-assigning the values to finalInts
		for (int i = 0; i < BezierPoints.Length; i++)
			FinalPoints [i] = BezierPoints [i];

		GameObject NewObject = new GameObject ("Bezier " + (FinalPoints.Length - 1));
		NewObject.transform.position = GetPosition (0.333f * FinalPoints.Length);
		NewObject.transform.SetParent (transform);

		// Add the input value to the newly made last key of the finalInts array
		FinalPoints [FinalPoints.Length - 1] =  NewObject.transform;

		// Assign BezierPoints to FinalPoints
		BezierPoints = FinalPoints;
	}

	// EditorLaneInspector.cs removes the array key for us
	public void RemoveBezierPoint(int PointID)
	{
		DestroyImmediate (BezierPoints [PointID].gameObject);

		// Create the new array with an decreased length of 1
		Transform[] FinalPoints = new Transform[BezierPoints.Length - 1];

		// Iterate through myInts re-assigning the values to finalInts
		int FinalPointID = 0;
		for (int i = 0; i < BezierPoints.Length; i++) {
			if (i != PointID) {
				FinalPoints [FinalPointID] = BezierPoints [i];
				FinalPointID++;
			}
		}

		// Assign BezierPoints to FinalPoints
		BezierPoints = FinalPoints;
	}

	#if UNITY_EDITOR
		public void CreateNewLaneFromEndPos()
		{
			GameObject NewLane = new GameObject ("Start Lane 0");
			NewLane.transform.position = EndPoint.position;
			NewLane.AddComponent<LaneBezierHandler> ();

			GameObject NewLaneEnd = new GameObject ("End Lane 0");
			NewLaneEnd.transform.SetParent (NewLane.transform);
			NewLaneEnd.transform.position = NewLane.transform.position + (Vector3.forward * 5f);
			NewLaneEnd.AddComponent<LaneBezierHandler> ();

			UnityEditor.Selection.activeObject = NewLane;
		}

		public void CreateNewLaneFromStartPos()
		{
			GameObject NewLane = new GameObject ("Start Lane 0");
			NewLane.transform.position = transform.position;
			NewLane.AddComponent<LaneBezierHandler> ();

			GameObject NewLaneEnd = new GameObject ("End Lane 0");
			NewLaneEnd.transform.SetParent (NewLane.transform);
			NewLaneEnd.transform.position = transform.position + (Vector3.forward * 5f);
			NewLaneEnd.AddComponent<LaneBezierHandler> ();

			UnityEditor.Selection.activeObject = NewLane;
		}
	#endif

	[HideInInspector]
	[SerializeField]
	public Vector3[] CachedPoints = new Vector3[4]; // Getting the position constantly was expensive and unnessesary

	void Awake()
	{
		if(IsStartPoint && EndPoint != null)
			UpdateCachedPoints ();
	}

	void UpdateCachedPoints()
	{
		if (!IsStartPoint || EndPoint == null) return;

		CachedPoints [0] = transform.position;
		CachedPoints [1] = (BezierPoints.Length >= 1 ? BezierPoints [0].position : Vector3.zero);
		CachedPoints [2] = (BezierPoints.Length >= 2 ? BezierPoints [1].position : Vector3.zero);
		CachedPoints [3] = EndPoint.position;

		#if UNITY_EDITOR
		if(!Application.isPlaying){
			CachedSpherePosGreen = GetPosition(0.25f); //Vector3.Lerp (CachedPoints [0], CachedPoints [3], 0.25f);
			CachedSpherePosOrange = GetPosition(0.5f); //Vector3.Lerp (CachedPoints [0], CachedPoints [3], 0.5f);
			CachedSpherePosRed = GetPosition(0.75f); //Vector3.Lerp (CachedPoints [0], CachedPoints [3], 0.75f);

			//CachedRotationMatrix = Matrix4x4.TRS (transform.position, transform.rotation, transform.lossyScale);

			UnityEditor.Handles.color = IsIntersection ? (WaitForClearIntersection ? Color.black : (DontWaitForThisLane ? new Color(1f, 0.5f, 0.5f) : Color.blue)) : Color.white;

			if (BezierPoints.Length >= 2) {
				UnityEditor.Handles.DrawBezier (CachedPoints [0], CachedPoints [3], CachedPoints [1], CachedPoints [2], IsIntersection ? (WaitForClearIntersection ? Color.black : (DontWaitForThisLane ? new Color(1f, 0.5f, 0.5f) : Color.blue)) : Color.white, null, 2f);
			} else if (BezierPoints.Length >= 1) {
				UnityEditor.Handles.DrawBezier (CachedPoints [0], CachedPoints [3], CachedPoints [1], CachedPoints [1], IsIntersection ? (WaitForClearIntersection ? Color.black : (DontWaitForThisLane ? new Color(1f, 0.5f, 0.5f) : Color.blue)) : Color.white, null, 2f);
			} else {
				UnityEditor.Handles.DrawLine (CachedPoints [0], CachedPoints [3]);
			}

			UnityEditor.HandleUtility.Repaint ();
		}
		#endif
	}

	// Just a function to simply getting bezier points due to the first and last point not being part of the BezierPoints array
	public Vector3 GetPoint(int i)
	{
		// The points aren't cached in the editor outside play mode
		#if UNITY_EDITOR
			if (!Application.isPlaying){
				if (!IsStartPoint || EndPoint == null) return Vector3.zero;

				CachedPoints [0] = transform.position;
				CachedPoints [1] = (BezierPoints.Length >= 1 ? BezierPoints [0].position : Vector3.zero);
				CachedPoints [2] = (BezierPoints.Length >= 2 ? BezierPoints [1].position : Vector3.zero);
				CachedPoints [3] = EndPoint.position;
			}
		#endif

		return CachedPoints [i];
	}

	public void SetPoint(int i, Vector3 NewPos)
	{
		if (i == 0) {
			transform.position = NewPos;
		} else if (i == 1) {
			BezierPoints [0].position = NewPos;
		} else if (i == 2) {
			BezierPoints [1].position = NewPos;
		} else if (i == 3) {
			EndPoint.position = NewPos;
		}

		UpdateCachedPoints ();
	}

	// When a vehicle is in high performance mode they won't use the bezier curve
	public Vector3 GetPosition(float t, bool UseBezierCurve = true)
	{
		// Specifying 0f and 1f to improve performance because this function is called hundreds of times per frame with a lot of vehicles
		if (t == 0f) {
			return GetPoint (0);
		} else if (t == 1f) {
			return GetPoint (3);
		}

		if (UseBezierCurve) {
			if (BezierPoints.Length >= 2) {
				return Bezier.GetPoint (GetPoint (0), GetPoint (1), GetPoint (2), GetPoint (3), t);
			} else if (BezierPoints.Length >= 1) {
				return Bezier.GetPoint (GetPoint (0), GetPoint (1), GetPoint (3), t);
			}
		}

		return Vector3.LerpUnclamped (GetPoint (0), GetPoint (3), t);
	}
		
	public float GetLength(int Accuracy = 10, bool ForceRegen = false)
	{
		if (CachedLength <= 0f || ForceRegen) {
			if (BezierPoints.Length >= 2) {
				CachedLength = Bezier.GetLength (GetPoint (0), GetPoint (1), GetPoint (2), GetPoint (3), Accuracy);
			} else if (BezierPoints.Length >= 1) {
				CachedLength = Bezier.GetLength (GetPoint (0), GetPoint (1), GetPoint (3), Accuracy);
			} else {
				CachedLength = Vector3.Distance (GetPoint (0), GetPoint (3));
			}
		}

		return CachedLength;
	}

	public Vector3 GetVelocity(float t)
	{
		if (BezierPoints.Length >= 2) {
			return Bezier.GetFirstDerivative (GetPoint(0), GetPoint(1), GetPoint(2), GetPoint(3), t);
		} else if (BezierPoints.Length >= 1) {
			return Bezier.GetFirstDerivative (GetPoint(0), GetPoint(1), GetPoint(3), t);
		} else {
			return GetPoint(3) - GetPoint(0);
		}
	}

	[SerializeField]
	private Quaternion[] CachedDir = new Quaternion[6];

	public Quaternion GetDirection(float t)
	{
		int CacheKey = Mathf.RoundToInt(Mathf.Clamp01(t) * 5f);

		#if UNITY_EDITOR
			if(!Application.isPlaying){
				if(CachedDir.Length != 6) CachedDir = new Quaternion[6];

				Vector3 Velocity = GetVelocity (t).normalized;
				Quaternion ReturnValue = Quaternion.identity;

				if (Velocity != Vector3.zero)
					ReturnValue = Quaternion.LookRotation (Velocity);

				CachedDir[CacheKey] = ReturnValue;
			}
		#endif

		//Debug.Log("Requested " + t + " which gave key " + CacheKey + " and returned " + CachedDir[CacheKey]);

		return CachedDir[CacheKey];
	}

	#if UNITY_EDITOR
		public void SetDirty()
		{
			// This is required or the CachedDir and stuff reverts back to before we changed it when restarting or entering play mode..
			UnityEditor.EditorUtility.SetDirty(this);
		}

		private Vector3 CachedSpherePosGreen; 
		private Vector3 CachedSpherePosOrange;
		private Vector3 CachedSpherePosRed;

		//private Matrix4x4 CachedRotationMatrix;

		// Editor function + low time = minimal comments (feelsbadman)
		void OnDrawGizmos()
		{
			if(transform.childCount > 0){
				IsStartPoint = true;
				
				if (EndPoint == null)
					EndPoint = transform.GetComponentsInChildren<LaneBezierHandler> ()[1].transform;

				if (EndPoint == null) {
					Debug.Log ("End point still null on " + name);
				}

				Gizmos.color = new Color (1f, 0.8f, 0f);
				Gizmos.DrawSphere(CachedSpherePosOrange, 1f);

				Gizmos.color = Color.red;
				Gizmos.DrawSphere(CachedSpherePosRed, 1f);

				Gizmos.color = Color.green;
				Gizmos.DrawSphere(CachedSpherePosGreen, 1f);

				UpdateCachedPoints ();
			} else {
				IsStartPoint = false;
			}

			//Gizmos.matrix = CachedRotationMatrix;

			//Gizmos.DrawWireCube (Vector3.zero, Vector3.one);

			
		}
	#endif
}
